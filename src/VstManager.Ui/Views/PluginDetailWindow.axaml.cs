using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using VstManager.Core.Models;
using VstManager.Ui.Services;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Details / Files for one plugin. The view model stays dialog-free, so every confirmation and
/// validation message lives here.
/// </summary>
public partial class PluginDetailWindow : Window
{
    private readonly PluginDetailViewModel _vm;

    private readonly MainViewModel _main;

    public PluginDetailWindow(MainViewModel main, PluginCardViewModel plugin)
    {
        InitializeComponent();
        _main = main;
        _vm = new PluginDetailViewModel(main, plugin);
        _vm.CloseRequested += (_, _) => Close();

        // The view model stays dialog-free and owns no Avalonia types, so the picker and the
        // image decoding are supplied from here.
        _vm.CandidateChooser = candidates => MatchPickerWindow.PickAsync(this, _vm.Plugin.Name, candidates);
        _vm.LogoPreviewLoader = LoadLogoPreviewAsync;

        Closed += (_, _) => CleanupLogoPreviewFile();

        DataContext = _vm;
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!await _vm.SaveAsync())
        {
            await ConfirmDialog.ShowMessageAsync(this, "Fix Metadata", "Name can't be empty.");
            return;
        }

        // Everything else already saved and the window has closed; this only reports the part
        // that didn't take.
        if (_vm.SaveWarning is not null)
        {
            await ConfirmDialog.ShowMessageAsync(this, "Fix Metadata", _vm.SaveWarning);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void CategoryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CategoryDefinition category })
        {
            _vm.Form.SelectedCategoryId = category.Id;
        }
    }

    private void ShowInFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string path })
        {
            Reveal.InFolder(path);
        }
    }

    /// <summary>
    /// Excludes just this one install location from future scans (the file stays on disk). The
    /// rescan that follows rebuilds every card, so this window's copy is stale afterwards and
    /// its list cannot refresh in place — it closes on success, leaving a correct list to reopen.
    /// </summary>
    private async void RemoveFromScanning_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string path })
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(this, "Remove from Scanning",
            $"Remove this location from scanning?\n\n{Path.GetFileName(path)}\n\n"
            + "The file stays on disk, but VST Manager will skip it in all future scans on this "
            + "and any other machine running this app.",
            "Remove");

        if (confirmed && await _vm.ExcludePathAsync(path))
        {
            Close();
        }
    }

    private async void MarkAsNotAPlugin_Click(object? sender, RoutedEventArgs e)
    {
        var fileNames = string.Join("\n", _vm.Plugin.ActiveInstalls.Select(i => Path.GetFileName(i.Path)));

        var confirmed = await ConfirmDialog.ShowAsync(this, "Mark as Not a Plugin",
            $"Mark the following as not a plugin?\n\n{fileNames}\n\n"
            + "They will be excluded from all future scans on this and any other machine running this app.",
            "Exclude");

        if (confirmed && await _vm.MarkAsNotAPluginAsync())
        {
            Close();
        }
    }

    // ---- artwork -----------------------------------------------------------

    /// <summary>Formats Avalonia can decode. WebP is deliberately absent — it has no decoder.</summary>
    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <summary>
    /// Downloads a URL to a temp file and shows it, so the user sees the artwork before Save
    /// commits it. Returns whether it actually decoded — Save only writes a preview that worked.
    /// </summary>
    private async Task<bool> LoadLogoPreviewAsync(string url)
    {
        var localPath = await _main.PreviewLogoAsync(url);

        if (localPath is null)
        {
            _vm.Form.IsLogoPreviewValid = false;
            return false;
        }

        return ShowPreview(localPath, isTempFile: true);
    }

    /// <summary>
    /// Decodes an image into the preview box. A file Avalonia can't read is reported rather than
    /// thrown: an unsupported format is a normal thing for a user to pick.
    /// </summary>
    private bool ShowPreview(string path, bool isTempFile)
    {
        try
        {
            var bitmap = new Bitmap(path);

            var previous = LogoPreviewImage.Source as Bitmap;
            LogoPreviewImage.Source = bitmap;
            previous?.Dispose();

            _vm.Form.IsLogoPreviewValid = true;

            if (isTempFile)
            {
                CleanupLogoPreviewFile();
                _vm.Form.LogoPreviewLocalPath = path;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            _vm.Form.IsLogoPreviewValid = false;
            _vm.Form.LogoStatusText = "That image couldn't be read. PNG, JPG, BMP and GIF work.";
            return false;
        }
    }

    private async void BrowseForImage_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an image for this plugin",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            UseLocalImageFile(path);
        }
    }

    private void ImageDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDroppedImagePath(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ImageDropTarget_Drop(object? sender, DragEventArgs e)
    {
        var path = TryGetDroppedImagePath(e);
        if (path is not null)
        {
            UseLocalImageFile(path);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Exactly one file, with an extension Avalonia can actually decode. Reads the drop through
    /// DataTransfer rather than the older Data/IDataObject, which Avalonia 12 replaced.
    /// </summary>
    private static string? TryGetDroppedImagePath(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();

        if (files is not { Length: 1 })
        {
            return null;
        }

        var path = files[0].TryGetLocalPath();

        return path is not null
               && SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            ? path
            : null;
    }

    /// <summary>
    /// Stages a local image. Clears LogoUrl because the two are mutually exclusive — Save prefers
    /// the file, so leaving a stale URL behind would be misleading in the form.
    /// </summary>
    private void UseLocalImageFile(string path)
    {
        if (!ShowPreview(path, isTempFile: false))
        {
            return;
        }

        _vm.Form.LocalLogoFilePath = path;
        _vm.Form.LogoUrl = null;
        _vm.Form.LogoStatusText = $"Using {Path.GetFileName(path)}. Click Save to keep it.";
    }

    /// <summary>Deletes the downloaded preview temp file; wired to Closed as well as each reload.</summary>
    private void CleanupLogoPreviewFile()
    {
        var path = _vm.Form.LogoPreviewLocalPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _vm.Form.LogoPreviewLocalPath = null;

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless; the OS clears it eventually.
        }
    }
}
