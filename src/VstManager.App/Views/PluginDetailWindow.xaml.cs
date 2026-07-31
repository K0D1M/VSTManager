using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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

        if (Form.IsLogoPreviewValid && !string.IsNullOrWhiteSpace(Form.LogoUrl))
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
