using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.App.Services;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.App.ViewModels;

public enum ManagementMode
{
    All,
    Legit,
    Cracked
}

public enum InstalledFilterOption
{
    All,
    InstalledOnly,
    NotInstalledOnly
}

public enum FormatFilterOption
{
    All,
    Vst2,
    Vst3
}

public enum LayoutMode
{
    Grid,
    List
}

public partial class MainViewModel : ObservableObject
{
    private readonly ScanPathProvider _scanPathProvider = new();
    private readonly ExclusionListService _exclusionList = new();
    private readonly PluginScanner _scanner;
    private readonly LibraryStore _libraryStore = new();
    private readonly PluginCatalog _catalog = new();
    private readonly PluginDisplayBuilder _displayBuilder = new();
    private readonly LogoCache _logoCache = new();
    private readonly ManualLogoOverrideService _manualLogoOverrides = new();
    private readonly ManualMetadataOverrideService _manualMetadataOverrides = new();
    private readonly KvrLookupService _kvrLookup = new();
    private readonly UninstallerLookup _uninstallerLookup = new();
    private readonly AutostartService _autostartService = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly PluginVersionDetector _versionDetector = new();
    private readonly PluginNameMatcher _nameMatcher = new();
    private readonly DataPortabilityService _dataPortability = new();
    private readonly NotificationService _notificationService = new(
        Path.Combine(AppContext.BaseDirectory, "a_clean_modern_app_icon_logo_design_on_a_dark_b.ico"));

    private LibraryData _library = new();

    public ObservableCollection<PluginDisplayViewModel> Plugins { get; } = new();

    public ICollectionView FavoritesView { get; }
    public ICollectionView InstrumentsView { get; }
    public ICollectionView EffectsView { get; }
    public ICollectionView UnclassifiedView { get; }

    [ObservableProperty]
    private bool _isFavoritesSectionExpanded = true;

    [ObservableProperty]
    private bool _isInstrumentsSectionExpanded = true;

    [ObservableProperty]
    private bool _isEffectsSectionExpanded = true;

    [ObservableProperty]
    private bool _isUnclassifiedSectionExpanded = true;

    [ObservableProperty]
    private int _favoritesCount;

    [ObservableProperty]
    private int _instrumentsCount;

    [ObservableProperty]
    private int _effectsCount;

    [ObservableProperty]
    private int _unclassifiedCount;

    [ObservableProperty]
    private bool _isSelectionMode;

    [ObservableProperty]
    private int _selectedCount;

    /// <summary>
    /// Library-wide totals of plugins actually installed on this machine. Deliberately
    /// independent of the search box and filter popup (unlike the per-tab counts, which
    /// reflect the filtered views) so the header always reports the real size of the library.
    /// </summary>
    [ObservableProperty]
    private int _installedTotalCount;

    [ObservableProperty]
    private int _installedInstrumentCount;

    [ObservableProperty]
    private int _installedEffectCount;

    [ObservableProperty]
    private ManagementMode _mode = ManagementMode.Legit;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    // Defaults to showing only what's on disk: uninstalled plugins are remembered but kept out
    // of the way, as are catalog entries that were never installed. Both remain reachable via
    // the "Not Installed" / "All" pills in the Filters popup.
    private InstalledFilterOption _installedFilter = InstalledFilterOption.InstalledOnly;

    [ObservableProperty]
    private FormatFilterOption _formatFilter = FormatFilterOption.All;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private Color _accentColor = (Color)ColorConverter.ConvertFromString("#FF8A5CF6");

    [ObservableProperty]
    private string _accentHexInput = "#FF8A5CF6";

    public IReadOnlyList<Color> AccentSwatches { get; } = new[]
    {
        "#FF8A5CF6", "#FF2DD4BF", "#FFF97316", "#FFEF4444",
        "#FF3B82F6", "#FFEC4899", "#FF22C55E", "#FFEAB308"
    }.Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToList();

    [ObservableProperty]
    private bool _autostartEnabled;

    /// <summary>Whether the launch-time online lookup for newer plugin versions runs.</summary>
    [ObservableProperty]
    private bool _checkForPluginUpdatesOnStartup = true;

    /// <summary>Whether the main window opens minimized instead of normal-sized.</summary>
    [ObservableProperty]
    private bool _startMinimized;

    /// <summary>Whether Windows notifications fire for new plugins, outdated versions, and completed scans/refreshes.</summary>
    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private DateTime? _lastUpdateCheck;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _latestReleaseUrl;

    [ObservableProperty]
    private string? _updateAssetDownloadUrl;

    [ObservableProperty]
    private string _updateButtonText = "A new update is available";

    [ObservableProperty]
    private bool _isInstallingUpdate;

    [ObservableProperty]
    private string? _refreshProgressText;

    /// <summary>Dismissible banner text summarizing the results of the startup version check.</summary>
    [ObservableProperty]
    private string? _startupUpdateSummaryText;

    [ObservableProperty]
    private LayoutMode _layoutMode = LayoutMode.Grid;

    public string CurrentVersion => UpdateChecker.CurrentVersion;

    public IEnumerable<string> CustomScanFolders => _library.CustomScanFolders;

