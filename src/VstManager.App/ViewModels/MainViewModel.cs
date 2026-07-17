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

    [ObservableProperty]
    private ManagementMode _mode = ManagementMode.Legit;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private InstalledFilterOption _installedFilter = InstalledFilterOption.All;

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

        if (result.UpdateAvailable && result.ReleaseUrl is not null)
        {
            var openResult = MessageBox.Show(
                $"A new version (v{result.LatestVersion}) is available. Open the release page?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openResult == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
            }
        }

        IsCheckingForUpdates = false;
    }

    [RelayCommand]
    private void OpenLatestRelease()
    {
        if (LatestReleaseUrl is not null)
        {
            Process.Start(new ProcessStartInfo(LatestReleaseUrl) { UseShellExecute = true });
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
    }

    partial void OnModeChanged(ManagementMode value) => RefreshViews();
    partial void OnSearchTextChanged(string value) => RefreshViews();
    partial void OnInstalledFilterChanged(InstalledFilterOption value) => RefreshViews();
    partial void OnFormatFilterChanged(FormatFilterOption value) => RefreshViews();
    partial void OnShowHiddenChanged(bool value) => RefreshViews();

    [RelayCommand]
    private void ResetFilters()
    {
        InstalledFilter = InstalledFilterOption.All;
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
    private async Task Rescan() => await LoadAndScanAsync();

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
        var path = await _logoCache.GetManualLogoPathAsync(vm.BaseName, url);
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
        KvrLookupResult? WebLookupResult);

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
        if (vm.Installed is not null)
        {
            matchedEntry = _nameMatcher.FindMatch(vm.Installed.Name, _catalog.Entries);
            catalogMatchIsNew = matchedEntry is not null && !string.Equals(matchedEntry.Name, vm.Catalog?.Name, StringComparison.Ordinal);

            if (matchedEntry is null)
            {
                webResult = await _kvrLookup.SearchAsync(vm.Installed.Name);
            }
        }

        return new AutoDetectResult(
            DetectedCurrentVersion: detectedVersion,
            VersionAlreadySet: !string.IsNullOrWhiteSpace(vm.CurrentVersion),
            MatchedCatalogEntry: matchedEntry,
            CatalogMatchIsNew: catalogMatchIsNew,
            WebLookupResult: webResult);
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
    private void BatchMarkLegit() => ApplyTagToSelected(PluginTag.Legit);

    [RelayCommand]
    private void BatchMarkCracked() => ApplyTagToSelected(PluginTag.Cracked);

    [RelayCommand]
    private void BatchMarkInstrument() => ApplyKindToSelected(PluginKind.Instrument);

    [RelayCommand]
    private void BatchMarkEffect() => ApplyKindToSelected(PluginKind.Effect);

    private void ApplyTagToSelected(PluginTag tag)
    {
        var targets = Plugins.Where(p => p.IsSelected && p.IsInstalled).ToList();
        foreach (var vm in targets)
        {
            ApplyTagToAllCopies(vm, tag);
        }

        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void ApplyKindToSelected(PluginKind kind)
    {
        var targets = Plugins.Where(p => p.IsSelected && p.IsInstalled).ToList();
        foreach (var vm in targets)
        {
            ApplyKindToAllCopies(vm, kind);
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

        var isFavorite = !vm.IsFavorite;

        foreach (var copy in vm.Installs)
        {
            copy.IsFavorite = isFavorite;

            var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.IsFavorite = isFavorite;
            }
        }

        _libraryStore.Save(_library);
        vm.RefreshInstallInfo();
        RefreshViews();
    }

    [RelayCommand]
    private void ToggleHide(PluginDisplayViewModel? vm)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        var isHidden = !vm.IsHidden;

        foreach (var copy in vm.Installs)
        {
            copy.IsHidden = isHidden;

            var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.IsHidden = isHidden;
            }
        }

        _libraryStore.Save(_library);
        vm.RefreshInstallInfo();
        RefreshViews();
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
    }

    private async Task RefreshMetadataCoreAsync(IReadOnlyList<PluginDisplayViewModel> targets)
    {
        // 1. Fill in blank Current Version from the file's embedded version, falling back
        // to the Windows Uninstall registry's DisplayVersion (enumerated once per batch,
        // not once per plugin). Never overwrites a version the user already typed in.
        var anyVersionChanged = await Task.Run(() =>
        {
            var installedPrograms = _uninstallerLookup.EnumerateInstalledPrograms().ToList();
            var changed = false;

            foreach (var vm in targets)
            {
                foreach (var copy in vm.Installs)
                {
                    if (!string.IsNullOrWhiteSpace(copy.CurrentVersion))
                    {
                        continue;
                    }

                    var detected = _versionDetector.DetectFromFile(copy.Path)
                        ?? UninstallerLookup.FindUninstaller(installedPrograms, vm.Name, vm.Vendor)?.DisplayVersion;

                    if (string.IsNullOrWhiteSpace(detected))
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

        ApplyTagToAllCopies(vm, tag);
        _libraryStore.Save(_library);
        RefreshViews();
    }

    private void SetKind(PluginDisplayViewModel? vm, PluginKind kind)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

        ApplyKindToAllCopies(vm, kind);
        _libraryStore.Save(_library);
        RefreshViews();
    }

    [RelayCommand]
    private void ShowInFolder(PluginDisplayViewModel? vm)
    {
        var path = vm?.Installed?.Path;
        if (path is null || !File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task MarkAsNotAPlugin(PluginDisplayViewModel? vm) => await MarkAsNotAPluginAsync(vm);

    public async Task<bool> MarkAsNotAPluginAsync(PluginDisplayViewModel? vm)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return false;
        }

        var fileNames = string.Join("\n", vm.Installs.Select(i => Path.GetFileName(i.Path)));
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
    private void Uninstall(PluginDisplayViewModel? vm)
    {
        if (vm is null || vm.Installs.Count == 0)
        {
            return;
        }

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
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{uninstaller.UninstallCommand}\"") { UseShellExecute = true });
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

        foreach (var copy in vm.Installs)
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
