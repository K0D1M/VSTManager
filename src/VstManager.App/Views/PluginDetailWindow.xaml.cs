using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VstManager.App.ViewModels;
using VstManager.Core.Models;

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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
            else if (result.WebLookupResult is not null)
            {
                var web = result.WebLookupResult;
                Form.Name = web.ProductName;
                Form.Vendor = web.Vendor;
                messages.Add($"Not in the local catalog, but found online: {web.ProductName} ({web.Vendor}).");

                if (web.LogoUrl is not null)
                {
                    Form.LogoUrl = web.LogoUrl;
                    await LoadLogoPreviewAsync(web.LogoUrl);
                    messages.Add("Logo loaded below — review it, then Save.");
                }
            }
            else
            {
                messages.Add("No catalog match found, and an online KVR Audio search didn't find a confident match either — set Name/Vendor manually above.");
            }

            Form.AutoDetectStatusText = string.Join(" ", messages);
        }
        finally
        {
            Form.IsAutoDetecting = false;
        }
    }

    private void SearchKvr_Click(object sender, RoutedEventArgs e)
    {
        var query = string.IsNullOrWhiteSpace(_plugin.Vendor)
            ? _plugin.Name
            : $"{_plugin.Name} {_plugin.Vendor}";

        // KVR's own site search ("Quick Search") requires being logged into a KVR account just
        // to view results — without that it shows a login prompt instead of anything useful.
        // Searching via DuckDuckGo restricted to kvraudio.com/product pages works without any
        // login and reliably lands on the right product page.
        var searchUrl = "https://duckduckgo.com/?q=" + Uri.EscapeDataString($"site:kvraudio.com/product {query}");
        Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
        Form.LogoStatusText = "Open the matching product page on KVR, right-click its box art, choose \"Copy image address\", then paste it below.";
    }

    private void SearchWeb_Click(object sender, RoutedEventArgs e)
    {
        var query = string.IsNullOrWhiteSpace(_plugin.Vendor)
            ? $"{_plugin.Name} vst plugin logo"
            : $"{_plugin.Name} {_plugin.Vendor} vst plugin logo";

        var searchUrl = "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query);
        Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
        Form.LogoStatusText = "Find an image in your browser, right-click it, choose \"Copy image address\", then paste it below.";
    }

    private void LogoUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Form.IsLogoPreviewValid = false;
        LogoPreviewImage.Visibility = Visibility.Collapsed;
        LogoPlaceholderText.Visibility = Visibility.Visible;
        LogoPlaceholderText.Text = "Paste an image URL below and click Preview";
        Form.LogoStatusText = string.Empty;
        CleanupLogoPreviewFile();
    }

    private async void PreviewLogo_Click(object sender, RoutedEventArgs e)
    {
        var url = (Form.LogoUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Form.LogoStatusText = "Enter a valid image URL first.";
            return;
        }

        await LoadLogoPreviewAsync(url);
    }

    private async Task LoadLogoPreviewAsync(string url)
    {
        Form.LogoStatusText = "Loading preview...";
        Form.IsLogoPreviewValid = false;

        var localPath = await _mainViewModel.PreviewLogoAsync(url);
        CleanupLogoPreviewFile();
        _logoPreviewLocalPath = localPath;

        if (localPath is null)
        {
            Form.LogoStatusText = "Couldn't download an image from that URL.";
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
            LogoPreviewImage.Visibility = Visibility.Visible;
            LogoPlaceholderText.Visibility = Visibility.Collapsed;
            Form.LogoStatusText = "Looks good? Click Save to keep it.";
            Form.IsLogoPreviewValid = true;
        }
        catch (NotSupportedException)
        {
            Form.LogoStatusText = "That image format isn't supported. Try a different image (JPG or PNG work best).";
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
