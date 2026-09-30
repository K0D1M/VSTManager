using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VstManager.Core.Models;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Settings. File and folder pickers go through Avalonia's StorageProvider rather than
/// Microsoft.Win32 dialogs, which is what keeps this window buildable off Windows.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    // ---- category drag-to-reorder --------------------------------------------
    // Pointer capture on the list, not DragDrop: the row moves live as the pointer crosses its
    // neighbours. Capture sits on the list rather than the handle because moving an item can
    // recreate its container, which would drop a capture held by the handle.

    private CategoryDefinition? _draggedCategory;

    private void CategoryHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CategoryDefinition { IsPreset: false } category }
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggedCategory = category;
        e.Pointer.Capture(CategoryList);
        SetDraggedRowDimmed(true);
        e.Handled = true;
    }

    private void CategoryList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedCategory is null)
        {
            return;
        }

        var y = e.GetPosition(CategoryList).Y;
        foreach (var container in CategoryList.GetRealizedContainers())
        {
            var top = container.TranslatePoint(new Point(0, 0), CategoryList)?.Y;
            if (top is { } t && y >= t && y < t + container.Bounds.Height)
            {
                SetDraggedRowDimmed(false);
                _vm.MoveCategoryTo(_draggedCategory, CategoryList.IndexFromContainer(container));
                SetDraggedRowDimmed(true);
                break;
            }
        }
    }

    private void CategoryList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedCategory is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        EndCategoryDrag();
    }

    private void CategoryList_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndCategoryDrag();

    private void EndCategoryDrag()
    {
        if (_draggedCategory is null)
        {
            return;
        }

        SetDraggedRowDimmed(false);
        _draggedCategory = null;
        _vm.CommitCategoryOrder();
    }

    private void SetDraggedRowDimmed(bool dimmed)
    {
        if (_draggedCategory is not null && CategoryList.ContainerFromItem(_draggedCategory) is { } row)
        {
            row.Opacity = dimmed ? 0.5 : 1;
        }
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan for plugins",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _vm.AddScanFolder(path);
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export VST Manager Data",
            SuggestedFileName = $"vstmanager-backup-{DateTime.Now:yyyy-MM-dd}.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("VST Manager backup") { Patterns = ["*.json"] }]
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, _vm.ExportBundle());
            _vm.DataStatus = $"Exported to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.DataStatus = $"Couldn't write that file: {ex.Message}";
        }
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import VST Manager Data",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("VST Manager backup") { Patterns = ["*.json"] }]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(this, "Import Data",
            "This replaces your current library, tags and corrections with the contents of "
            + "that file.\n\nThis can't be undone. Continue?",
            "Import");

        if (!confirmed)
        {
            return;
        }

        try
        {
            _vm.ImportBundle(await File.ReadAllTextAsync(path));
            _vm.DataStatus = "Imported. Your library has been reloaded.";
        }
        catch (InvalidDataException ex)
        {
            _vm.DataStatus = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.DataStatus = $"Couldn't read that file: {ex.Message}";
        }
    }

    /// <summary>
    /// Confirmed rather than bound straight to the command: this replaces everything on this
    /// machine with the cloud copy, and there is no undo.
    /// </summary>
    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmDialog.ShowAsync(this, "Restore From Cloud",
            "This replaces your library, tags and corrections on this machine with the cloud "
            + "copy.\n\nAnything changed here since the last sync is lost. Continue?",
            "Restore");

        if (confirmed)
        {
            await _vm.RestoreCommand.ExecuteAsync(null);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    // ---- cloud ------------------------------------------------------------------

    /// <summary>
    /// The device id was already selectable/manually-copyable via SelectableTextBlock; this just
    /// removes the need to select-then-Ctrl+C by hand for a value the user is meant to paste
    /// somewhere else (another machine's "Use a different sync id" field).
    /// </summary>
    private async void CopyDeviceId_Click(object? sender, RoutedEventArgs e)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_vm.CloudDeviceId);
        }
    }

    // ---- about ----------------------------------------------------------------

    /// <summary>No Skip here: choosing Settings → Update means the user wants it now.</summary>
    private async void UpdateNow_Click(object? sender, RoutedEventArgs e) =>
        await UpdateAvailableWindow.ShowAsync(this, _vm.Main, offerSkip: false);

    private void AuthorLink_Click(object? sender, PointerPressedEventArgs e) => OpenUrl("https://github.com/K0D1M");

    private void RepoLink_Click(object? sender, PointerPressedEventArgs e) =>
        OpenUrl("https://github.com/K0D1M/VSTManager");

    /// <summary>
    /// Same portable open-in-browser pattern already used for KVR product pages
    /// (MatchPickerWindow.axaml.cs's ViewPage_Click) — UseShellExecute lets the OS resolve the
    /// default browser on both Windows and macOS rather than hardcoding one.
    /// </summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser, or a URL the shell refuses — not worth interrupting Settings over.
        }
    }
}
