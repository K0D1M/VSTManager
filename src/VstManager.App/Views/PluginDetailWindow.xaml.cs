using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.App.ViewModels;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.App.Views;

public partial class PluginDetailWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly PluginDisplayViewModel _plugin;
    private string? _logoPreviewLocalPath;

    public PluginEditFormViewModel Form { get; }

    public PluginDetailWindow(MainViewModel mainViewModel, PluginDisplayViewModel plugin)
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
        WindowIcon.ApplyDefault(this);
        _mainViewModel = mainViewModel;
        _plugin = plugin;
        Form = new PluginEditFormViewModel(plugin);
        DataContext = plugin;

        if (_mainViewModel.ShouldShowLogoInstructions)
        {
            Form.LogoStatusText = "First time fixing a logo? Click \"Search KVR Audio\" or \"Search the Web\" below, "
                + "right-click the image you want, choose \"Copy image address\", then paste it here and click Preview.";
            _mainViewModel.MarkLogoInstructionsSeen();
        }

        Closed += (_, _) => CleanupLogoPreviewFile();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Form.Name))
        {
            MessageBox.Show("Name can't be empty.", "Fix Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Form.Name.Trim() != _plugin.Name || (Form.Vendor?.Trim() ?? string.Empty) != (_plugin.Vendor ?? string.Empty))
        {
            _mainViewModel.ApplyMetadataOverride(_plugin, Form.Name, Form.Vendor);
        }

        _mainViewModel.SetVersions(_plugin, Form.CurrentVersion, Form.LatestVersion);

        if (Form.Kind != _plugin.Kind)
        {
            if (Form.Kind == PluginKind.Instrument)
            {
                _mainViewModel.MarkAsInstrumentCommand.Execute(_plugin);
            }
            else if (Form.Kind == PluginKind.Effect)
            {
                _mainViewModel.MarkAsEffectCommand.Execute(_plugin);
            }
        }

        if (Form.SelectedTag == PluginTag.Legit)
        {
            _mainViewModel.MarkLegitCommand.Execute(_plugin);
        }
        else if (Form.SelectedTag == PluginTag.Cracked)
        {
            _mainViewModel.MarkCrackedCommand.Execute(_plugin);
        }

        if (!string.IsNullOrWhiteSpace(Form.LocalLogoFilePath))
        {
            var success = await _mainViewModel.SetLogoFromLocalFileAsync(_plugin, Form.LocalLogoFilePath);
            if (!success)
            {
                MessageBox.Show("Couldn't save the image. The rest of your changes were saved — try again.",
                    "Fix Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (Form.IsLogoPreviewValid && !string.IsNullOrWhiteSpace(Form.LogoUrl))
        {
            var success = await _mainViewModel.FixLogoAsync(_plugin, Form.LogoUrl.Trim());
            if (!success)
            {
                MessageBox.Show("Couldn't save the logo. The rest of your changes were saved — try the logo again.",
                    "Fix Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => WindowSizing.FitToScreen(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            _mainViewModel.ShowPathInFolderCommand.Execute(path);
        }
    }

    private async void MarkAsNotAPlugin_Click(object sender, RoutedEventArgs e)
    {
        var excluded = await _mainViewModel.MarkAsNotAPluginAsync(_plugin);
        if (excluded)
        {
            Close();
        }
    }

    private void InstrumentRadio_Checked(object sender, RoutedEventArgs e) => Form.Kind = PluginKind.Instrument;

    private void EffectRadio_Checked(object sender, RoutedEventArgs e) => Form.Kind = PluginKind.Effect;

    private void LegitRadio_Checked(object sender, RoutedEventArgs e) => Form.SelectedTag = PluginTag.Legit;

    private void CrackedRadio_Checked(object sender, RoutedEventArgs e) => Form.SelectedTag = PluginTag.Cracked;

    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        Form.IsAutoDetecting = true;
        Form.AutoDetectStatusText = "Looking...";

        try
        {
            var result = await _mainViewModel.PreviewAutoDetectAsync(_plugin);
            var messages = new List<string>();

            if (result.DetectedCurrentVersion is null)
            {
                messages.Add("Couldn't detect a version from the file or the Windows registry.");
            }
            else if (string.IsNullOrWhiteSpace(Form.CurrentVersion))
            {
                Form.CurrentVersion = result.DetectedCurrentVersion;
                messages.Add($"Filled in version {result.DetectedCurrentVersion}.");
            }
            else if (!string.Equals(Form.CurrentVersion.Trim(), result.DetectedCurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"Detected version {result.DetectedCurrentVersion}, but the field above already has \"{Form.CurrentVersion}\" — edit it yourself if you want the detected one.");
            }
            else
            {
                messages.Add("Version is already up to date.");
            }

            if (result.CatalogMatchIsNew && result.MatchedCatalogEntry is not null)
            {
                Form.Name = result.MatchedCatalogEntry.Name;
                Form.Vendor = result.MatchedCatalogEntry.Vendor;
                messages.Add($"Found a catalog match: {result.MatchedCatalogEntry.Name} ({result.MatchedCatalogEntry.Vendor}).");
            }
            else if (result.MatchedCatalogEntry is not null)
            {
                messages.Add("Already matched to the right catalog entry.");
            }

            // When the online search wasn't sure — weak top hit, or two near-equal hits — put
            // the choice to the user rather than silently applying a guess.
            var chosen = result.WebLookupResult;
            if (result.NeedsUserChoice && result.WebCandidates.Count > 0)
            {
                var picker = new MatchPickerWindow(_plugin.Name, result.WebCandidates) { Owner = this };
                picker.ShowDialog();
                chosen = picker.SelectedInfo;

                messages.Add(chosen is null
                    ? "Online search wasn't conclusive and no match was chosen."
                    : $"Using your pick: {chosen.ProductName}.");
            }

            if (chosen is not null)
            {
                // A curated catalog entry wins on identity; the web only supplies the version
                // (and artwork) for plugins the catalog already knows.
                if (result.MatchedCatalogEntry is null)
                {
                    await ApplyFetchedInfoAsync(chosen, "the online match");
                }
                else if (!string.IsNullOrWhiteSpace(chosen.LatestVersion))
                {
                    Form.LatestVersion = chosen.LatestVersion;
                }

                if (!string.IsNullOrWhiteSpace(chosen.LatestVersion))
                {
                    messages.Add($"Latest version online: {chosen.LatestVersion}.");
                }

                if (chosen.SourceUrl is not null)
                {
                    Form.InfoUrl = chosen.SourceUrl;
                }
            }
            else if (result.MatchedCatalogEntry is null && result.WebCandidates.Count == 0)
            {
                messages.Add("No catalog match, and nothing convincing found online — set Name/Vendor manually above, "
                             + "or paste the plugin's product page address below.");
            }

            Form.AutoDetectStatusText = string.Join(" ", messages);
        }
        finally
        {
            Form.IsAutoDetecting = false;
        }
    }

    /// <summary>
    /// Re-reads the installed version from disk on demand. Runs the same fallback chain the
    /// automatic detection uses — each copy's file metadata (Windows version resource, then
    /// VST3 moduleinfo.json, then the vendor's bundle manifest), then the Windows uninstall
    /// registry — and reports which one answered, so a surprising number is traceable.
    /// </summary>
    private async void DetectCurrentVersion_Click(object sender, RoutedEventArgs e)
    {
        Form.IsDetectingCurrentVersion = true;
        Form.AutoDetectStatusText = "Reading the installed version...";

        try
        {
            var result = await _mainViewModel.DetectCurrentVersionAsync(_plugin);

            if (result.Version is null)
            {
                Form.AutoDetectStatusText = $"Couldn't find a version in {result.SourceDescription}. "
                                            + "Some plugins don't record one anywhere — you can type it in yourself.";
                return;
            }

            var previous = Form.CurrentVersion?.Trim();
            Form.CurrentVersion = result.Version;

            Form.AutoDetectStatusText = string.Equals(previous, result.Version, StringComparison.OrdinalIgnoreCase)
                ? $"Confirmed version {result.Version} from {result.SourceDescription}."
                : $"Found version {result.Version} in {result.SourceDescription}. Click Save to keep it.";
        }
        finally
        {
            Form.IsDetectingCurrentVersion = false;
        }
    }

    /// <summary>
    /// Lets the user force-correct a bad match by searching directly and choosing from the
    /// results — unlike Auto-Detect, this always shows the picker, even for a single strong
    /// hit, since the entire point is overriding whatever the automatic match got wrong (e.g.
    /// Auto-Detect matching "Omnisphere" to Omnisphere 1 when Omnisphere 3 is installed).
    /// Searches on the current Name/Vendor fields, so editing them first narrows the search.
    /// </summary>
    private async void SearchManually_Click(object sender, RoutedEventArgs e)
    {
        var name = (Form.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Form.AutoDetectStatusText = "Enter a name above first, then search.";
            return;
        }

        Form.IsSearchingManually = true;
        Form.AutoDetectStatusText = "Searching...";

        try
        {
            var candidates = await _mainViewModel.SearchCandidatesAsync(name, Form.Vendor?.Trim());
            if (candidates.Count == 0)
            {
                Form.AutoDetectStatusText = $"No matches found for \"{name}\" online.";
                return;
            }

            var picker = new MatchPickerWindow(name, candidates) { Owner = this };
            picker.ShowDialog();

            var chosen = picker.SelectedInfo;
            if (chosen is null)
            {
                Form.AutoDetectStatusText = "No match chosen — nothing changed.";
                return;
            }

            await ApplyFetchedInfoAsync(chosen, "your search");

            // ApplyFetchedInfoAsync reports into LogoStatusText (the Web Info tab); mirror the
            // same confirmation here since this button lives on the Details tab.
            Form.AutoDetectStatusText = $"Applied \"{chosen.ProductName}\" — review the fields above, then click Save.";
        }
        finally
        {
            Form.IsSearchingManually = false;
        }
    }

    /// <summary>
    /// Reads plugin details straight off a product page the user pasted. Replaces the old
    /// "search the web, then copy an image address" flow: the page yields name, vendor,
    /// version and artwork in one step, and works for KVR or any other plugin database.
    /// </summary>
    private async void FetchInfo_Click(object sender, RoutedEventArgs e)
    {
        var url = (Form.InfoUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Form.LogoStatusText = "Paste a full web address first (starting with https://).";
            return;
        }

        Form.IsFetchingInfo = true;
        Form.LogoStatusText = "Reading that page...";

        try
        {
            var info = await _mainViewModel.FetchInfoFromUrlAsync(url);
            if (info is null)
            {
                Form.LogoStatusText = "Couldn't read plugin details from that page. Check the address, "
                                      + "or try the plugin's page on kvraudio.com.";
                return;
            }

            await ApplyFetchedInfoAsync(info, "that page");
        }
        finally
        {
            Form.IsFetchingInfo = false;
        }
    }

    /// <summary>
    /// Fills the form from a fetched/chosen result. Only overwrites fields the source actually
    /// supplied, so a page missing (say) a version never blanks out a good existing value.
    /// </summary>
    private async Task ApplyFetchedInfoAsync(KvrLookupResult info, string sourceLabel)
    {
        var applied = new List<string>();

        if (!string.IsNullOrWhiteSpace(info.ProductName))
        {
            Form.Name = info.ProductName;
            applied.Add("name");
        }

        if (!string.IsNullOrWhiteSpace(info.Vendor))
        {
            Form.Vendor = info.Vendor;
            applied.Add("vendor");
        }

        if (!string.IsNullOrWhiteSpace(info.LatestVersion))
        {
            Form.LatestVersion = info.LatestVersion;
            applied.Add("latest version");
        }

        if (!string.IsNullOrWhiteSpace(info.LogoUrl))
        {
            Form.LogoUrl = info.LogoUrl;
            await LoadLogoPreviewAsync(info.LogoUrl!);
            if (Form.IsLogoPreviewValid)
            {
                applied.Add("artwork");
            }
        }

        Form.LogoStatusText = applied.Count == 0
            ? $"Nothing usable found on {sourceLabel}."
            : $"Filled in {string.Join(", ", applied)} from {sourceLabel}. Review above, then click Save.";
    }

    /// <summary>
    /// Downloads the artwork found on a fetched page and shows it, so the user sees what will
    /// be saved. Failures are non-fatal — the rest of the fetched details still apply.
    /// </summary>
    private async Task LoadLogoPreviewAsync(string url)
    {
        Form.IsLogoPreviewValid = false;
        Form.LocalLogoFilePath = null;

        var localPath = await _mainViewModel.PreviewLogoAsync(url);
        CleanupLogoPreviewFile();
        _logoPreviewLocalPath = localPath;

        if (localPath is null)
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            LogoPreviewImage.Source = bitmap;
            Form.IsLogoPreviewValid = true;
        }
        catch (NotSupportedException)
        {
            // Unsupported image format — leave the preview hidden; other details still applied.
        }
    }

    /// <summary>
    /// Lets the user pick an image straight from disk — the escape hatch for when the plugin's
    /// current artwork (or a fetched URL's) fails to display, e.g. an unsupported format like
    /// WebP that WPF can't decode. Picking a file here takes priority over LogoUrl on Save.
    /// </summary>
    private void BrowseForImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an Image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        UseLocalImageFile(dialog.FileName);
    }

    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <summary>Only accept drags that are a single, image-extensioned file — anything else
    /// (multiple files, folders, other data) falls through without changing the cursor.</summary>
    private void ImageDropTarget_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetDroppedImagePath(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImageDropTarget_Drop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (TryGetDroppedImagePath(e, out var path))
        {
            UseLocalImageFile(path);
        }
    }

    private static bool TryGetDroppedImagePath(System.Windows.DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
        {
            return false;
        }

        if (!SupportedImageExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant()))
        {
            return false;
        }

        path = files[0];
        return true;
    }

    /// <summary>
    /// Applies a local image file as the pending artwork — the shared endpoint for both the
    /// "Browse..." dialog and dropping a file onto the artwork card. Picking/dropping a file
    /// here takes priority over LogoUrl on Save.
    /// </summary>
    private void UseLocalImageFile(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            LogoPreviewImage.Source = bitmap;
            Form.IsLogoPreviewValid = true;
            Form.LocalLogoFilePath = filePath;
            Form.LogoUrl = null;
            Form.LogoStatusText = "Using this image from your computer. Click Save to keep it.";
        }
        catch (NotSupportedException)
        {
            MessageBox.Show("That file isn't a supported image format. Try a PNG, JPG, BMP or GIF instead.",
                "Choose an Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CleanupLogoPreviewFile()
    {
        if (_logoPreviewLocalPath is not null && File.Exists(_logoPreviewLocalPath))
        {
            try
            {
                File.Delete(_logoPreviewLocalPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover preview file is harmless.
            }
        }

        _logoPreviewLocalPath = null;
    }
}