    private bool _isInitializing;
    private bool _isSyncingHexInput;

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeManager.Apply(value ? AppTheme.Dark : AppTheme.Light);
        SaveThemeSettings();
    }

    [RelayCommand]
    private void SetLayoutMode(LayoutMode mode) => LayoutMode = mode;

    partial void OnLayoutModeChanged(LayoutMode value)
    {
        if (_isInitializing)
        {
            return;
        }

        _library.LayoutMode = value.ToString();
        _libraryStore.Save(_library);
    }

    partial void OnAccentColorChanged(Color value)
    {
        ThemeManager.ApplyAccent(value);
        SaveThemeSettings();

        _isSyncingHexInput = true;
        AccentHexInput = value.ToString();
        _isSyncingHexInput = false;
    }

    partial void OnAccentHexInputChanged(string value)
    {
        if (_isSyncingHexInput)
        {
            return;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                AccentColor = color;
            }
        }
        catch (FormatException)
        {
            // Ignore invalid hex input until the user finishes typing a valid value.
        }
    }

    [RelayCommand]
    private void SelectAccentColor(Color color) => AccentColor = color;

    partial void OnCheckForPluginUpdatesOnStartupChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _library.CheckForPluginUpdatesOnStartup = value;
        _libraryStore.Save(_library);
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _library.StartMinimized = value;
        _libraryStore.Save(_library);
    }

    partial void OnShowNotificationsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _library.ShowNotifications = value;
        _libraryStore.Save(_library);
    }

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _autostartService.SetEnabled(value);
        _library.AutostartEnabled = value;
        _libraryStore.Save(_library);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingForUpdates = true;
        UpdateStatusText = string.Empty;

        var result = await _updateChecker.CheckForUpdateAsync();

        LastUpdateCheck = DateTime.Now;
        _library.LastUpdateCheck = LastUpdateCheck;
        _libraryStore.Save(_library);

        UpdateStatusText = result switch
        {
            { Error: not null } => $"Update check failed: {result.Error}",
            { UpdateAvailable: true } => $"Update available: v{result.LatestVersion}",
            _ => "You're up to date."
        };

        IsUpdateAvailable = result.UpdateAvailable;
        LatestReleaseUrl = result.ReleaseUrl;
        UpdateAssetDownloadUrl = result.AssetDownloadUrl;
        UpdateButtonText = UpdateAssetDownloadUrl is not null ? "Update Now" : "A new update is available";

        if (result.UpdateAvailable)
        {
            var message = UpdateAssetDownloadUrl is not null
                ? $"A new version (v{result.LatestVersion}) is ready to install. VST Manager will close and the installer will open. Continue?"
                : $"A new version (v{result.LatestVersion}) is available. Open the release page?";

            var confirmResult = MessageBox.Show(message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (confirmResult == MessageBoxResult.Yes)
            {
                await InstallOrOpenRelease();
            }
        }

        IsCheckingForUpdates = false;
    }

    [RelayCommand]
    private async Task InstallOrOpenRelease()
    {
        if (UpdateAssetDownloadUrl is not null)
        {
            await InstallUpdateAsync(UpdateAssetDownloadUrl);
        }
        else if (LatestReleaseUrl is not null)
        {
            Process.Start(new ProcessStartInfo(LatestReleaseUrl) { UseShellExecute = true });
        }
    }

    private async Task InstallUpdateAsync(string downloadUrl)
    {
        IsInstallingUpdate = true;
        try
        {
            var destinationPath = Path.Combine(Path.GetTempPath(), UpdateChecker.InstallerAssetName);
            var success = await _updateChecker.DownloadInstallerAsync(downloadUrl, destinationPath);

            if (!success)
            {
                MessageBox.Show(
                    "Couldn't download the update. Try again later, or use the button again to open the release page instead.",
                    "Update Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(destinationPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    private void SaveThemeSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        _library.IsDarkTheme = IsDarkTheme;
        _library.AccentColor = AccentColor.ToString();
        _libraryStore.Save(_library);
    }

    public MainViewModel()
    {
        _scanner = new PluginScanner(_exclusionList);

        FavoritesView = new ListCollectionView(Plugins)
        {
            Filter = obj => MatchesFilters(obj) && obj is PluginDisplayViewModel { IsFavorite: true }
        };
        FavoritesView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.Name), ListSortDirection.Ascending));

        InstrumentsView = new ListCollectionView(Plugins)
        {
            Filter = obj => MatchesFilters(obj) && obj is PluginDisplayViewModel { Kind: PluginKind.Instrument }
        };
        InstrumentsView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.Name), ListSortDirection.Ascending));

        EffectsView = new ListCollectionView(Plugins)
        {
            Filter = obj => MatchesFilters(obj) && obj is PluginDisplayViewModel { Kind: PluginKind.Effect }
        };
        EffectsView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.Name), ListSortDirection.Ascending));

        UnclassifiedView = new ListCollectionView(Plugins)
        {
            Filter = obj => MatchesFilters(obj) && obj is PluginDisplayViewModel { Kind: PluginKind.Unclassified }
        };
        UnclassifiedView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.Name), ListSortDirection.Ascending));

        _isInitializing = true;
        var settings = _libraryStore.Load();
        _isDarkTheme = settings.IsDarkTheme;
        if (ColorConverter.ConvertFromString(settings.AccentColor) is Color accent)
        {
            _accentColor = accent;
            _accentHexInput = accent.ToString();
        }
        ThemeManager.Apply(_isDarkTheme ? AppTheme.Dark : AppTheme.Light);
        ThemeManager.ApplyAccent(_accentColor);

        _autostartEnabled = _autostartService.IsEnabled();
        _checkForPluginUpdatesOnStartup = settings.CheckForPluginUpdatesOnStartup;
        _startMinimized = settings.StartMinimized;
        _showNotifications = settings.ShowNotifications;
        _lastUpdateCheck = settings.LastUpdateCheck;
        _layoutMode = Enum.TryParse<LayoutMode>(settings.LayoutMode, out var layoutMode) ? layoutMode : LayoutMode.Grid;
        _isInitializing = false;

        _ = InitializeAsync();

        _ = CheckForUpdatesCommand.ExecuteAsync(null);
    }

    private async Task InitializeAsync()
    {
        await LoadAndScanAsync();

        // The catalog is also fetched from the GitHub repo so it can be updated
        // without shipping a new app release; rebuild the display when it changed.
        var catalogChanged = await _catalog.TryRefreshFromRemoteAsync();
        if (catalogChanged)
        {
            await LoadAndScanAsync();
        }

        // The scan(s) above already set IsNew on any plugin whose install path wasn't in the
        // library before this launch (LoadAndScanAsync, same signal that paints the "NEW"
        // badge) — capture that count before the KVR check below runs, since it doesn't
        // touch IsNew.
        var newlyFoundCount = Plugins.Count(p => p.IsInstalled && p.IsNew);

        // Check every installed plugin against KVR for a newer release, same as "Refresh All
        // Metadata" but without the catalog-rematch step or its confirm dialog — this only
        // needs to light up OUTDATED badges and report a summary, not touch identity/logo for
        // catalogued plugins. Runs after the UI is already populated so launch isn't blocked.
        await CheckForOutdatedPluginsOnStartupAsync(newlyFoundCount);
    }

    private async Task CheckForOutdatedPluginsOnStartupAsync(int newlyFoundCount)
    {
        // Only the online lookup is optional. Newly-found plugins and any already-known
        // outdated versions are local facts, so they're still summarised either way.
        if (CheckForPluginUpdatesOnStartup)
        {
            await EnrichAllFromWebAsync();
        }

        var outdated = Plugins.Where(p => p.IsInstalled && p.IsOutdated).ToList();

        var newPart = newlyFoundCount switch
        {
            0 => null,
            1 => "1 new plugin was found",
            _ => $"{newlyFoundCount} new plugins were found"
        };

        var outdatedPart = outdated.Count switch
        {
            0 => null,
            1 => $"{outdated[0].Name} has a newer version available",
            _ => $"{outdated.Count} plugins have newer versions available"
        };

        StartupUpdateSummaryText = (newPart, outdatedPart) switch
        {
            (null, null) => null,
            (not null, null) => $"{newPart}.",
            (null, not null) => $"{outdatedPart}.",
            _ => $"{newPart}, and {outdatedPart}."
        };

        if (ShowNotifications && StartupUpdateSummaryText is not null)
        {
            _notificationService.Show("VST Manager", StartupUpdateSummaryText);
        }
    }

    [RelayCommand]
    private void DismissStartupUpdateSummary() => StartupUpdateSummaryText = null;

    partial void OnModeChanged(ManagementMode value) => RefreshViews();
    partial void OnSearchTextChanged(string value) => RefreshViews();
    partial void OnInstalledFilterChanged(InstalledFilterOption value) => RefreshViews();
    partial void OnFormatFilterChanged(FormatFilterOption value) => RefreshViews();
    partial void OnShowHiddenChanged(bool value) => RefreshViews();

    [RelayCommand]
    private void ResetFilters()
    {
        InstalledFilter = InstalledFilterOption.InstalledOnly;
        FormatFilter = FormatFilterOption.All;
        ShowHidden = false;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void RefreshViews()
    {
        FavoritesView.Refresh();
        InstrumentsView.Refresh();
        EffectsView.Refresh();
        UnclassifiedView.Refresh();
        FavoritesCount = FavoritesView.Cast<object>().Count();
        InstrumentsCount = InstrumentsView.Cast<object>().Count();
        EffectsCount = EffectsView.Cast<object>().Count();
        UnclassifiedCount = UnclassifiedView.Cast<object>().Count();

        RefreshInstalledTotals();
    }

    /// <summary>
    /// Recomputes the header's library totals from the full plugin list, bypassing the
    /// filtered views so search terms and filters never change the reported numbers.
    /// </summary>
    private void RefreshInstalledTotals()
    {
        var installed = Plugins.Where(p => p.IsInstalled).ToList();

        InstalledTotalCount = installed.Count;
        InstalledInstrumentCount = installed.Count(p => p.Kind == PluginKind.Instrument);
        InstalledEffectCount = installed.Count(p => p.Kind == PluginKind.Effect);
    }

    private bool MatchesFilters(object obj)
    {
        if (obj is not PluginDisplayViewModel vm)
        {
            return false;
        }

        // A plugin with both a Legit and a Cracked copy belongs to both modes.
        var matchesMode = Mode switch
        {
            ManagementMode.Legit => vm.Tag is PluginTagSummary.Legit or PluginTagSummary.Unclassified or PluginTagSummary.Both,
            ManagementMode.Cracked => vm.Tag is PluginTagSummary.Cracked or PluginTagSummary.Unclassified or PluginTagSummary.Both,
            _ => true
        };

        if (!matchesMode)
        {
            return false;
        }

        if (!ShowHidden && vm.IsHidden)
        {
            return false;
        }

        var matchesStatus = InstalledFilter switch
        {
            InstalledFilterOption.InstalledOnly => vm.IsInstalled,
            InstalledFilterOption.NotInstalledOnly => !vm.IsInstalled,
            _ => true
        };

        if (!matchesStatus)
        {
            return false;
        }

        var matchesFormat = FormatFilter switch
        {
            FormatFilterOption.Vst2 => !vm.IsInstalled || vm.HasFormat(PluginFormat.Vst2),
            FormatFilterOption.Vst3 => !vm.IsInstalled || vm.HasFormat(PluginFormat.Vst3),
            _ => true
        };

        if (!matchesFormat)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || (vm.Vendor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
               || (vm.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand]
    private async Task Rescan()
    {
        await LoadAndScanAsync();

        if (ShowNotifications)
        {
            _notificationService.Show("VST Manager", "Plugin scan complete.");
        }
    }

    public event EventHandler<IReadOnlyList<PluginDisplayViewModel>>? NewMultiCopyPluginsFound;

    private async Task LoadAndScanAsync()
    {
        IsScanning = true;
        try
        {
            var (displayItems, newPaths, badgePaths) = await Task.Run(() =>
            {
                _library = _libraryStore.Load();
                var previousPaths = new HashSet<string>(_library.Plugins.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

                var vst3Paths = _scanPathProvider.GetVst3Paths(_library.CustomScanFolders);
                var vst2Paths = _scanPathProvider.GetVst2Paths(_library.CustomScanFolders);
                var scanned = _scanner.Scan(vst3Paths, vst2Paths);

                var merged = _libraryStore.MergeOnRescan(_library.Plugins, scanned);
                _library.Plugins = merged;
                _libraryStore.Save(_library);

                var newPaths = new HashSet<string>(
                    merged.Select(p => p.Path).Where(p => !previousPaths.Contains(p)),
                    StringComparer.OrdinalIgnoreCase);

                // Separate from newPaths above: the "NEW" badge should never light up the
                // entire library on the very first-ever scan, when there's nothing yet to
                // compare against. The multi-copy classification prompt below intentionally
                // keeps using newPaths as-is, since prompting for every multi-copy plugin on
                // first launch is the existing, desired behavior.
                var badgePaths = previousPaths.Count == 0
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : newPaths;

                var displayItems = _displayBuilder.Build(_catalog.Entries, merged);
                _displayBuilder.ApplyManualOverrides(displayItems, _manualMetadataOverrides);
                return (displayItems, newPaths, badgePaths);
            });

            Plugins.Clear();
            foreach (var item in displayItems)
            {
                var vm = new PluginDisplayViewModel(item);
                vm.PropertyChanged += OnPluginPropertyChanged;
                vm.IsNew = item.Installs.Any(i => badgePaths.Contains(i.Path));
                Plugins.Add(vm);
                _ = LoadLogoAsync(vm, item);
            }

            RefreshViews();

            // Newly discovered plugins with more than one installed copy: ask the
            // user which copy is the legit one and which the cracked one.
            var newMultiCopy = Plugins
                .Where(vm => vm.Installs.Count > 1
                             && vm.Tag == PluginTagSummary.Unclassified
                             && vm.Installs.Any(i => newPaths.Contains(i.Path)))
                .ToList();

            if (newMultiCopy.Count > 0)
            {
                NewMultiCopyPluginsFound?.Invoke(this, newMultiCopy);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void OnPluginPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginDisplayViewModel.IsSelected))
        {
            SelectedCount = Plugins.Count(p => p.IsSelected);
        }
    }

    public void SetCopyTag(PluginDisplayViewModel vm, PluginInfo copy, PluginTag tag)
    {
        copy.Tag = tag;

        var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
        if (stored is not null)
        {
            stored.Tag = tag;
        }

        _libraryStore.Save(_library);
        vm.RefreshInstallInfo();
        RefreshViews();
    }

    private async Task LoadLogoAsync(PluginDisplayViewModel vm, PluginDisplayItem item)
    {
        if (_manualLogoOverrides.IsLocalFileOverride(item.BaseName))
        {
            var cachedLocalPath = _logoCache.FindManualCachedFile(item.BaseName);
            if (cachedLocalPath is not null)
            {
                vm.LogoPath = cachedLocalPath;
                return;
            }
        }

        var overrideUrl = _manualLogoOverrides.GetOverrideUrl(item.BaseName);
        if (overrideUrl is not null)
        {
            var manualPath = await _logoCache.GetManualLogoPathAsync(item.BaseName, overrideUrl);
            if (manualPath is not null)
            {
                vm.LogoPath = manualPath;
                return;
            }
        }

        if (item.Catalog is null)
        {
            return;
        }

        var path = await _logoCache.GetLogoPathAsync(item.Catalog);
        vm.LogoPath = path;
    }

    public async Task<string?> PreviewLogoAsync(string url) => await _logoCache.DownloadPreviewAsync(url);

    public async Task<bool> FixLogoAsync(PluginDisplayViewModel vm, string url)
    {
        var path = await _logoCache.GetManualLogoPathAsync(vm.BaseName, url, forceRefresh: true);
        if (path is null)
        {
            return false;
        }

        _manualLogoOverrides.SetOverride(vm.BaseName, url);

        foreach (var plugin in Plugins.Where(p => string.Equals(p.BaseName, vm.BaseName, StringComparison.OrdinalIgnoreCase)))
        {
            plugin.LogoPath = path;
        }

        return true;
    }

    /// <summary>
    /// Sets a plugin's artwork from a local image file — used by the "Browse..." picker when the
    /// plugin's normal (URL-based) artwork fails to display. Unlike <see cref="FixLogoAsync"/>
    /// this never hits the network; the chosen file is copied straight into the logo cache.
    /// </summary>
    public async Task<bool> SetLogoFromLocalFileAsync(PluginDisplayViewModel vm, string filePath)
    {
        var path = await _logoCache.SaveLocalLogoAsync(vm.BaseName, filePath);
        if (path is null)
        {
            return false;
        }

        _manualLogoOverrides.SetLocalFileOverride(vm.BaseName);

        foreach (var plugin in Plugins.Where(p => string.Equals(p.BaseName, vm.BaseName, StringComparison.OrdinalIgnoreCase)))
        {
            plugin.LogoPath = path;
        }

        return true;
    }

    public void ApplyMetadataOverride(PluginDisplayViewModel vm, string? name, string? vendor)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var normalizedVendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim();

        _manualMetadataOverrides.SetOverride(vm.BaseName, normalizedName, normalizedVendor);

        foreach (var plugin in Plugins.Where(p => string.Equals(p.BaseName, vm.BaseName, StringComparison.OrdinalIgnoreCase)))
        {
            plugin.ApplyMetadataOverride(normalizedName, normalizedVendor);
        }

        RefreshViews();
    }

    public sealed record AutoDetectResult(
        string? DetectedCurrentVersion,
        bool VersionAlreadySet,
        CatalogEntry? MatchedCatalogEntry,
        bool CatalogMatchIsNew,
        KvrLookupResult? WebLookupResult,
        IReadOnlyList<PluginInfoCandidate> WebCandidates,
        bool NeedsUserChoice);

    /// <summary>Fetches plugin details from a product page URL the user supplied (KVR or any plugin database).</summary>
    public async Task<KvrLookupResult?> FetchInfoFromUrlAsync(string url) => await _kvrLookup.FetchFromUrlAsync(url);

    /// <summary>What turned up the installed version, so the UI can say where the number came from.</summary>
    public sealed record VersionDetectionResult(string? Version, string SourceDescription);

    /// <summary>
    /// Re-detects the installed version for one plugin, walking the whole fallback chain until
    /// something answers: each copy's own file metadata (Windows version resource → VST3
    /// moduleinfo.json → vendor bundle manifest, all inside DetectFromFile), then the Windows
    /// uninstall registry. Unlike the bulk refresh this tries *every* installed copy rather
    /// than only the first, since a plugin can ship a VST2 DLL with no version resource
    /// alongside a VST3 bundle that has one.
    /// </summary>
    public async Task<VersionDetectionResult> DetectCurrentVersionAsync(PluginDisplayViewModel vm)
    {
        var copies = vm.ActiveInstalls.ToList();
        if (copies.Count == 0)
        {
            return new VersionDetectionResult(null, "this plugin isn't installed");
        }

        return await Task.Run(() =>
        {
            foreach (var copy in copies)
            {
                var fromFile = _versionDetector.DetectFromFile(copy.Path);
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    return new VersionDetectionResult(fromFile, $"the {FormatLabel(copy.Format)} file");
                }
            }

            // Last resort: the vendor's installer entry in the Windows uninstall registry.
            var installedPrograms = _uninstallerLookup.EnumerateInstalledPrograms().ToList();
            var fromRegistry = UninstallerLookup.FindUninstaller(installedPrograms, vm.Name, vm.Vendor)?.DisplayVersion;

            return string.IsNullOrWhiteSpace(fromRegistry)
                ? new VersionDetectionResult(null, "the plugin files or the Windows registry")
                : new VersionDetectionResult(fromRegistry, "the Windows uninstall registry");
        });
    }

    private static string FormatLabel(PluginFormat format) => format == PluginFormat.Vst3 ? "VST3" : "VST2";

    /// <summary>
    /// Looks up candidates for a name/vendor the user controls, for the "Search Manually"
    /// button — unlike PreviewAutoDetectAsync this never auto-applies a "confident" result,
    /// since the whole point is letting the user correct a match Auto-Detect got wrong (e.g.
    /// a name-similarity false-positive matching "Omnisphere" to "Omnisphere" v1 when v3 is
    /// actually installed).
    /// </summary>
    public async Task<IReadOnlyList<PluginInfoCandidate>> SearchCandidatesAsync(string name, string? vendor) =>
        await _kvrLookup.SearchCandidatesAsync(name, vendor);

    /// <summary>
    /// Non-mutating preview of what an automated refresh would find for one plugin — used by
    /// the Fix Metadata window's "Auto-Detect" button so the user can review before committing
    /// via Save, instead of RefreshMetadataCoreAsync's silent-write-then-confirm flow. Always
    /// runs both checks and reports what it found (or didn't), rather than only reporting when
    /// something changed — so the window can explain *why* nothing changed instead of just
    /// saying "no changes found". Falls back to a live KVR Audio lookup only when the plugin
    /// isn't in the local catalog at all — the local catalog is always tried first since it's
    /// fast and reliable, while the web lookup is a best-effort fallback that can fail quietly.
    /// </summary>
    public async Task<AutoDetectResult> PreviewAutoDetectAsync(PluginDisplayViewModel vm)
    {
        string? detectedVersion = null;
        if (vm.Installed is not null)
        {
            detectedVersion = await Task.Run(() =>
            {
                var installedPrograms = _uninstallerLookup.EnumerateInstalledPrograms().ToList();
                return _versionDetector.DetectFromFile(vm.Installed.Path)
                    ?? UninstallerLookup.FindUninstaller(installedPrograms, vm.Name, vm.Vendor)?.DisplayVersion;
            });
        }

        CatalogEntry? matchedEntry = null;
        var catalogMatchIsNew = false;
        KvrLookupResult? webResult = null;
        IReadOnlyList<PluginInfoCandidate> candidates = Array.Empty<PluginInfoCandidate>();
        var needsUserChoice = false;

        if (vm.Installed is not null)
        {
            matchedEntry = _nameMatcher.FindMatch(vm.Installed.Name, _catalog.Entries);
            catalogMatchIsNew = matchedEntry is not null && !string.Equals(matchedEntry.Name, vm.Catalog?.Name, StringComparison.Ordinal);

            // Always look online, even for catalogued plugins — the web result carries the
            // latest released version, which the local catalog doesn't know about. (For
            // catalogued plugins only the version is applied; identity stays curated.)
            // Unlike the bulk refresh, this interactive path gathers several scored candidates
            // so an unclear result can be put to the user instead of guessed at.
            var query = matchedEntry?.Name ?? vm.Catalog?.Name ?? vm.Installed.Name;
            var queryVendor = matchedEntry?.Vendor ?? vm.Catalog?.Vendor ?? vm.Vendor;
            candidates = await _kvrLookup.SearchCandidatesAsync(query, queryVendor);

            var best = candidates.FirstOrDefault();
            if (best is not null)
            {
                // Ask the user when the top hit isn't clearly right, or when a runner-up is
                // close enough behind it that picking automatically would be a coin flip.
                var runnerUp = candidates.Skip(1).FirstOrDefault();
                var isAmbiguous = runnerUp is not null && best.Confidence - runnerUp.Confidence < 0.15;

                needsUserChoice = best.Confidence < NameSimilarity.ConfidentThreshold || isAmbiguous;
                webResult = needsUserChoice ? null : best.Info;
            }
        }

        return new AutoDetectResult(
            DetectedCurrentVersion: detectedVersion,
            VersionAlreadySet: !string.IsNullOrWhiteSpace(vm.CurrentVersion),
            MatchedCatalogEntry: matchedEntry,
            CatalogMatchIsNew: catalogMatchIsNew,
            WebLookupResult: webResult,
            WebCandidates: candidates,
            NeedsUserChoice: needsUserChoice);
    }

    public event EventHandler<PluginDisplayViewModel>? FixMetadataRequested;

    [RelayCommand]
    private void OpenFixMetadata(PluginDisplayViewModel? vm)
    {
        if (vm is not null)
        {
            FixMetadataRequested?.Invoke(this, vm);
        }
    }

    [RelayCommand]
    private void MarkLegit(PluginDisplayViewModel? vm) => SetTag(vm, PluginTag.Legit);

    [RelayCommand]
    private void MarkCracked(PluginDisplayViewModel? vm) => SetTag(vm, PluginTag.Cracked);

    [RelayCommand]
    private void MarkAsInstrument(PluginDisplayViewModel? vm) => SetKind(vm, PluginKind.Instrument);

    [RelayCommand]
    private void MarkAsEffect(PluginDisplayViewModel? vm) => SetKind(vm, PluginKind.Effect);

    [RelayCommand]
    private void MarkAllLegit() => SetTagForVisibleInstalled(PluginTag.Legit);

    [RelayCommand]
    private void MarkAllCracked() => SetTagForVisibleInstalled(PluginTag.Cracked);

    private void SetTagForVisibleInstalled(PluginTag tag)
    {
        var targets = Plugins.Where(p => p.IsInstalled && MatchesFilters(p)).ToList();
        foreach (var vm in targets)
        {
            ApplyTagToAllCopies(vm, tag);
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        if (!value)
        {
            ClearSelection();
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var vm in Plugins)
        {
            vm.IsSelected = false;
        }
    }

    public void SetSelected(PluginDisplayViewModel vm, bool selected)
    {
        vm.IsSelected = selected;
        if (selected)
        {
            IsSelectionMode = true;
        }
    }

    [RelayCommand]
    private void BatchMarkLegit() => ApplyTagToTargets(Plugins.Where(p => p.IsSelected).ToList(), PluginTag.Legit);

    [RelayCommand]
    private void BatchMarkCracked() => ApplyTagToTargets(Plugins.Where(p => p.IsSelected).ToList(), PluginTag.Cracked);

    [RelayCommand]
    private void BatchMarkInstrument() => ApplyKindToTargets(Plugins.Where(p => p.IsSelected).ToList(), PluginKind.Instrument);

    [RelayCommand]
    private void BatchMarkEffect() => ApplyKindToTargets(Plugins.Where(p => p.IsSelected).ToList(), PluginKind.Effect);

    /// <summary>
    /// Decides what a context-menu command should act on: the right-clicked plugin alone, or
    /// the whole current selection. The latter only applies when the right-clicked item is
    /// itself part of an active multi-selection — set up by
    /// MainWindow's right-click handler, which collapses the selection to a single item
    /// whenever you right-click something outside the current selection, so a stray right-click
    /// can never silently apply a batch action to plugins the user isn't looking at.
    /// </summary>
    private List<PluginDisplayViewModel> ResolveTargets(PluginDisplayViewModel? clicked)
    {
        if (clicked is not null && IsSelectionMode && clicked.IsSelected && SelectedCount > 1)
        {
            return Plugins.Where(p => p.IsSelected).ToList();
        }

        return clicked is null ? new List<PluginDisplayViewModel>() : new List<PluginDisplayViewModel> { clicked };
    }

    private void ApplyTagToTargets(List<PluginDisplayViewModel> targets, PluginTag tag)
    {
        foreach (var vm in targets.Where(p => p.IsInstalled))
        {
            ApplyTagToAllCopies(vm, tag);
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void ApplyKindToTargets(List<PluginDisplayViewModel> targets, PluginKind kind)
    {
        foreach (var vm in targets.Where(p => p.IsInstalled))
        {
            ApplyKindToAllCopies(vm, kind);
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void ApplyFavoriteToTargets(List<PluginDisplayViewModel> targets, bool isFavorite)
    {
        foreach (var vm in targets.Where(p => p.Installs.Count > 0))
        {
            foreach (var copy in vm.Installs)
            {
                copy.IsFavorite = isFavorite;

                var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
                if (stored is not null)
                {
                    stored.IsFavorite = isFavorite;
                }
            }

            vm.RefreshInstallInfo();
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void ApplyHiddenToTargets(List<PluginDisplayViewModel> targets, bool isHidden)
    {
        foreach (var vm in targets.Where(p => p.Installs.Count > 0))
        {
            foreach (var copy in vm.Installs)
            {
                copy.IsHidden = isHidden;

                var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
                if (stored is not null)
                {
                    stored.IsHidden = isHidden;
                }
            }

            vm.RefreshInstallInfo();
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void ApplyTagToAllCopies(PluginDisplayViewModel vm, PluginTag tag)
    {
        foreach (var copy in vm.Installs)
        {
            copy.Tag = tag;

            var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.Tag = tag;
            }
        }

        vm.RefreshInstallInfo();
    }

    private void ApplyKindToAllCopies(PluginDisplayViewModel vm, PluginKind kind)
    {
        foreach (var copy in vm.Installs)
        {
            copy.Kind = kind;

            var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.Kind = kind;
            }
        }

        vm.RefreshInstallInfo();
    }

    [RelayCommand]
    private void ToggleFavorite(PluginDisplayViewModel? vm)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        // The right-clicked plugin's current state decides the outcome for the whole batch —
        // a mixed selection ends up uniformly favorite/not, rather than each item flipping its
        // own state independently and looking inconsistent afterward.
        ApplyFavoriteToTargets(ResolveTargets(vm), !vm.IsFavorite);
    }

    [RelayCommand]
    private void ToggleHide(PluginDisplayViewModel? vm)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        ApplyHiddenToTargets(ResolveTargets(vm), !vm.IsHidden);
    }

    [RelayCommand]
    private async Task RefreshAllMetadata()
    {
        var result = MessageBox.Show(
            "This action will take some time. Continue?",
            "Refresh All Metadata",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var targets = Plugins.Where(p => p.IsInstalled).ToList();

        IsCheckingForUpdates = true;
        try
        {
            await RefreshMetadataCoreAsync(targets);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }

        if (ShowNotifications)
        {
            _notificationService.Show("VST Manager", "Metadata refresh complete.");
        }
    }

    private async Task RefreshMetadataCoreAsync(IReadOnlyList<PluginDisplayViewModel> targets)
    {
        // 1. Re-detect Current Version from the file's embedded version, falling back to the
        // Windows Uninstall registry's DisplayVersion (enumerated once per batch, not once
        // per plugin). Freshly detected values replace whatever was stored (a full refresh
        // means updated info wins); existing values are kept only when detection finds nothing.
        var anyVersionChanged = await Task.Run(() =>
        {
            var installedPrograms = _uninstallerLookup.EnumerateInstalledPrograms().ToList();
            var changed = false;

            foreach (var vm in targets)
            {
                // Active copies only: reading a remembered copy's path would just be failed
                // disk I/O for a file that's been uninstalled.
                foreach (var copy in vm.ActiveInstalls)
                {
                    var detected = _versionDetector.DetectFromFile(copy.Path)
                        ?? UninstallerLookup.FindUninstaller(installedPrograms, vm.Name, vm.Vendor)?.DisplayVersion;

                    if (string.IsNullOrWhiteSpace(detected)
                        || string.Equals(detected, copy.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    copy.CurrentVersion = detected;
                    var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
                    if (stored is not null)
                    {
                        stored.CurrentVersion = detected;
                    }

                    changed = true;
                }
            }

            return changed;
        });

        foreach (var vm in targets)
        {
            vm.RefreshInstallInfo();
        }

        if (anyVersionChanged)
        {
            // Persist before any rebuild below, since LoadAndScanAsync reloads _library
            // from disk and would otherwise discard these in-memory version writes.
            _libraryStore.Save(_library);
        }

        // 2. Pick up catalog updates published since the app started.
        await _catalog.TryRefreshFromRemoteAsync();

        // 3. Re-run name matching against the (possibly refreshed) catalog.
        var changedMatches = new List<(PluginDisplayViewModel Vm, CatalogEntry NewEntry)>();
        var newMatches = new List<(PluginDisplayViewModel Vm, CatalogEntry NewEntry)>();

        foreach (var vm in targets)
        {
            if (vm.Installed is null)
            {
                continue;
            }

            // Compare by Name, not reference: a successful remote catalog refetch replaces
            // every CatalogEntry instance even when its content is unchanged.
            var match = _nameMatcher.FindMatch(vm.Installed.Name, _catalog.Entries);
            if (match is null || string.Equals(match.Name, vm.Catalog?.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (vm.Catalog is null)
            {
                newMatches.Add((vm, match));
            }
            else
            {
                changedMatches.Add((vm, match));
            }
        }

        var applyChangedMatches = true;
        if (changedMatches.Count > 0)
        {
            var summary = string.Join("\n", changedMatches.Select(m => $"{m.Vm.Name} → {m.NewEntry.Name}"));
            var confirmResult = MessageBox.Show(
                $"Refreshing found a different catalog match for {changedMatches.Count} plugin(s):\n\n{summary}\n\nApply these changes?",
                "Catalog Match Changed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            applyChangedMatches = confirmResult == MessageBoxResult.Yes;
        }

        // Matching is recomputed fresh from the catalog on every rebuild rather than
        // persisted, so a rebuild is all-or-nothing: if the user declined the changed
        // matches, skip the rebuild entirely rather than applying it anyway just because
        // there were also new matches to pick up (those will apply on the next refresh).
        var shouldRebuild = (changedMatches.Count == 0 || applyChangedMatches)
                             && (newMatches.Count > 0 || changedMatches.Count > 0);

        // Multiple plugins re-matching at once can newly collapse two previously
        // separate uncatalogued items onto the same catalog entry, which needs proper
        // regrouping (merging their Installs) rather than in-place mutation.
        if (shouldRebuild)
        {
            await LoadAndScanAsync();
        }

        var refreshedTargets = Plugins.Where(p => p.IsInstalled && p.Catalog is not null).ToList();
        foreach (var vm in refreshedTargets)
        {
            var path = await _logoCache.RefreshLogoAsync(vm.Catalog!);
            vm.LogoPath = path;
        }

        // 4. Web enrichment: look up every installed plugin on KVR to fetch its latest
        // released version (and, for uncatalogued plugins, name/vendor/logo too). Iterates
        // the fresh Plugins list since the rebuild above may have replaced every vm.
        await EnrichAllFromWebAsync();
    }

    /// <summary>
    /// Fetches KVR info for every installed plugin: LatestVersion for all, plus Name/Vendor/
    /// logo for uncatalogued ones (catalogued plugins keep their curated identity — the web
    /// only supplies the version for them). Sequential with a small delay per lookup to stay
    /// polite to the search endpoint; failures skip quietly per-plugin.
    /// </summary>
    private async Task EnrichAllFromWebAsync()
    {
        var webTargets = Plugins.Where(p => p.IsInstalled).ToList();
        var resultsByBaseName = new Dictionary<string, KvrLookupResult?>(StringComparer.OrdinalIgnoreCase);
        var anyChanged = false;

        try
        {
            for (var i = 0; i < webTargets.Count; i++)
            {
                var vm = webTargets[i];
                RefreshProgressText = $"Checking online {i + 1}/{webTargets.Count}: {vm.Name}";

                if (!resultsByBaseName.TryGetValue(vm.BaseName, out var result))
                {
                    var query = vm.Catalog?.Name ?? vm.Installed!.Name;
                    result = await _kvrLookup.SearchAsync(query, vm.Catalog?.Vendor ?? vm.Vendor);
                    resultsByBaseName[vm.BaseName] = result;

                    await Task.Delay(400);
                }

                if (result is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(result.LatestVersion))
                {
                    foreach (var copy in vm.Installs)
                    {
                        copy.LatestVersion = result.LatestVersion;

                        var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
                        if (stored is not null)
                        {
                            stored.LatestVersion = result.LatestVersion;
                        }
                    }

                    anyChanged = true;
                }

                if (vm.Catalog is null)
                {
                    ApplyMetadataOverride(vm, result.ProductName, result.Vendor);

                    if (result.LogoUrl is not null && vm.LogoPath is null)
                    {
                        await FixLogoAsync(vm, result.LogoUrl);
                    }
                }

                vm.RefreshInstallInfo();
            }

            if (anyChanged)
            {
                _libraryStore.Save(_library);
            }
        }
        finally
        {
            RefreshProgressText = null;
        }
    }

    public void SetVersions(PluginDisplayViewModel? vm, string? currentVersion, string? latestVersion)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        var normalizedCurrent = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion.Trim();
        var normalizedLatest = string.IsNullOrWhiteSpace(latestVersion) ? null : latestVersion.Trim();

        foreach (var copy in vm.Installs)
        {
            copy.CurrentVersion = normalizedCurrent;
            copy.LatestVersion = normalizedLatest;

            var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.CurrentVersion = normalizedCurrent;
                stored.LatestVersion = normalizedLatest;
            }
        }

        vm.RefreshInstallInfo();
        _libraryStore.Save(_library);
    }

    private void SetTag(PluginDisplayViewModel? vm, PluginTag tag)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        ApplyTagToTargets(ResolveTargets(vm), tag);
    }

    private void SetKind(PluginDisplayViewModel? vm, PluginKind kind)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        ApplyKindToTargets(ResolveTargets(vm), kind);
    }

    [RelayCommand]
    private void ShowInFolder(PluginDisplayViewModel? vm) => ShowPathInFolder(vm?.Installed?.Path);

    /// <summary>
    /// Used by the detail window's per-copy folder icon, since a plugin can have several
    /// install copies (VST2 + VST3, or multiple DAW-specific folders) and the context-menu
    /// command above only ever targets the first one.
    /// </summary>
    [RelayCommand]
    private void ShowPathInFolder(string? path)
    {
        if (path is null || !File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Permanently discards the library's memory of an uninstalled plugin, so the remembered
    /// list can't grow without bound. Only valid for remembered plugins: an installed one
    /// would simply be rediscovered by the next scan.
    /// </summary>
    [RelayCommand]
    private void ForgetPlugin(PluginDisplayViewModel? vm)
    {
        var targets = ResolveTargets(vm).Where(v => v.IsRemembered).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var message = targets.Count == 1
            ? $"Forget \"{targets[0].Name}\"?\n\nVST Manager will stop remembering that this plugin was ever installed, "
              + "along with its tag, type, versions and favourite status. If you install it again later it "
              + "will be treated as a brand-new plugin."
            : $"Forget these {targets.Count} plugins?\n\n{string.Join("\n", targets.Select(v => v.Name))}\n\n"
              + "VST Manager will stop remembering that they were ever installed, along with their tags, "
              + "types, versions and favourite status. If any are installed again later they'll be treated "
              + "as brand-new plugins.";

        var result = MessageBox.Show(
            message,
            "Forget Plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        // Two passes deliberately: remove everything from the persisted library first (this
        // loop only reads `targets`, a snapshot, never `Plugins`), then remove from the live
        // `Plugins` collection afterward. Doing the Plugins.Remove calls inside the same loop
        // that produced `targets` would mutate the collection while it's still being enumerated.
        foreach (var v in targets)
        {
            foreach (var copy in v.Installs)
            {
                _library.Plugins.RemoveAll(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            }
        }

        _libraryStore.Save(_library);

        // Removing items directly rather than rescanning: a full rescan would cost a disk
        // sweep the user didn't ask for, and for a catalogued plugin would re-add it anyway as
        // a never-installed catalog entry.
        foreach (var v in targets)
        {
            v.PropertyChanged -= OnPluginPropertyChanged;
            Plugins.Remove(v);
        }

        RefreshViews();

        // Manual name/logo overrides are deliberately left alone: they're keyed by BaseName,
        // which several display items can share, so clearing here could strip a rename or logo
        // from an unrelated plugin that is still installed.
    }

    [RelayCommand]
    private async Task MarkAsNotAPlugin(PluginDisplayViewModel? vm)
    {
        var targets = ResolveTargets(vm);
        if (targets.Count <= 1)
        {
            await MarkAsNotAPluginAsync(vm);
            return;
        }

        await MarkMultipleAsNotAPluginAsync(targets);
    }

    /// <summary>
    /// Batch form of MarkAsNotAPluginAsync, for the context menu when multiple plugins are
    /// selected. Kept separate from the single-plugin method (rather than routing single calls
    /// through this one) since PluginDetailWindow depends on MarkAsNotAPluginAsync's exact
    /// signature and single-item confirmation wording.
    /// </summary>
    private async Task MarkMultipleAsNotAPluginAsync(List<PluginDisplayViewModel> targets)
    {
        var installed = targets.Where(v => v.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            return;
        }

        var allFiles = installed.SelectMany(v => v.ActiveInstalls.Select(i => Path.GetFileName(i.Path))).ToList();
        const int maxShown = 10;
        var summary = string.Join("\n", allFiles.Take(maxShown));
        if (allFiles.Count > maxShown)
        {
            summary += $"\n...and {allFiles.Count - maxShown} more";
        }

        var result = MessageBox.Show(
            $"Mark the following {installed.Count} plugin(s) as not a plugin?\n\n{summary}\n\n"
            + "They will be excluded from all future scans on this and any other machine running this app.",
            "Mark as Not a Plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var vm in installed)
        {
            foreach (var copy in vm.Installs)
            {
                _exclusionList.Exclude(copy.Path);
                _library.Plugins.RemoveAll(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            }
        }

        _libraryStore.Save(_library);
        await LoadAndScanAsync();
    }

    public async Task<bool> MarkAsNotAPluginAsync(PluginDisplayViewModel? vm)
    {
        // Requires a real file: excluding a remembered plugin would do nothing useful, since
        // there is nothing on disk for future scans to skip.
        if (vm is null || !vm.IsInstalled)
        {
            return false;
        }

        var fileNames = string.Join("\n", vm.ActiveInstalls.Select(i => Path.GetFileName(i.Path)));
        var result = MessageBox.Show(
            $"Mark the following as not a plugin?\n\n{fileNames}\n\nThey will be excluded from all future scans on this and any other machine running this app.",
            "Mark as Not a Plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var copy in vm.Installs)
        {
            _exclusionList.Exclude(copy.Path);
            _library.Plugins.RemoveAll(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
        }

        _libraryStore.Save(_library);

        await LoadAndScanAsync();
        return true;
    }

    [RelayCommand]
    private async Task Uninstall(PluginDisplayViewModel? vm)
    {
        var targets = ResolveTargets(vm).Where(v => v.IsInstalled).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == 1)
        {
            await UninstallSingleAsync(targets[0]);
            return;
        }

        await UninstallBatchAsync(targets);
    }

    private async Task UninstallSingleAsync(PluginDisplayViewModel vm)
    {
        var uninstaller = _uninstallerLookup.FindUninstaller(vm.Name, vm.Vendor);
        if (uninstaller is not null)
        {
            var result = MessageBox.Show(
                $"Run the uninstaller for \"{uninstaller.DisplayName}\"?",
                "Uninstall Plugin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes || !ConfirmFinalUninstall(vm.Name))
            {
                return;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{uninstaller.UninstallCommand}\"") { UseShellExecute = true });

                // Wait for the vendor uninstaller to finish, then rescan so the removed
                // plugin disappears from the list without a manual Rescan. Best-effort: if
                // the launched process hands off to another and exits early, the rescan
                // simply finds nothing changed and the user can rescan again later.
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }

                await LoadAndScanAsync();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                MessageBox.Show(
                    $"Couldn't launch the uninstaller for \"{uninstaller.DisplayName}\".\n\n{ex.Message}",
                    "Uninstall Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        var deleteResult = MessageBox.Show(
            $"No registered uninstaller was found for \"{vm.Name}\".\n\nDelete the plugin file(s) directly instead?\n\n{vm.Path}",
            "No Uninstaller Found",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (deleteResult != MessageBoxResult.Yes || !ConfirmFinalUninstall(vm.Name))
        {
            return;
        }

        var failures = new List<string>();
        var anyDeleted = false;

        // Active copies only — remembered ones have no file left to delete.
        foreach (var copy in vm.ActiveInstalls)
        {
            if (DeletePluginFile(copy.Path))
            {
                anyDeleted = true;
            }
            else
            {
                failures.Add(Path.GetFileName(copy.Path));
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(
                $"Couldn't delete the following file(s) — they may be in use by another program (close any DAW using this plugin and try again) or require administrator rights:\n\n{string.Join("\n", failures)}",
                "Uninstall Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        if (anyDeleted)
        {
            _ = LoadAndScanAsync();
        }
    }

    /// <summary>
    /// Uninstalls several plugins from one context-menu action. Unlike the single-plugin path,
    /// this asks for confirmation exactly once upfront (naming every plugin, and warning that
    /// installers run one after another) rather than per plugin — chosen deliberately so a
    /// large selection doesn't mean clicking through up to two dialogs per plugin. Each plugin's
    /// uninstaller (or file-delete fallback) still runs one at a time, in sequence — vendor
    /// installers are foreground GUIs, so launching several at once isn't an option — and one
    /// plugin failing never stops the rest of the batch. Failures are collected and reported
    /// together at the end, followed by a single rescan.
    /// </summary>
    private async Task UninstallBatchAsync(List<PluginDisplayViewModel> targets)
    {
        var names = string.Join("\n", targets.Select(v => v.Name));
        var confirm = MessageBox.Show(
            $"Uninstall these {targets.Count} plugins?\n\n{names}\n\n"
            + "Plugins with a registered uninstaller will open their installer window one at a time — "
            + "close each as it appears to continue to the next. This cannot be undone.",
            "Uninstall Plugins",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var installedPrograms = _uninstallerLookup.EnumerateInstalledPrograms().ToList();
        var failures = new List<string>();
        var anyChanged = false;

        foreach (var vm in targets)
        {
            var uninstaller = UninstallerLookup.FindUninstaller(installedPrograms, vm.Name, vm.Vendor);
            if (uninstaller is not null)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{uninstaller.UninstallCommand}\"") { UseShellExecute = true });
                    if (process is not null)
                    {
                        await process.WaitForExitAsync();
                    }

                    anyChanged = true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
                {
                    failures.Add($"{vm.Name}: couldn't launch its uninstaller ({ex.Message})");
                }

                continue;
            }

            var deletedAny = false;
            foreach (var copy in vm.ActiveInstalls)
            {
                if (DeletePluginFile(copy.Path))
                {
                    deletedAny = true;
                }
                else
                {
                    failures.Add($"{vm.Name}: couldn't delete {Path.GetFileName(copy.Path)}");
                }
            }

            anyChanged |= deletedAny;
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(
                "Some plugins couldn't be fully uninstalled — they may be in use by another program "
                + "(close any DAW using them and try again) or require administrator rights:\n\n"
                + string.Join("\n", failures),
                "Uninstall Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        if (anyChanged)
        {
            await LoadAndScanAsync();
        }
    }

    private static bool ConfirmFinalUninstall(string pluginName)
    {
        var result = MessageBox.Show(
            $"This will permanently uninstall \"{pluginName}\" from this computer.\n\nThis action cannot be undone. Are you sure you want to continue?",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    private static bool DeletePluginFile(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void AddScanFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!_library.CustomScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _library.CustomScanFolders.Add(folder);
            _libraryStore.Save(_library);
            OnPropertyChanged(nameof(CustomScanFolders));
        }
    }

    [RelayCommand]
    private void RemoveScanFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _library.CustomScanFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _libraryStore.Save(_library);
        OnPropertyChanged(nameof(CustomScanFolders));
    }

    public IReadOnlyList<string> DefaultVst3Paths => ScanPathProvider.DefaultVst3Paths;
    public IReadOnlyList<string> DefaultVst2Paths => ScanPathProvider.DefaultVst2Paths;

    public bool ShouldShowLogoInstructions => !_library.HasSeenLogoInstructions;

    public void MarkLogoInstructionsSeen()
    {
        if (_library.HasSeenLogoInstructions)
        {
            return;
        }

        _library.HasSeenLogoInstructions = true;
        _libraryStore.Save(_library);
    }

    public async Task<(bool Success, string Message)> ExportDataAsync(string filePath)
    {
        try
        {
            var json = _dataPortability.ExportBundle();
            await File.WriteAllTextAsync(filePath, json);
            return (true, "Export complete.");
        }
        catch (IOException ex)
        {
            return (false, $"Couldn't save the export file: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ImportDataAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            _dataPortability.ImportBundle(json);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return (false, $"Couldn't import that file: {ex.Message}");
        }

        // Reload everything from the files the import just overwrote. _library is reassigned
        // first so that the property setters below (which each re-save _library as a side
        // effect) persist the freshly imported data instead of clobbering it with the stale
        // pre-import copy.
        var settings = _libraryStore.Load();
        _library = settings;
        IsDarkTheme = settings.IsDarkTheme;
        if (ColorConverter.ConvertFromString(settings.AccentColor) is Color accent)
        {
            AccentColor = accent;
        }
        AutostartEnabled = settings.AutostartEnabled;
        StartMinimized = settings.StartMinimized;
        ShowNotifications = settings.ShowNotifications;
        LastUpdateCheck = settings.LastUpdateCheck;
        LayoutMode = Enum.TryParse<LayoutMode>(settings.LayoutMode, out var layoutMode) ? layoutMode : LayoutMode.Grid;

        _exclusionList.Reload();
        _manualLogoOverrides.Reload();
        _manualMetadataOverrides.Reload();
        OnPropertyChanged(nameof(CustomScanFolders));

        await LoadAndScanAsync();

        return (true, "Import complete. Your plugin data has been restored.");
    }
}
