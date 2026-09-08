using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
}
