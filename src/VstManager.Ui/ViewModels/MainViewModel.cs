using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;
using VstManager.Core.Services.Cloud;
using VstManager.Ui.Converters;
using VstManager.Ui.Services;
using VstManager.Ui.Themes;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// One entry in the "Move to Folder" submenu — "Unfiled" (null FolderId) plus every real folder
/// by its full path. A plain bindable entry rather than PluginFolder directly, so same-named
/// folders in different branches ("Mixing / EQ" vs "Mastering / EQ") stay tellable apart.
/// </summary>
public sealed record FolderMenuEntry(string Label, string? FolderId);

#if !NO_LICENSE_STATUS
/// <summary>Which tag bucket the library list is filtered to.</summary>
public enum ManagementMode
{
    All,
    Legit,
    Cracked
}
#endif

/// <summary>How the library list is ordered.</summary>
public enum SortOption
{
    Name,
    Vendor,
    Type,
    DateAdded,
    UpdateStatus
}

public enum LayoutMode
{
    Grid,
    List
}

public enum ViewDensity
{
    Compact,
    Default,
    Large
}

/// <summary>Which install-status bucket the Filter popup narrows to.</summary>
public enum InstalledFilterOption
{
    All,
    InstalledOnly,
    NotInstalledOnly
}

/// <summary>Which plugin format the Filter popup narrows to.</summary>
public enum FormatFilterOption
{
    All,
    Vst2,
    Vst3,
    Aax,
    Clap,
    Au
}

/// <summary>
/// One pending "both sides changed" decision. Carries its own TaskCompletionSource so the
/// background sync can await an answer the UI supplies whenever the user gets to it.
/// </summary>
public sealed class CloudConflictRequest(DateTime localChangedAt, DateTime remoteChangedAt)
{
    public DateTime LocalChangedAt { get; } = localChangedAt;

    public DateTime RemoteChangedAt { get; } = remoteChangedAt;

    public TaskCompletionSource<ConflictResolution> Completion { get; } = new();
}

/// <summary>
/// Drives the Avalonia library view. Scanning, matching, tagging and version comparison all come
/// from VstManager.Core untouched — this only decides what the list shows and writes user choices
/// back to the same library.json the WPF app uses.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ScanPathProvider _scanPathProvider = new();
    private readonly ExclusionListService _exclusionList = new();
    private readonly PluginScanner _scanner;
    private readonly LibraryStore _libraryStore = new();
    private readonly PluginCatalog _catalog = new();
    private readonly PluginDisplayBuilder _displayBuilder = new();
    private readonly ManualMetadataOverrideService _manualMetadataOverrides = new();
    private readonly PluginTagService _pluginTags = new();
    private readonly PluginFolderService _pluginFolders = new();
    private readonly LogoCache _logoCache = new();
    private readonly ManualLogoOverrideService _manualLogoOverrides = new();
    private readonly PluginVersionDetector _versionDetector = new();
    private readonly KvrLookupService _kvrLookup = new();
    private readonly LookupCacheService _lookupCache = new();
    private readonly InstalledProgramsProvider _installedPrograms = new();
    private readonly DataPortabilityService _dataPortability = new();
    private readonly SwitchableCloudProvider _cloudProvider;
    private readonly MegaS4SyncProvider _megaProvider;
    private readonly GoogleDriveSyncProvider _driveProvider = new();
    private readonly Task _driveSessionRestored;
    private readonly CloudSyncService _cloudSync;

    private readonly List<PluginCardViewModel> _all = new();
    private LibraryData _library = new();

    public MainViewModel()
    {
        _scanner = new PluginScanner(_exclusionList);

        var settings = _libraryStore.Load();
        _library = settings;

        // Seeded here rather than left to whichever head happens to run first: a brand-new
        // library.json (or an existing one from before this feature) has no Categories list at
        // all, and the two presets have to exist before anything can backfill CategoryId from the
        // existing Kind field below.
        if (PresetCategories.EnsureSeeded(_library.Categories))
        {
            _libraryStore.Save(_library);
        }

        // Carry the saved appearance across from the shared library.json, so the Avalonia head
        // opens looking the way the user last left the app rather than resetting to a default.
        ThemeService.Apply(settings.IsDarkTheme ? AppThemeMode.Dark : AppThemeMode.Light);
        if (Avalonia.Media.Color.TryParse(settings.AccentColor, out var accent))
        {
            ThemeService.ApplyAccent(accent);
        }

        _layout = Enum.TryParse<LayoutMode>(settings.LayoutMode, out var lm) ? lm : LayoutMode.Grid;
        _density = Enum.TryParse<ViewDensity>(settings.ViewDensity, out var vd) ? vd : ViewDensity.Default;
        _sort = Enum.TryParse<SortOption>(settings.SortOption, out var so) ? so : SortOption.Name;
        _sortDescending = settings.SortDescending;
        _cloudSyncEnabled = settings.CloudSyncEnabled;
        _showNotifications = settings.ShowNotifications;
        _minimizeToTray = settings.MinimizeToTray;
        _pluginUpdateCheckIntervalHours = settings.PluginUpdateCheckIntervalHours;
        _checkForPluginUpdatesOnStartup = settings.CheckForPluginUpdatesOnStartup;

        // Read from the OS, not from library.json: the entry can be removed behind the app's
        // back (another tool, a user editing the registry or LaunchAgents), and the real state
        // is whatever is registered right now.
        _autostartEnabled = AutostartService.IsEnabled();

        // Generated once and then persisted: it is the key this machine's blob lives under, so
        // without it every user would collide on the same object in the bucket.
        if (string.IsNullOrWhiteSpace(settings.CloudDeviceId))
        {
            settings.CloudDeviceId = Guid.NewGuid().ToString("N");
            _libraryStore.Save(settings);
        }

        _megaProvider = new MegaS4SyncProvider(settings.CloudDeviceId);
        _isGoogleDriveSelected = settings.CloudProvider == "GoogleDrive";
        _cloudProvider = new SwitchableCloudProvider(
            _isGoogleDriveSelected ? _driveProvider : _megaProvider);

        // Silent: reuses a saved refresh token, never opens a browser at startup. The first
        // sync awaits this so it doesn't run against a not-yet-restored Drive session.
        _driveSessionRestored = RestoreDriveSessionAsync();
        _cloudSync = new CloudSyncService(
            _cloudProvider,
            _dataPortability,
            () => _library.LastCloudSyncAt,
            syncedAt =>
            {
                // Raw save, not SaveLibrary: this write *is* the tail of a sync, and queuing
                // another one from it would loop forever.
                _library.LastCloudSyncAt = syncedAt;
                _libraryStore.Save(_library);
            });

        _cloudSync.StateChanged += OnCloudSyncStateChanged;
        _cloudSync.RemoteDataApplied += OnCloudRemoteDataApplied;
        _cloudSync.ConflictResolver = AskAboutCloudConflictAsync;

        // Publish the service's opening state. Without this the status line keeps its "off"
        // default even when sync is on, because nothing has raised StateChanged yet.
        _cloudState = _cloudSync.State;
        _cloudStatusMessage = _cloudSyncEnabled
            ? _cloudSync.StatusMessage
            : "Cloud sync is off.";
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = new();

    // ---- grouped sections: one per category, then Unclassified ----------------------------

    public ObservableCollection<CategorySectionViewModel> Sections { get; } = new();

    private readonly Dictionary<string, CategorySectionViewModel> _sectionsByKey = new(StringComparer.OrdinalIgnoreCase);

    private const string UnclassifiedSectionKey = "";

    /// <summary>
    /// Every tag that exists, presets first then the user's own alphabetically — the order the
    /// WPF tag list uses, so the two heads agree. Assigning tags belongs to the plugin detail
    /// window rather than the context menu; this is kept ready for it.
    /// </summary>
    public ObservableCollection<TagDefinition> AvailableTags { get; } = new();

    /// <summary>Every category that exists — Instrument and Effect first, then the user's own
    /// alphabetically. Same ordering convention as AvailableTags.</summary>
    public ObservableCollection<CategoryDefinition> AvailableCategories { get; } = new();

    // ---- folders ------------------------------------------------------------
    // A place, not a label — a plugin sits in exactly one, unlike tags which stack freely. See
    // PluginFolder's own doc comment in Core for the fuller reasoning.

    /// <summary>Root folders only; each node holds its own children. Kept for the tree shape.</summary>
    public ObservableCollection<FolderNodeViewModel> FolderTree { get; } = new();

    /// <summary>
    /// Every node flattened depth-first, indent carrying the nesting — bound to a single
    /// ItemsControl rather than a tree control, since each folder hosts a plugin list the same
    /// way every other filter section does, which a tree control's item containers make awkward.
    /// </summary>
    public ObservableCollection<FolderNodeViewModel> VisibleFolders { get; } = new();

    /// <summary>Every folder flat, ordered by path.</summary>
    public ObservableCollection<PluginFolder> AllFolders { get; } = new();

    /// <summary>
    /// What the "Move to Folder" submenu shows: "Unfiled" first, then every folder by path.
    /// Rebuilt alongside the tree so the menu never offers a folder that no longer exists.
    /// </summary>
    public ObservableCollection<FolderMenuEntry> FolderMenuEntries { get; } = new();

    /// <summary>True once the user has made at least one folder — hides the whole section until then.</summary>
    [ObservableProperty]
    private bool _hasFolders;

    /// <summary>Plugins in no folder at all, matching the current filters.</summary>
    [ObservableProperty]
    private int _unfiledCount;

    /// <summary>Whether the folder list is shown under its header. Defaults open so a new folder is visible right after creating it.</summary>
    [ObservableProperty]
    private bool _isFoldersExpanded = true;

    [RelayCommand]
    private void ClearFolderFilter() => SelectedFolderId = null;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>What the busy overlay says — a folder scan and a web lookup share it.</summary>
    [ObservableProperty]
    private string _busyMessage = "Scanning your plugin folders…";

    [ObservableProperty]
    private string _searchText = string.Empty;

#if !NO_LICENSE_STATUS
    [ObservableProperty]
    private ManagementMode _mode = ManagementMode.All;
#endif

    [ObservableProperty]
    private SortOption _sort = SortOption.Name;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private LayoutMode _layout = LayoutMode.Grid;

    [ObservableProperty]
    private ViewDensity _density = ViewDensity.Default;

    public bool IsCompact
    {
        get => Density == ViewDensity.Compact;
        set { if (value) { Density = ViewDensity.Compact; } }
    }

    public bool IsDefaultDensity
    {
        get => Density == ViewDensity.Default;
        set { if (value) { Density = ViewDensity.Default; } }
    }

    public bool IsLarge
    {
        get => Density == ViewDensity.Large;
        set { if (value) { Density = ViewDensity.Large; } }
    }

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private bool _favoritesOnly;

    [ObservableProperty]
    private bool _outdatedOnly;

    /// <summary>Status/format/tag narrowing from the toolbar's Filter popup — WPF's InstalledFilter/
    /// FormatFilter/SelectedTagFilterIds, ported here since the Avalonia head had no equivalent at
    /// all until now (only Favourites/Updates/Hidden toggles and search existed).</summary>
    [ObservableProperty]
    private InstalledFilterOption _installedFilter = InstalledFilterOption.InstalledOnly;

    [ObservableProperty]
    private FormatFilterOption _formatFilter = FormatFilterOption.All;

    /// <summary>Tag ids currently narrowing the list. Empty means "don't filter by tag".</summary>
    public HashSet<string> SelectedTagFilterIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True while any tag filter is active — drives the Filter popup's "Clear" affordance.</summary>
    public bool HasTagFilter => SelectedTagFilterIds.Count > 0;

    public bool IsTagFilterActive(TagDefinition tag) => SelectedTagFilterIds.Contains(tag.Id);

    /// <summary>
    /// Drives the small dot badge on the toolbar's Filter button — compared against each
    /// property's own non-narrowing default, not a blanket "is default enum value" check:
    /// InstalledFilterOption's default is InstalledOnly, not All, so a naive != All check would
    /// permanently show the badge as active out of the box.
    /// </summary>
    public bool IsAnyFilterActive =>
        InstalledFilter != InstalledFilterOption.InstalledOnly
        || FormatFilter != FormatFilterOption.All
        || HasTagFilter
        || ShowHidden;

    /// <summary>
    /// Which folder the main list is narrowed to, or null for every plugin. This project's
    /// single-column card grid has no room for WPF's per-folder inline grids, so a folder acts as
    /// one more filter alongside search/tag/favourite rather than a separate nested view.
    /// </summary>
    [ObservableProperty]
    private string? _selectedFolderId;

    /// <summary>The chosen folder's own view model, for the header ("Showing: Bass ✕") — null means "All plugins".</summary>
    public FolderNodeViewModel? SelectedFolder =>
        SelectedFolderId is null ? null : VisibleFolders.FirstOrDefault(f => f.Id == SelectedFolderId);

    partial void OnSelectedFolderIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedFolder));

        foreach (var node in VisibleFolders)
        {
            node.IsSelected = string.Equals(node.Id, value, StringComparison.OrdinalIgnoreCase);
        }

        ApplyFilters();
    }

    [ObservableProperty]
    private string _statusLine = "Loading…";

    [ObservableProperty]
    private int _selectedCount;

    // Settable so the layout and mode toggles can bind IsChecked two-way. Setting one to true
    // is what selects it; the false case is ignored, since deselecting a radio button in a
    // group is meaningless — the other one becoming true is what turns this one off.
    public bool IsGrid
    {
        get => Layout == LayoutMode.Grid;
        set { if (value) { Layout = LayoutMode.Grid; } }
    }

    public bool IsList
    {
        get => Layout == LayoutMode.List;
        set { if (value) { Layout = LayoutMode.List; } }
    }

#if !NO_LICENSE_STATUS
    public bool IsModeAll
    {
        get => Mode == ManagementMode.All;
        set { if (value) { Mode = ManagementMode.All; } }
    }

    public bool IsModeLegit
    {
        get => Mode == ManagementMode.Legit;
        set { if (value) { Mode = ManagementMode.Legit; } }
    }

    public bool IsModeCracked
    {
        get => Mode == ManagementMode.Cracked;
        set { if (value) { Mode = ManagementMode.Cracked; } }
    }
#endif

    public bool IsStatusAll
    {
        get => InstalledFilter == InstalledFilterOption.All;
        set { if (value) { InstalledFilter = InstalledFilterOption.All; } }
    }

    public bool IsStatusInstalled
    {
        get => InstalledFilter == InstalledFilterOption.InstalledOnly;
        set { if (value) { InstalledFilter = InstalledFilterOption.InstalledOnly; } }
    }

    public bool IsStatusNotInstalled
    {
        get => InstalledFilter == InstalledFilterOption.NotInstalledOnly;
        set { if (value) { InstalledFilter = InstalledFilterOption.NotInstalledOnly; } }
    }

    public bool IsFormatAll
    {
        get => FormatFilter == FormatFilterOption.All;
        set { if (value) { FormatFilter = FormatFilterOption.All; } }
    }

    public bool IsFormatVst2
    {
        get => FormatFilter == FormatFilterOption.Vst2;
        set { if (value) { FormatFilter = FormatFilterOption.Vst2; } }
    }

    public bool IsFormatVst3
    {
        get => FormatFilter == FormatFilterOption.Vst3;
        set { if (value) { FormatFilter = FormatFilterOption.Vst3; } }
    }

    public bool IsFormatAax
    {
        get => FormatFilter == FormatFilterOption.Aax;
        set { if (value) { FormatFilter = FormatFilterOption.Aax; } }
    }

    public bool IsFormatClap
    {
        get => FormatFilter == FormatFilterOption.Clap;
        set { if (value) { FormatFilter = FormatFilterOption.Clap; } }
    }

    public bool IsFormatAu
    {
        get => FormatFilter == FormatFilterOption.Au;
        set { if (value) { FormatFilter = FormatFilterOption.Au; } }
    }

    /// <summary>Audio Units only exist on macOS, so the AU filter option only appears there.</summary>
    public static bool ShowAuFormat => OperatingSystem.IsMacOS();

    public bool HasSelection => SelectedCount > 0;

    /// <summary>
    /// Any one selected card, for the selection bar's buttons to pass as their parameter — the
    /// commands take a single card and expand it to the full selection themselves, so the bar
    /// reuses them unchanged rather than needing bulk-only duplicates.
    /// </summary>
    public PluginCardViewModel? SelectionAnchor => _all.FirstOrDefault(c => c.IsSelected);

    /// <summary>True when filters are hiding something, so the UI can offer a way out.</summary>
    public bool IsFiltered => Plugins.Count != _all.Count;

    public bool IsEmpty => !IsScanning && Plugins.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilters();

#if !NO_LICENSE_STATUS
    partial void OnModeChanged(ManagementMode value)
    {
        OnPropertyChanged(nameof(IsModeAll));
        OnPropertyChanged(nameof(IsModeLegit));
        OnPropertyChanged(nameof(IsModeCracked));
        ApplyFilters();
    }
#endif

    partial void OnShowHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyFilterActive));
        ApplyFilters();
    }
    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilters();
    partial void OnOutdatedOnlyChanged(bool value) => ApplyFilters();

    partial void OnInstalledFilterChanged(InstalledFilterOption value)
    {
        OnPropertyChanged(nameof(IsStatusAll));
        OnPropertyChanged(nameof(IsStatusInstalled));
        OnPropertyChanged(nameof(IsStatusNotInstalled));
        OnPropertyChanged(nameof(IsAnyFilterActive));
        ApplyFilters();
    }

    partial void OnFormatFilterChanged(FormatFilterOption value)
    {
        OnPropertyChanged(nameof(IsFormatAll));
        OnPropertyChanged(nameof(IsFormatVst2));
        OnPropertyChanged(nameof(IsFormatVst3));
        OnPropertyChanged(nameof(IsFormatAax));
        OnPropertyChanged(nameof(IsFormatClap));
        OnPropertyChanged(nameof(IsFormatAu));
        OnPropertyChanged(nameof(IsAnyFilterActive));
        ApplyFilters();
    }

    [RelayCommand]
    private void ToggleTagFilter(TagDefinition? tag)
    {
        if (tag is null)
        {
            return;
        }

        if (!SelectedTagFilterIds.Remove(tag.Id))
        {
            SelectedTagFilterIds.Add(tag.Id);
        }

        OnPropertyChanged(nameof(HasTagFilter));
        OnPropertyChanged(nameof(IsAnyFilterActive));
        ApplyFilters();
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        if (SelectedTagFilterIds.Count == 0)
        {
            return;
        }

        SelectedTagFilterIds.Clear();
        OnPropertyChanged(nameof(HasTagFilter));
        OnPropertyChanged(nameof(IsAnyFilterActive));
        ApplyFilters();
    }

    /// <summary>Resets Status/Format only — matching WPF's own ResetFilters, which leaves Hidden
    /// and tag selection untouched.</summary>
    [RelayCommand]
    private void ResetFilters()
    {
        InstalledFilter = InstalledFilterOption.InstalledOnly;
        FormatFilter = FormatFilterOption.All;
    }

    partial void OnSortChanged(SortOption value)
    {
        _library.SortOption = value.ToString();
        SaveLibrary();
        ApplyFilters();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        _library.SortDescending = value;
        SaveLibrary();
        ApplyFilters();
    }

    partial void OnDensityChanged(ViewDensity value)
    {
        _library.ViewDensity = value.ToString();
        SaveLibrary();
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IsDefaultDensity));
        OnPropertyChanged(nameof(IsLarge));
    }

    partial void OnLayoutChanged(LayoutMode value)
    {
        _library.LayoutMode = value.ToString();
        SaveLibrary();
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(IsList));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionAnchor));
        OnPropertyChanged(nameof(RefreshMetadataButtonLabel));
        OnPropertyChanged(nameof(RefreshMetadataButtonTooltip));
    }

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>Scans disk and rebuilds the list, mirroring the WPF app's load pipeline.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        BusyMessage = "Scanning your plugin folders…";
        IsScanning = true;
        var busySince = DateTime.UtcNow;
        StatusLine = "Scanning…";

        try
        {
            var (items, badgePaths) = await Task.Run(() =>
            {
                _library = _libraryStore.Load();

                // LoadAsync reassigns _library wholesale on every rescan, which would silently
                // discard the constructor's one-time seeding — found while wiring this up, since
                // Categories would otherwise only ever exist for the very first load of the
                // session. Re-seeded here too so a fresh load is never missing the two presets.
                var seeded = PresetCategories.EnsureSeeded(_library.Categories);

                // Bridges old data into the new field: a plugin already classified as Instrument/
                // Effect via the pre-existing Kind enum gets the matching CategoryId backfilled,
                // so it shows correctly under the new category sections without the user
                // re-doing anything. One-directional and one-time per plugin — once CategoryId is
                // set, this never overwrites it again even if Kind later changes independently.
                var backfilled = false;
                foreach (var plugin in _library.Plugins.Where(p => p.CategoryId is null))
                {
                    var categoryId = plugin.Kind switch
                    {
                        PluginKind.Instrument => PresetCategories.Instrument,
                        PluginKind.Effect => PresetCategories.Effect,
                        _ => null
                    };

                    if (categoryId is not null)
                    {
                        plugin.CategoryId = categoryId;
                        backfilled = true;
                    }
                }

                if (seeded || backfilled)
                {
                    _libraryStore.Save(_library);
                }

                var previousPaths = new HashSet<string>(
                    _library.Plugins.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

                var vst3 = _scanPathProvider.GetVst3Paths(_library.CustomScanFolders);
                var vst2 = _scanPathProvider.GetVst2Paths(_library.CustomScanFolders);
                var aax = _scanPathProvider.GetAaxPaths(_library.CustomScanFolders);
                var clap = _scanPathProvider.GetClapPaths(_library.CustomScanFolders);
                var au = _scanPathProvider.GetAuPaths(_library.CustomScanFolders);
                var scanned = _scanner.Scan(vst3, vst2, aax, clap, au);

                var merged = _libraryStore.MergeOnRescan(_library.Plugins, scanned);
                _library.Plugins = merged;
                _libraryStore.Save(_library);

                // Never light up the whole library as NEW on a first-ever scan: with nothing
                // to compare against, everything would qualify.
                var badges = previousPaths.Count == 0
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(
                        merged.Select(p => p.Path).Where(p => !previousPaths.Contains(p)),
                        StringComparer.OrdinalIgnoreCase);

                // Marks the persistent "still needs a category" flag at the exact moment a path
                // is recognised as new — same badges set, same first-ever-scan exemption, so a
                // brand-new library.json's initial scan doesn't retroactively demand categories
                // for a user's whole existing collection.
                // A new copy of a plugin already in the library (e.g. an AAX build of Kontakt
                // beside its VST3) is not a new plugin: it takes the known copy's category
                // instead of asking again.
                var knownCategories = merged
                    .Where(p => !badges.Contains(p.Path) && p.CategoryId is not null)
                    .GroupBy(p => PluginNameMatcher.Normalize(p.Name))
                    .ToDictionary(g => g.Key, g => g.First().CategoryId);

                foreach (var plugin in merged.Where(p => badges.Contains(p.Path)))
                {
                    if (knownCategories.TryGetValue(PluginNameMatcher.Normalize(plugin.Name), out var categoryId))
                    {
                        plugin.CategoryId = categoryId;
                        continue;
                    }

                    plugin.NeedsCategoryReview = true;
                }

                if (badges.Count > 0)
                {
                    _libraryStore.Save(_library);
                }

                var display = _displayBuilder.Build(_catalog.Entries, merged);
                _displayBuilder.ApplyManualOverrides(display, _manualMetadataOverrides);
                _displayBuilder.ApplyTags(display, _pluginTags, _library.Tags);
                return (display, badges);
            });

            RebuildAvailableTags();
            RebuildAvailableCategories();

            _all.Clear();
            foreach (var item in items)
            {
                var card = new PluginCardViewModel(item)
                {
                    IsNew = item.Installs.Any(i => badgePaths.Contains(i.Path))
                };
                card.PropertyChanged += OnCardPropertyChanged;
                _all.Add(card);
            }

            ApplyFilters();
            _ = LoadLogosAsync();

            // Plugins still missing Category: required, not optional, before a plugin is
            // considered done — broadened from the original "IsNew && multi-copy only" trigger
            // so a single-copy new plugin no longer slips through with nothing but a passive NEW
            // badge. Keyed off NeedsCategoryReview specifically, not CategoryId is null: the
            // latter would sweep in every pre-existing Unclassified plugin from before this
            // feature shipped the very first time it runs — confirmed with the user this should
            // be scoped to genuinely new plugins only. NeedsCategoryReview is set once, at the
            // moment a path is first recognised as new, and — unlike the transient IsNew flag,
            // which resets every scan — persists until SetCategory actually clears it, which is
            // what makes "Later" (which saves nothing) correctly keep re-prompting on every
            // future scan instead of the plugin quietly aging out of being "new".
            var needsInfo = _all
                .Where(c => c.NeedsCategoryReview)
                .ToList();

            if (needsInfo.Count > 0)
            {
                NewPluginsNeedInfo?.Invoke(this, needsInfo);
            }

            // Once, after the library is on screen — not on every rescan, and not before, since
            // a sync can replace the local files and would then be racing the scan that is
            // reading them.
            if (!_initialLoadComplete)
            {
                _initialLoadComplete = true;

                if (CloudSyncEnabled)
                {
                    _ = SyncCloudNowCommand.ExecuteAsync(null);
                }
            }
        }
        finally
        {
            await HoldBusyOverlayAsync(busySince);
            IsScanning = false;
        }
    }

    private bool _initialLoadComplete;

    [RelayCommand]
    private async Task RescanAsync()
    {
        await LoadAsync();
        Notify("Plugin scan complete.");
    }

    /// <summary>
    /// The toolbar's reverse-direction button. The sort choice itself now comes from the
    /// ComboBox's own two-way SelectedValue binding straight to Sort (see MainWindow.axaml) —
    /// there is no longer a command in the loop for that half at all.
    /// </summary>
    [RelayCommand]
    private void ReverseSortDirection() => SortDescending = !SortDescending;

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeService.Apply(ThemeService.IsDark ? AppThemeMode.Light : AppThemeMode.Dark);
        _library.IsDarkTheme = ThemeService.IsDark;
        SaveLibrary();
    }

    [RelayCommand]
    private void ToggleFavorite(PluginCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        ApplyToTargets(card, (info, value) => info.IsFavorite = value, !card.IsFavorite);
    }

    [RelayCommand]
    private void ToggleHidden(PluginCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        ApplyToTargets(card, (info, value) => info.IsHidden = value, !card.IsHidden);
    }

#if !NO_LICENSE_STATUS
    [RelayCommand]
    private void MarkLegit(PluginCardViewModel? card) => SetTag(card, PluginTag.Legit);

    [RelayCommand]
    private void MarkCracked(PluginCardViewModel? card) => SetTag(card, PluginTag.Cracked);

    [RelayCommand]
    private void MarkUnclassified(PluginCardViewModel? card) => SetTag(card, PluginTag.Unclassified);

    [RelayCommand]
    private void MarkAllLegit() => SetTagForVisibleInstalled(PluginTag.Legit);

    [RelayCommand]
    private void MarkAllCracked() => SetTagForVisibleInstalled(PluginTag.Cracked);

    /// <summary>
    /// Sets a tag across every currently-visible, installed plugin — the toolbar's bulk "Mark
    /// All" action, distinct from the context menu's "Mark Legit/Cracked" which only ever acts on
    /// the clicked card (or its selection). Scoped to what MatchesFilters leaves on screen, same
    /// convention as the rest of the toolbar's bulk actions, so an active search/filter is never
    /// reached past.
    /// </summary>
    private void SetTagForVisibleInstalled(PluginTag tag)
    {
        var targets = _all.Where(c => c.IsInstalled && MatchesFilters(c)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var card in targets)
        {
            foreach (var copy in card.Installs)
            {
                copy.Tag = tag;
                if (stored.TryGetValue(copy.Path, out var libraryCopy))
                {
                    libraryCopy.Tag = tag;
                }
            }

            card.RefreshFromItem();
        }

        SaveLibrary();
        ApplyFilters();
    }
#endif

    [RelayCommand]
    private void MarkInstrument(PluginCardViewModel? card) => SetKind(card, PluginKind.Instrument);

    [RelayCommand]
    private void MarkEffect(PluginCardViewModel? card) => SetKind(card, PluginKind.Effect);

#if !NO_LICENSE_STATUS
    /// <summary>
    /// Legit/Cracked is stored per installed copy, so a plugin with a VST2 and a VST3 build gets
    /// both written — that is what makes the summary chip resolve to one value rather than "Mixed".
    /// </summary>
    private void SetTag(PluginCardViewModel? card, PluginTag tag)
    {
        if (card is null || card.Installs.Count == 0)
        {
            return;
        }

        ApplyToTargets(card, (info, _) => info.Tag = tag, true);
    }
#endif

    private void SetKind(PluginCardViewModel? card, PluginKind kind)
    {
        if (card is null || card.Installs.Count == 0)
        {
            return;
        }

        // Sections group by CategoryId, so the legacy Kind actions must move it too.
        var categoryId = kind switch
        {
            PluginKind.Instrument => PresetCategories.Instrument,
            PluginKind.Effect => PresetCategories.Effect,
            _ => null
        };

        ApplyToTargets(card, (info, _) =>
        {
            info.Kind = kind;
            info.CategoryId = categoryId;
            info.NeedsCategoryReview = false;
        }, true);
    }

    /// <summary>
    /// Sets CategoryId, and — only when the chosen category is one of the two built-in presets —
    /// also sets Kind to match, so anything outside this new system that still reads Kind
    /// (context menu labels, WPF, older data) stays correct. A custom category has no Kind
    /// equivalent, so Kind is left at whatever it already was rather than forced to Unclassified,
    /// in case the plugin also happens to carry a legacy Kind from before categories existed.
    /// </summary>
    /// <summary>
    /// Not a [RelayCommand]: MVVM Toolkit's relay commands only support zero or one parameter,
    /// and this genuinely needs both the target card and the chosen category — called directly
    /// from code-behind (a chip Click handler with both values already in hand) rather than a
    /// single CommandParameter binding.
    /// </summary>
    public void SetCategory(PluginCardViewModel? card, CategoryDefinition? category)
    {
        if (card is null || card.Installs.Count == 0 || category is null)
        {
            return;
        }

        var kind = category.Id switch
        {
            PresetCategories.Instrument => PluginKind.Instrument,
            PresetCategories.Effect => PluginKind.Effect,
            _ => (PluginKind?)null
        };

        var targets = ResolveTargets(card);
        var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            foreach (var copy in target.Installs)
            {
                copy.CategoryId = category.Id;
                copy.NeedsCategoryReview = false;
                if (kind is { } k)
                {
                    copy.Kind = k;
                }

                if (stored.TryGetValue(copy.Path, out var libraryCopy))
                {
                    libraryCopy.CategoryId = category.Id;
                    libraryCopy.NeedsCategoryReview = false;
                    if (kind is { } k2)
                    {
                        libraryCopy.Kind = k2;
                    }
                }
            }

            target.RefreshFromItem();
        }

        SaveLibrary();
        ApplyFilters();
    }

    /// <summary>
    /// Adds or removes one tag across the resolved targets. The clicked card decides the
    /// direction, so a mixed selection ends up consistent rather than each item flipping to its
    /// own opposite — matching how the WPF app's tag submenu behaves.
    /// </summary>
    /// <summary>
    /// Re-reads the installed version from disk for the resolved targets, then asks KVR for each
    /// one's latest published version — the two halves of what "refresh metadata" means.
    ///
    /// The installed half falls back to the Windows uninstall registry when the plugin files
    /// carry no version, matching the WPF app; off Windows that step reports nothing and the
    /// chain simply ends earlier (see InstalledProgramsProvider).
    /// </summary>
    [RelayCommand]
    private async Task RefreshMetadataAsync(PluginCardViewModel? card)
    {
        var targets = ResolveTargets(card).Where(c => c.IsInstalled).ToList();
        await RefreshMetadataCoreAsync(targets);
    }

    /// <summary>
    /// True while any card is selected — the toolbar button reads this to decide its own label
    /// ("Check Updates for Selected" vs. "Refresh All Metadata") and, via
    /// <see cref="RefreshAllMetadataAsync"/>, its actual target set. Not the same test as
    /// <see cref="ResolveTargets"/>'s "clicked card is part of a multi-select" — this button has
    /// no clicked card of its own, so any selection at all (even a single card) narrows it.
    /// </summary>
    public string RefreshMetadataButtonLabel => HasSelection ? "Check Updates for Selected" : "Refresh All Metadata";

    public string RefreshMetadataButtonTooltip => HasSelection
        ? "Re-checks installed versions and looks up the latest release for the selected plugins"
        : "Re-checks installed versions and looks up the latest release for every installed plugin";

    /// <summary>
    /// The toolbar's refresh button — every installed plugin in the library, unless one or more
    /// are selected, in which case only those (see <see cref="RefreshMetadataButtonLabel"/> for
    /// the matching label switch). The confirmation dialog lives in code-behind (this project's
    /// established pattern; see MainWindow.axaml.cs), so by the time this runs the user has
    /// already agreed to the wait.
    /// </summary>
    public async Task RefreshAllMetadataAsync()
    {
        var targets = (HasSelection ? _all.Where(c => c.IsSelected) : _all)
            .Where(c => c.IsInstalled)
            .ToList();

        BusyMessage = "Looking up plugin info online…";
        IsScanning = true;
        var busySince = DateTime.UtcNow;
        try
        {
            // RefreshMetadataCoreAsync already raises "Metadata refresh complete." when it
            // finishes (shared with the single-plugin path), so nothing further is needed here.
            await RefreshMetadataCoreAsync(targets);
        }
        finally
        {
            await HoldBusyOverlayAsync(busySince);
            IsScanning = false;
        }
    }

    /// <summary>A scan can finish in ~0.1s; without a floor the overlay is a flicker nobody can
    /// read, which looks like nothing happened.</summary>
    private static async Task HoldBusyOverlayAsync(DateTime since)
    {
        var remaining = TimeSpan.FromMilliseconds(700) - (DateTime.UtcNow - since);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private async Task RefreshMetadataCoreAsync(IReadOnlyList<PluginCardViewModel> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var target in targets)
        {
            target.IsRefreshingMetadata = true;
        }

        try
        {
            var changed = await Task.Run(() =>
            {
                var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

                // Enumerated once for the whole batch rather than per plugin: on Windows this
                // walks three registry hives, which is far too expensive to repeat per target.
                var installedPrograms = _installedPrograms.EnumerateInstalledPrograms();
                var any = false;

                foreach (var target in targets)
                {
                    // A hand-corrected version outranks anything re-detected from disk, exactly
                    // as the WPF refresh treats it.
                    if (_manualMetadataOverrides.GetOverride(target.BaseName)?.CurrentVersion is not null)
                    {
                        continue;
                    }

                    foreach (var copy in target.Item.ActiveInstalls)
                    {
                        var detected = _versionDetector.DetectFromFile(copy.Path)
                                       ?? InstalledProgramsProvider
                                           .FindUninstaller(installedPrograms, target.Name, target.Vendor)?.DisplayVersion;

                        if (string.IsNullOrWhiteSpace(detected)
                            || string.Equals(detected, copy.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        copy.CurrentVersion = detected;
                        if (stored.TryGetValue(copy.Path, out var libraryCopy))
                        {
                            libraryCopy.CurrentVersion = detected;
                        }

                        any = true;
                    }
                }

                return any;
            });

            foreach (var target in targets)
            {
                target.RefreshFromItem();
            }

            if (changed)
            {
                SaveLibrary();
                ApplyFilters();
            }

            // The online half. Deliberately after the local pass and not inside the Task.Run
            // above: it is network-bound rather than CPU-bound, and it writes through the same
            // library objects, so it must not run concurrently with the file walk.
            await EnrichFromWebAsync(targets, ignoreCache: true);
        }
        finally
        {
            foreach (var target in targets)
            {
                target.IsRefreshingMetadata = false;
            }
        }

        Notify("Metadata refresh complete.");
    }

    // ---- online lookup -----------------------------------------------------

    /// <summary>
    /// Asks KVR for the latest published version of each target and applies what comes back.
    /// Grouped by BaseName because a VST2 and VST3 copy of the same plugin are one product to
    /// KVR — looking both up would double the requests for an identical answer.
    /// </summary>
    private async Task EnrichFromWebAsync(IReadOnlyList<PluginCardViewModel> targets, bool ignoreCache)
    {
        var byBaseName = targets
            .GroupBy(t => t.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var results = new Dictionary<string, KvrLookupResult?>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string Key, string Name, string? Vendor)>();

        foreach (var (baseName, card) in byBaseName)
        {
            // A cached answer is used unless the user explicitly asked to refresh: a normal launch
            // should cost no network at all.
            if (!ignoreCache && _lookupCache.IsFresh(baseName))
            {
                results[baseName] = LookupCacheService.ToResult(_lookupCache.Get(baseName));
                continue;
            }

            pending.Add((baseName, card.Name, card.Vendor));
        }

        if (pending.Count > 0)
        {
            try
            {
                var fetched = await _kvrLookup.SearchManyAsync(pending);

                foreach (var (key, result) in fetched)
                {
                    results[key] = result;

                    if (result is not null)
                    {
                        _lookupCache.Set(key, result);
                    }
                    else
                    {
                        _lookupCache.SetNotFound(key);
                    }
                }

                // One write for the whole batch rather than per result.
                _lookupCache.Save();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // Offline, or KVR unreachable. The local half of the refresh already succeeded,
                // so this is a partial result rather than a failure worth interrupting the user.
            }
        }

        ApplyLookupResults(byBaseName, results);
    }

    /// <summary>
    /// Writes lookup results through to the cards and the library: latest version, auto tags from
    /// the product's categories, and — for plugins the local catalog doesn't know — the product's
    /// name, vendor and artwork.
    /// </summary>
    private void ApplyLookupResults(
        IReadOnlyDictionary<string, PluginCardViewModel> byBaseName,
        IReadOnlyDictionary<string, KvrLookupResult?> results)
    {
        var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var anyChanged = false;
        var anyTagChanged = false;
        var logoWork = new List<(PluginCardViewModel Card, string Url)>();

        foreach (var (baseName, card) in byBaseName)
        {
            if (!results.TryGetValue(baseName, out var result) || result is null)
            {
                continue;
            }

            // A hand-entered latest version is absolute — no refresh may quietly undo it. This is
            // what keeps "UADx 1176" from being reset by a match against plain "1176".
            var manualLatest = _manualMetadataOverrides.GetOverride(baseName)?.LatestVersion;

            if (!string.IsNullOrWhiteSpace(result.LatestVersion) && manualLatest is null)
            {
                foreach (var copy in card.Installs)
                {
                    copy.LatestVersion = result.LatestVersion;

                    if (stored.TryGetValue(copy.Path, out var libraryCopy))
                    {
                        libraryCopy.LatestVersion = result.LatestVersion;
                    }
                }

                anyChanged = true;
            }

            // SetAutoTags respects tags the user applied or removed by hand, so this can run on
            // every refresh without fighting the user.
            var detectedTags = CategoryTagMapper.Map(result.Categories);
            if (detectedTags.Count > 0)
            {
                _pluginTags.SetAutoTags(baseName, detectedTags, save: false);
                anyTagChanged = true;
            }

            // Only for plugins the curated catalog doesn't cover — where it does, its name,
            // vendor and logo are the better source and must not be overwritten by a web guess.
            if (card.Catalog is null)
            {
                ApplyMetadataOverride(card, result.ProductName, result.Vendor);

                if (result.LogoUrl is not null && card.Logo is null)
                {
                    logoWork.Add((card, result.LogoUrl));
                }
            }

            card.RefreshFromItem();
        }

        if (anyTagChanged)
        {
            _pluginTags.Save();
            _displayBuilder.ApplyTags(_all.Select(c => c.Item).ToList(), _pluginTags, _library.Tags);

            foreach (var card in _all)
            {
                card.RefreshFromItem();
            }
        }

        if (anyChanged || anyTagChanged)
        {
            SaveLibrary();
            ApplyFilters();
        }

        // Fire-and-forget: artwork is decorative, and making the refresh wait on image downloads
        // would hold the spinner long after the versions are already on screen.
        foreach (var (card, url) in logoWork)
        {
            _ = FixLogoAsync(card, url);
        }
    }

    // ---- background update checks ------------------------------------------

    private static readonly TimeSpan MinUpdateCheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxUpdateCheckInterval = TimeSpan.FromHours(24);
    private CancellationTokenSource _updateChecks = new();
    private bool _isCheckingForUpdates;

    /// <summary>
    /// How often the repeating check re-runs while the app stays open, in hours. Settings binds
    /// to this directly; changing it takes effect immediately (see OnPluginUpdateCheckIntervalHoursChanged)
    /// rather than waiting for the current wait to finish — a user shortening the interval to
    /// "check more often" expects that to start applying right away, not after the old, longer
    /// wait completes once more.
    /// </summary>
    [ObservableProperty]
    private double _pluginUpdateCheckIntervalHours = 6;

    [ObservableProperty]
    private bool _checkForPluginUpdatesOnStartup = true;

    partial void OnCheckForPluginUpdatesOnStartupChanged(bool value)
    {
        _library.CheckForPluginUpdatesOnStartup = value;
        SaveLibrary();
    }

    partial void OnPluginUpdateCheckIntervalHoursChanged(double value)
    {
        _library.PluginUpdateCheckIntervalHours = value;
        SaveLibrary();

        // Restart the loop so the new interval is honoured from now, not from whenever the
        // in-flight PeriodicTimer wait happens to elapse next.
        _updateChecks.Cancel();
        _updateChecks = new CancellationTokenSource();
        _ = RunUpdateChecksAsync(skipInitialCheck: true);
    }

    /// <summary>Runs one check right away, then repeats at the configured interval while the app is open.</summary>
    public void StartUpdateChecks() => _ = RunUpdateChecksAsync(skipInitialCheck: false);

    public void StopUpdateChecks() => _updateChecks.Cancel();

    /// <param name="skipInitialCheck">
    /// True when restarting the loop after the user changed the interval mid-session — an
    /// immediate re-check isn't what "I changed the interval" means, only "wait this long instead
    /// from now on."
    /// </param>
    private async Task RunUpdateChecksAsync(bool skipInitialCheck)
    {
        if (!skipInitialCheck && CheckForPluginUpdatesOnStartup)
        {
            // The launch check trusts the lookup cache, so a normal start costs no network; the
            // repeating ones bypass it, since the cache stays "fresh" for a week and would
            // otherwise hide every release that lands while the app is running.
            await CheckForUpdatesAsync(ignoreCache: false, summariseAll: true);
        }

        var token = _updateChecks.Token;

        try
        {
            while (true)
            {
                var interval = TimeSpan.FromHours(PluginUpdateCheckIntervalHours);
                if (interval < MinUpdateCheckInterval)
                {
                    interval = MinUpdateCheckInterval;
                }
                else if (interval > MaxUpdateCheckInterval)
                {
                    interval = MaxUpdateCheckInterval;
                }

                // A fresh PeriodicTimer per wait, not one created once outside the loop: the
                // interval is re-read every iteration, so a change picked up between ticks is
                // reflected in the very next wait rather than only after a restart.
                using var timer = new PeriodicTimer(interval);
                if (!await timer.WaitForNextTickAsync(token))
                {
                    return;
                }

                await CheckForUpdatesAsync(ignoreCache: true, summariseAll: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Looks up every installed plugin and raises a notification for updates. On launch it
    /// summarises everything outdated; on later checks only plugins that became outdated, or whose
    /// latest version moved on, since the last look — so the same update is never announced twice.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool ignoreCache, bool summariseAll)
    {
        if (_isCheckingForUpdates)
        {
            return;
        }

        _isCheckingForUpdates = true;
        try
        {
            var before = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in _all.Where(c => c.IsInstalled && c.ShowOutdatedBadge))
            {
                before[card.BaseName] = card.LatestVersion;
            }

            // Already known to be outdated: looking them up again only re-confirms it.
            var targets = _all.Where(c => c.IsInstalled && !c.ShowOutdatedBadge).ToList();
            if (targets.Count > 0)
            {
                await EnrichFromWebAsync(targets, ignoreCache);
            }

            var outdated = _all.Where(c => c.IsInstalled && c.ShowOutdatedBadge).ToList();
            var report = summariseAll
                ? outdated
                : outdated.Where(c => !before.TryGetValue(c.BaseName, out var known)
                                      || !string.Equals(known, c.LatestVersion, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (report.Count == 0)
            {
                return;
            }

            if (report.Count == 1)
            {
                // Only the single-plugin case has one thumbnail to show — the plural summary
                // names no specific plugin, so there's nothing for an image to represent there.
                // A failed logo lookup (network hiccup, corrupt cache entry) must never take the
                // whole notification down with it — the text update still matters on its own.
                string? imagePath = null;
                try
                {
                    imagePath = await ResolveLogoPathAsync(report[0]);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                }

                Notify($"{report[0].Name} has a newer version available"
                    + (string.IsNullOrWhiteSpace(report[0].LatestVersion) ? "." : $" ({report[0].LatestVersion})."),
                    imagePath);
            }
            else
            {
                Notify($"{report.Count} plugins have newer versions available. Use the Updates button to see all of them.");
            }
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    /// <summary>Candidate matches for a name the user typed, for the detail window's manual search.</summary>
    public Task<IReadOnlyList<PluginInfoCandidate>> SearchCandidatesAsync(string name, string? vendor) =>
        _kvrLookup.SearchCandidatesAsync(name, vendor);

    /// <summary>Reads plugin details off a product page URL the user supplied.</summary>
    public Task<KvrLookupResult?> FetchInfoFromUrlAsync(string url) => _kvrLookup.FetchFromUrlAsync(url);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var card in Plugins)
        {
            card.IsSelected = true;
        }
    }

    /// <summary>Collapses the selection to one card — a plain click.</summary>
    public void SelectOnly(PluginCardViewModel card)
    {
        foreach (var other in _all.Where(c => c.IsSelected && !ReferenceEquals(c, card)))
        {
            other.IsSelected = false;
        }

        card.IsSelected = true;
    }

    /// <summary>
    /// Selects everything between two cards inclusive, replacing the current selection. Ranges
    /// run over <see cref="Plugins"/> rather than the unfiltered list, so a Shift-click spans
    /// what the user can actually see rather than sweeping in hidden or filtered-out plugins.
    /// </summary>
    public void SelectRange(PluginCardViewModel anchor, PluginCardViewModel target)
    {
        var from = Plugins.IndexOf(anchor);
        var to = Plugins.IndexOf(target);

        // The anchor can have been filtered away since it was set; fall back to a plain click.
        if (from < 0 || to < 0)
        {
            SelectOnly(target);
            return;
        }

        if (from > to)
        {
            (from, to) = (to, from);
        }

        foreach (var card in _all.Where(c => c.IsSelected))
        {
            card.IsSelected = false;
        }

        for (var i = from; i <= to; i++)
        {
            Plugins[i].IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var card in _all.Where(c => c.IsSelected))
        {
            card.IsSelected = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
#if !NO_LICENSE_STATUS
        Mode = ManagementMode.All;
#endif
        FavoritesOnly = false;
        OutdatedOnly = false;
        ShowHidden = false;
    }

    /// <summary>
    /// Every plugin an action should hit: the whole selection when the clicked card is part of
    /// one, otherwise just the card itself — the bulk-edit convention the WPF app uses.
    /// </summary>
    private List<PluginCardViewModel> ResolveTargets(PluginCardViewModel? clicked)
    {
        if (clicked is null)
        {
            return [];
        }

        return clicked.IsSelected && SelectedCount > 1
            ? _all.Where(c => c.IsSelected).ToList()
            : [clicked];
    }

    // ---- first-run classification -------------------------------------------

    /// <summary>
    /// Raised when a scan finds plugins still needing a category (mandatory) — and, for ones with
    /// several installed copies, a per-copy Legit/Cracked call (optional, as before). Renamed
    /// from NewMultiCopyPluginsFound when this broadened beyond multi-copy plugins only. The view
    /// owns the window.
    /// </summary>
    public event EventHandler<IReadOnlyList<PluginCardViewModel>>? NewPluginsNeedInfo;

#if !NO_LICENSE_STATUS
    /// <summary>
    /// Tags one specific copy, rather than every copy of the plugin. This is the one place that
    /// distinction matters: a plugin can have a legitimate install alongside a cracked one, and
    /// the per-plugin commands would flatten both to the same value.
    /// </summary>
    public void SetCopyTag(PluginCardViewModel card, PluginInfo copy, PluginTag tag)
    {
        copy.Tag = tag;

        var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
        if (stored is not null)
        {
            stored.Tag = tag;
        }

        SaveLibrary();
        card.RefreshFromItem();
        ApplyFilters();
    }
#endif

    // ---- cloud sync ---------------------------------------------------------

    [ObservableProperty]
    private bool _cloudSyncEnabled;

    [ObservableProperty]
    private CloudSyncState _cloudState = CloudSyncState.NotConfigured;

    [ObservableProperty]
    private string _cloudStatusMessage = "Cloud sync is off.";

    /// <summary>Identifies this user's blob in the bucket; carrying it to another machine is what pairs them.</summary>
    public string CloudDeviceId => _library.CloudDeviceId ?? string.Empty;

    /// <summary>Raised when a sync conflict needs a decision; the view owns the dialog.</summary>
    public event EventHandler<CloudConflictRequest>? CloudConflictRequested;

    // No "still loading" guard needed: the constructor seeds the backing field directly, so this
    // only ever runs on a real user toggle.
    partial void OnCloudSyncEnabledChanged(bool value)
    {
        _library.CloudSyncEnabled = value;
        SaveLibrary();

        if (value)
        {
            // Local may have drifted while sync was off — ask before a one-sided local change
            // silently overwrites whatever is on the remote (this is exactly the "I made edits,
            // then turned sync on, and want the option to keep the cloud copy instead" case).
            _ = RunCloudSyncAsync(confirmBeforeOverwriting: true);
        }
        else
        {
            CloudState = CloudSyncState.NotConfigured;
            CloudStatusMessage = "Cloud sync is off.";
        }
    }

    [RelayCommand]
    private Task SyncCloudNowAsync() => RunCloudSyncAsync(confirmBeforeOverwriting: false);

    private async Task RunCloudSyncAsync(bool confirmBeforeOverwriting)
    {
        if (!CloudSyncEnabled)
        {
            CloudStatusMessage = "Cloud sync is off — turn it on in Settings → Cloud.";
            return;
        }

        await _driveSessionRestored;

        if (!_cloudProvider.IsConfigured)
        {
            CloudState = CloudSyncState.NotConfigured;
            CloudStatusMessage = IsGoogleDriveSelected && _driveProvider.HasClient
                ? "Sign in to Google Drive in Settings → Cloud."
                : "The cloud service isn't set up in this build yet.";
            return;
        }

        await _cloudSync.SyncAsync(confirmBeforeOverwriting: confirmBeforeOverwriting);
    }

    public Task UploadToCloudAsync() => _cloudSync.ForceUploadAsync();

    // ---- cloud provider: MEGA or Google Drive ----

    [ObservableProperty]
    private bool _isGoogleDriveSelected;

    public bool IsMegaSelected
    {
        get => !IsGoogleDriveSelected;
        set { if (value) { IsGoogleDriveSelected = false; } }
    }

    public bool GoogleDriveAvailable => _driveProvider.HasClient;

    public bool IsGoogleSignedIn => _driveProvider.IsSignedIn;

    public string GoogleAccountText => _driveProvider.IsSignedIn
        ? $"Signed in as {_driveProvider.SignedInEmail ?? "your Google account"}"
        : "Not signed in";

    [ObservableProperty]
    private bool _isGoogleSigningIn;

    partial void OnIsGoogleDriveSelectedChanged(bool value)
    {
        _cloudProvider.Current = value ? _driveProvider : _megaProvider;
        _library.CloudProvider = value ? "GoogleDrive" : "Mega";
        // A different remote: the last-sync time belongs to the old one and would skew the
        // upload/download/conflict decision on the first sync against the new one.
        _library.LastCloudSyncAt = null;
        _libraryStore.Save(_library);
        OnPropertyChanged(nameof(IsMegaSelected));

        if (CloudSyncEnabled)
        {
            _ = SyncCloudNowCommand.ExecuteAsync(null);
        }
    }

    private async Task RestoreDriveSessionAsync()
    {
        try
        {
            await _driveProvider.RestoreSessionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Google Drive session restore failed: {ex.Message}");
        }

        Dispatcher.UIThread.Post(RaiseGoogleAccountChanged);
    }

    private void RaiseGoogleAccountChanged()
    {
        OnPropertyChanged(nameof(IsGoogleSignedIn));
        OnPropertyChanged(nameof(GoogleAccountText));
    }

    [RelayCommand]
    private async Task SignInGoogleAsync()
    {
        IsGoogleSigningIn = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _driveProvider.SignInAsync(timeout.Token);
            CloudStatusMessage = "Signed in to Google Drive.";
            if (CloudSyncEnabled && IsGoogleDriveSelected)
            {
                _ = SyncCloudNowCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            CloudStatusMessage = ex is OperationCanceledException
                ? "Google sign-in was cancelled or timed out."
                : $"Google sign-in failed: {ex.Message}";
        }
        finally
        {
            IsGoogleSigningIn = false;
            RaiseGoogleAccountChanged();
        }
    }

    [RelayCommand]
    private async Task SignOutGoogleAsync()
    {
        await _driveProvider.SignOutAsync();
        if (IsGoogleDriveSelected)
        {
            CloudState = CloudSyncState.NotConfigured;
            CloudStatusMessage = "Signed out of Google Drive.";
        }

        RaiseGoogleAccountChanged();
    }

    public Task RestoreFromCloudAsync() => _cloudSync.ForceDownloadAsync();

    /// <summary>
    /// Changes which blob this machine syncs with. Clears the last-synced stamp too, so the next
    /// sync compares against the new remote rather than the old one's timeline.
    /// </summary>
    public void SetCloudDeviceId(string deviceId)
    {
        var trimmed = deviceId.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == _library.CloudDeviceId)
        {
            return;
        }

        _library.CloudDeviceId = trimmed;
        _library.LastCloudSyncAt = null;
        SaveLibrary();

        OnPropertyChanged(nameof(CloudDeviceId));
        CloudStatusMessage = "Sync id changed — restart VST Manager to sync with it.";
    }

    private void OnCloudSyncStateChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CloudState = _cloudSync.State;
            CloudStatusMessage = _cloudSync.StatusMessage;
        });

    /// <summary>A download replaced the local files, so everything on screen is stale.</summary>
    private void OnCloudRemoteDataApplied(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(async () =>
        {
            _pluginTags.Reload();
            _manualMetadataOverrides.Reload();
            await LoadAsync();
        });

    /// <summary>
    /// Hands the conflict to the view and waits for the answer. Marshalled to the UI thread
    /// because the sync runs on a background one.
    /// </summary>
    private Task<ConflictResolution> AskAboutCloudConflictAsync(DateTime localChangedAt, DateTime remoteChangedAt)
    {
        var request = new CloudConflictRequest(localChangedAt, remoteChangedAt);

        Dispatcher.UIThread.Post(() =>
        {
            if (CloudConflictRequested is null)
            {
                // Nothing is listening, so nobody can answer — leaving both sides untouched is
                // the only safe outcome.
                request.Completion.TrySetResult(ConflictResolution.Skip);
                return;
            }

            CloudConflictRequested.Invoke(this, request);
        });

        return request.Completion.Task;
    }

    // ---- self-update ----------------------------------------------------------
    // Ported from VstManager.App/ViewModels/MainViewModel.cs — same state shape, same GitHub
    // release check, same download-and-launch-installer flow. Confirmation and the install
    // trigger live in SettingsWindow.axaml.cs's click handler rather than here, since this class
    // stays dialog-free by design (see DetailRequested/SettingsRequested above) and
    // ConfirmDialog.ShowAsync needs an owning Window this view model doesn't have.

    private readonly UpdateChecker _updateChecker = new();

    public static string CurrentVersion => UpdateChecker.CurrentVersion;

    [ObservableProperty]
    private DateTime? _lastUpdateCheck;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    // Named distinctly from the unrelated plugin-metadata checker's own _isCheckingForUpdates
    // busy-flag earlier in this file — two separate "update" concepts that happen to coexist,
    // same as they do in WPF (app self-update vs. per-plugin newer-version lookups).
    [ObservableProperty]
    private bool _isCheckingForAppUpdate;

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

    /// <summary>
    /// Runs the check only — does not confirm or install. SettingsWindow.axaml.cs's click handler
    /// (and the startup call in MainWindow.axaml.cs's OnLoaded) decides what to do with the result,
    /// same separation of concerns as WPF's CheckForUpdates deciding to show a MessageBox itself,
    /// just moved to where a Window actually exists here.
    /// </summary>
    [RelayCommand]
    private async Task CheckForAppUpdateAsync()
    {
        IsCheckingForAppUpdate = true;
        UpdateStatusText = string.Empty;

        try
        {
            var result = await _updateChecker.CheckForUpdateAsync();

            LastUpdateCheck = DateTime.Now;
            _library.LastUpdateCheck = LastUpdateCheck;
            SaveLibrary();

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
        }
        finally
        {
            IsCheckingForAppUpdate = false;
        }
    }

    /// <summary>
    /// If a matching installer asset was found, downloads and launches it, then exits the app —
    /// otherwise just opens the release page in the browser. The caller (SettingsWindow.axaml.cs)
    /// is expected to have already confirmed with the user before calling this, exactly as WPF
    /// confirms before calling its own equivalent.
    /// </summary>
    public async Task InstallOrOpenReleaseAsync()
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

    /// <summary>
    /// Returns "download failed" as a message the caller can show, or null on success — mirrors
    /// WPF's own no-verification download-then-launch-then-exit sequence exactly (confirmed with
    /// the user: parity, not a hardening pass). UseShellExecute on macOS opens/mounts the .dmg in
    /// Finder rather than silently installing — there is no scriptable macOS equivalent of running
    /// a Windows installer unattended, so the user still finishes the install by hand there.
    /// </summary>
    private async Task InstallUpdateAsync(string downloadUrl)
    {
        IsInstallingUpdate = true;
        try
        {
            var destinationPath = Path.Combine(Path.GetTempPath(), UpdateChecker.InstallerAssetName);
            var success = await _updateChecker.DownloadInstallerAsync(downloadUrl, destinationPath);

            if (!success)
            {
                UpdateStatusText = "Couldn't download the update. Try again later, or use the button again to open the release page instead.";
                return;
            }

            Process.Start(new ProcessStartInfo(destinationPath) { UseShellExecute = true });

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    // ---- detail window ------------------------------------------------------

    /// <summary>Raised when a plugin should open in the detail window; the view owns dialogs.</summary>
    public event EventHandler<PluginCardViewModel>? DetailRequested;

    /// <summary>Raised when Settings should open, carrying the view model the window binds to.</summary>
    public event EventHandler<SettingsViewModel>? SettingsRequested;

    [RelayCommand]
    private void OpenSettings()
    {
        var settings = new SettingsViewModel(this, _libraryStore, _pluginTags, _library);

        // A changed scan folder or a deleted tag invalidates what is on screen, so rescan.
        settings.LibraryReloadRequested += async (_, _) => await LoadAsync();

        SettingsRequested?.Invoke(this, settings);
    }

    [RelayCommand]
    private void OpenDetails(PluginCardViewModel? card)
    {
        if (card is not null)
        {
            DetailRequested?.Invoke(this, card);
        }
    }

    public bool PluginHasTag(PluginCardViewModel card, TagDefinition tag) =>
        _pluginTags.HasTag(card.BaseName, tag.Id);

    /// <summary>
    /// Adds or removes one tag across the resolved targets. The given card decides the direction,
    /// so a mixed selection ends up consistent rather than each item flipping to its own opposite.
    /// </summary>
    public void ToggleTag(PluginCardViewModel card, TagDefinition tag)
    {
        var shouldAdd = !_pluginTags.HasTag(card.BaseName, tag.Id);

        foreach (var target in ResolveTargets(card))
        {
            if (shouldAdd)
            {
                _pluginTags.AddTag(target.BaseName, tag.Id, save: false);
            }
            else
            {
                _pluginTags.RemoveTag(target.BaseName, tag.Id, save: false);
            }
        }

        _pluginTags.Save();
        RefreshTagsOnCards();
    }

    /// <summary>
    /// Re-resolves tag assignments onto every card and redraws the chips. Cheap enough to run
    /// wholesale; tracking which items a batch touched would buy nothing visible.
    /// </summary>
    private void RefreshTagsOnCards()
    {
        _displayBuilder.ApplyTags(_all.Select(c => c.Item).ToList(), _pluginTags, _library.Tags);

        foreach (var card in _all)
        {
            card.RefreshFromItem();
        }

        ApplyFilters();
    }

    /// <summary>Records a name/vendor correction and applies it to every card sharing the base name.</summary>
    public void ApplyMetadataOverride(PluginCardViewModel card, string? name, string? vendor)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var normalizedVendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim();

        _manualMetadataOverrides.SetOverride(card.BaseName, normalizedName, normalizedVendor);

        foreach (var other in _all.Where(c => string.Equals(c.BaseName, card.BaseName, StringComparison.OrdinalIgnoreCase)))
        {
            other.ApplyMetadataOverride(normalizedName, normalizedVendor);
        }

        ApplyFilters();
    }

    /// <summary>
    /// Writes corrected versions to every copy and records them as a manual override — without
    /// that record a later online refresh would silently put the detected value back.
    /// </summary>
    public void SetVersions(PluginCardViewModel card, string? currentVersion, string? latestVersion)
    {
        var normalizedCurrent = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion.Trim();
        var normalizedLatest = string.IsNullOrWhiteSpace(latestVersion) ? null : latestVersion.Trim();

        // Recorded unconditionally, even for a remembered (uninstalled) plugin with zero copies —
        // that is exactly the case where preserving a manually-found version matters most, since
        // there is no file left to re-detect it from. An earlier version of this method returned
        // before reaching here whenever Installs was empty, which silently dropped every version
        // correction made on an uninstalled plugin; caught by actually saving one (Arturia
        // "Analog Lab", remembered-only) and finding manual-metadata.json still empty afterwards.
        _manualMetadataOverrides.SetVersionOverride(card.BaseName, normalizedCurrent, normalizedLatest);

        if (card.Installs.Count > 0)
        {
            var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var copy in card.Installs)
            {
                copy.CurrentVersion = normalizedCurrent;
                copy.LatestVersion = normalizedLatest;

                if (stored.TryGetValue(copy.Path, out var libraryCopy))
                {
                    libraryCopy.CurrentVersion = normalizedCurrent;
                    libraryCopy.LatestVersion = normalizedLatest;
                }
            }
        }

        card.RefreshFromItem();
        SaveLibrary();
        ApplyFilters();
    }

    /// <summary>Where an installed version came from, so the UI can say rather than just show a number.</summary>
    public sealed record VersionDetectionResult(string? Version, string SourceDescription);

    /// <summary>
    /// Walks the whole fallback chain for one plugin's installed version: every copy's own file
    /// metadata first, then the Windows uninstall registry. Safe to call off-thread.
    ///
    /// The registry step reports nothing off Windows, so on macOS the chain simply ends one step
    /// earlier rather than behaving differently.
    /// </summary>
    public VersionDetectionResult DetectInstalledVersion(PluginCardViewModel card)
    {
        foreach (var copy in card.Item.ActiveInstalls)
        {
            var fromFile = _versionDetector.DetectFromFile(copy.Path);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return new VersionDetectionResult(fromFile, "the plugin file");
            }
        }

        var installedPrograms = _installedPrograms.EnumerateInstalledPrograms();
        var fromRegistry = InstalledProgramsProvider
            .FindUninstaller(installedPrograms, card.Name, card.Vendor)?.DisplayVersion;

        if (!string.IsNullOrWhiteSpace(fromRegistry))
        {
            return new VersionDetectionResult(fromRegistry, "the Windows uninstall registry");
        }

        return new VersionDetectionResult(
            null,
            OperatingSystem.IsWindows() ? "the plugin files or the Windows registry" : "the plugin files");
    }

    // ---- uninstall -----------------------------------------------------------
    // The view model stays dialog-free, so a plan is prepared here (what would happen), the
    // window confirms it with the user, then execution is requested back — mirroring how
    // MarkAsNotAPluginAsync/ExcludePathFromScanAsync already split confirm-then-act in this file.

    /// <summary>One plugin's uninstall plan: the registered uninstaller found for it, if any.</summary>
    public sealed record UninstallPlan(PluginCardViewModel Card, UninstallEntry? Uninstaller);

    /// <summary>
    /// Looks up whether each resolved target (the whole selection, if the clicked card is part
    /// of one) has a registered uninstaller, without running anything yet — the window needs this
    /// to word its confirmation correctly ("run the uninstaller" versus "delete the files").
    /// Remembered plugins are excluded: there is nothing left on disk to uninstall.
    /// </summary>
    public IReadOnlyList<UninstallPlan> PrepareUninstall(PluginCardViewModel? clicked)
    {
        var targets = ResolveTargets(clicked).Where(c => c.IsInstalled).ToList();
        var installedPrograms = _installedPrograms.EnumerateInstalledPrograms();

        return targets
            .Select(t => new UninstallPlan(t, InstalledProgramsProvider.FindUninstaller(installedPrograms, t.Name, t.Vendor)))
            .ToList();
    }

    /// <summary>
    /// Runs one plan: launches the vendor uninstaller and waits for it to close, or — when none
    /// is registered — deletes the plugin's own files directly. Returns null on success, or a
    /// human-readable reason naming the plugin, so the caller can collect several before
    /// reporting a batch.
    /// </summary>
    public async Task<string?> ExecuteUninstallAsync(UninstallPlan plan)
    {
        if (plan.Uninstaller is not null)
        {
            // InstalledProgramsProvider only ever returns a match on Windows (see its own
            // portability note), so this should be unreachable elsewhere — checked anyway
            // rather than trusting that silently, since a registry-sourced command string is
            // meaningless input to cmd.exe on any other OS.
            if (!OperatingSystem.IsWindows())
            {
                return $"{plan.Card.Name}: no supported way to launch a vendor uninstaller on this OS yet.";
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{plan.Uninstaller.UninstallCommand}\"")
                {
                    UseShellExecute = true
                });

                // Best-effort: if the launched process hands off to another and exits early,
                // nothing here can tell — the caller's rescan simply finds nothing changed and
                // the user can uninstall again if it didn't actually take.
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }

                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                return $"{plan.Card.Name}: couldn't launch its uninstaller ({ex.Message})";
            }
        }

        var failures = new List<string>();
        var deletedAny = false;

        // Active copies only — a remembered copy has no file left to delete, and PrepareUninstall
        // already excludes fully-remembered plugins, but a mixed plugin can still have some.
        foreach (var copy in plan.Card.ActiveInstalls)
        {
            if (DeletePluginFile(copy.Path))
            {
                deletedAny = true;
            }
            else
            {
                failures.Add(Path.GetFileName(copy.Path));
            }
        }

        if (failures.Count > 0)
        {
            return $"{plan.Card.Name}: couldn't delete {string.Join(", ", failures)} — it may be in use "
                  + "by another program or require administrator rights.";
        }

        return deletedAny ? null : $"{plan.Card.Name}: nothing found to delete.";
    }

    /// <summary>Deletes one plugin file or bundle directory. False on failure, never throws.</summary>
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

    /// <summary>What a non-committal auto-detect found, for the detail window to stage into its form.</summary>
    public sealed record AutoDetectResult(
        string? DetectedCurrentVersion,
        KvrLookupResult? WebLookupResult,
        IReadOnlyList<PluginInfoCandidate> WebCandidates,
        bool NeedsUserChoice);

    /// <summary>
    /// Non-mutating preview of what a refresh would find for one plugin. Deliberately writes
    /// nothing: the detail window stages the result into its form and the user commits with Save,
    /// so a wrong match is discarded simply by closing the window.
    /// </summary>
    public async Task<AutoDetectResult> PreviewAutoDetectAsync(PluginCardViewModel card)
    {
        var detectedVersion = await Task.Run(() => DetectInstalledVersion(card).Version);

        try
        {
            var candidates = await _kvrLookup.SearchCandidatesAsync(card.Name, card.Vendor);

            if (candidates.Count == 0)
            {
                return new AutoDetectResult(detectedVersion, null, [], false);
            }

            // One clearly-best answer is offered directly; anything less certain goes to the
            // picker rather than being guessed at on the user's behalf.
            var best = candidates[0];
            var isConfident = best.Confidence >= NameSimilarity.ConfidentThreshold
                              && (candidates.Count == 1 || candidates[1].Confidence < NameSimilarity.ConfidentThreshold);

            return isConfident
                ? new AutoDetectResult(detectedVersion, best.Info, candidates, false)
                : new AutoDetectResult(detectedVersion, null, candidates, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline: the file-detected version is still worth reporting.
            return new AutoDetectResult(detectedVersion, null, [], false);
        }
    }

    // ---- artwork -----------------------------------------------------------

    /// <summary>Downloads an image to a temp file so the user can see it before committing.</summary>
    public Task<string?> PreviewLogoAsync(string url) => _logoCache.DownloadPreviewAsync(url);

    /// <summary>
    /// Records a URL as this plugin's artwork and loads it. Applies to every card sharing the
    /// BaseName, since a VST2 and VST3 copy are one product and must not show different logos.
    /// </summary>
    public async Task<bool> FixLogoAsync(PluginCardViewModel card, string url)
    {
        var path = await _logoCache.GetManualLogoPathAsync(card.BaseName, url, forceRefresh: true);
        if (path is null)
        {
            return false;
        }

        _manualLogoOverrides.SetOverride(card.BaseName, url);
        await ApplyLogoFileAsync(card.BaseName, path);
        return true;
    }

    /// <summary>
    /// Copies a local image in as this plugin's artwork. Unlike a URL there is nothing to
    /// re-download later, so the override records only that a local file was used.
    /// </summary>
    public async Task<bool> SetLogoFromLocalFileAsync(PluginCardViewModel card, string filePath)
    {
        var path = await _logoCache.SaveLocalLogoAsync(card.BaseName, filePath);
        if (path is null)
        {
            return false;
        }

        _manualLogoOverrides.SetLocalFileOverride(card.BaseName);
        await ApplyLogoFileAsync(card.BaseName, path);
        return true;
    }

    /// <summary>
    /// Decodes a logo file and hands it to every card sharing the name.
    ///
    /// No cache to invalidate here, unlike the WPF app: that binds a path through a caching
    /// converter and must be told the file changed, while this binds the Bitmap itself, so
    /// assigning a new one repaints. The old bitmap is disposed rather than left to the finalizer.
    /// </summary>
    /// <summary>
    /// Decodes a logo at thumbnail size. Logo files are often 1000–2000 px wide but are only
    /// ever drawn in a box of at most 96 px, and a full-size decode costs width × height × 4
    /// bytes each — across a library that was hundreds of MB. 256 px wide stays sharp at 2x.
    /// </summary>
    private static Bitmap LoadThumbnail(string path)
    {
        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, 256, BitmapInterpolationMode.HighQuality);
    }

    private async Task ApplyLogoFileAsync(string baseName, string path)
    {
        try
        {
            var bitmap = await Task.Run(() => LoadThumbnail(path));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var other in _all.Where(c =>
                             string.Equals(c.BaseName, baseName, StringComparison.OrdinalIgnoreCase)))
                {
                    var previous = other.Logo;
                    other.Logo = bitmap;

                    if (!ReferenceEquals(previous, bitmap))
                    {
                        previous?.Dispose();
                    }
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            // An image Avalonia can't decode (WebP, or a truncated download). The override is
            // still recorded, so the card keeps its initial rather than showing a broken image.
        }
    }

    /// <summary>
    /// Excludes one install location from future scans (the file stays on disk) and rescans.
    /// Confirmation is the view's job; this only acts. Rescanning rebuilds every card, so a
    /// detail window showing this plugin should close afterwards.
    /// </summary>
    public async Task<bool> ExcludePathFromScanAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        _exclusionList.Exclude(path);
        _library.Plugins.RemoveAll(p => string.Equals(Path.GetFileName(p.Path), fileName, StringComparison.OrdinalIgnoreCase));
        SaveLibrary();

        await LoadAsync();
        return true;
    }

    /// <summary>Excludes every installed copy of a plugin from future scans, then rescans.</summary>
    public async Task<bool> MarkAsNotAPluginAsync(PluginCardViewModel? card)
    {
        // Needs a real file: excluding a remembered plugin leaves nothing for a scan to skip.
        if (card is null || !card.IsInstalled)
        {
            return false;
        }

        foreach (var copy in card.Installs)
        {
            _exclusionList.Exclude(copy.Path);
            _library.Plugins.RemoveAll(p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
        }

        SaveLibrary();
        await LoadAsync();
        return true;
    }

    /// <summary>Suppresses (or restores) the OUTDATED badge for the resolved targets.</summary>
    [RelayCommand]
    private void ToggleIgnoreVersionCheck(PluginCardViewModel? card)
    {
        if (card is null || card.Installs.Count == 0)
        {
            return;
        }

        ApplyToTargets(card, (info, value) => info.IgnoreVersionCheck = value, !card.IgnoreVersionCheck);
    }

    private void RebuildAvailableTags()
    {
        AvailableTags.Clear();

        foreach (var tag in _library.Tags
                     .OrderByDescending(t => t.IsPreset)
                     .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AvailableTags.Add(tag);
        }
    }

    private void RebuildAvailableCategories()
    {
        AvailableCategories.Clear();

        foreach (var category in PresetCategories.Ordered(_library.Categories))
        {
            AvailableCategories.Add(category);
        }
    }

    /// <summary>
    /// Settings adds/deletes categories on its own <see cref="LibraryData"/> reference (the same
    /// object, shared at construction) but has no way to tell this view model's
    /// <see cref="AvailableCategories"/> to catch up — unlike a rescan, which already calls
    /// <see cref="RebuildAvailableCategories"/> itself. Without this, a category created in
    /// Settings would not appear in the detail window's chip row until the next rescan.
    /// </summary>
    public void RefreshAvailableCategories()
    {
        RebuildAvailableCategories();
        ApplyFilters();
    }

    /// <summary>
    /// Groups the already-sorted list by category, in AvailableCategories order, with anything
    /// uncategorised (or pointing at a deleted category) in a trailing Unclassified section.
    /// </summary>
    private void RebuildSections(IReadOnlyList<PluginCardViewModel> ordered)
    {
        var wanted = new List<CategorySectionViewModel>();

        foreach (var category in AvailableCategories)
        {
            if (!_sectionsByKey.TryGetValue(category.Id, out var section))
            {
                section = new CategorySectionViewModel(category.Id, category.Icon, category.Name);
                _sectionsByKey[category.Id] = section;
            }

            section.Icon = category.Icon;
            section.Title = category.Name;
            wanted.Add(section);
        }

        if (!_sectionsByKey.TryGetValue(UnclassifiedSectionKey, out var unclassified))
        {
            unclassified = new CategorySectionViewModel(null, "❔", "Unclassified");
            _sectionsByKey[UnclassifiedSectionKey] = unclassified;
        }
        wanted.Add(unclassified);

        var byId = wanted
            .Where(s => s.CategoryId is not null)
            .ToDictionary(s => s.CategoryId!, StringComparer.OrdinalIgnoreCase);

        foreach (var section in wanted)
        {
            section.Plugins.Clear();
        }

        foreach (var p in ordered)
        {
            var target = p.CategoryId is { } id && byId.TryGetValue(id, out var s) ? s : unclassified;
            target.Plugins.Add(p);
        }

        foreach (var section in wanted)
        {
            section.Count = section.Plugins.Count;
        }

        if (!Sections.SequenceEqual(wanted))
        {
            Sections.Clear();
            foreach (var section in wanted)
            {
                Sections.Add(section);
            }
        }
    }

    /// <summary>
    /// Applies a change to every resolved target, writing through to both the display item's
    /// PluginInfo and the library's own copy of it.
    /// </summary>
    private void ApplyToTargets(PluginCardViewModel clicked, Action<PluginInfo, bool> set, bool value)
    {
        var targets = ResolveTargets(clicked);
        var stored = _library.Plugins.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var card in targets)
        {
            foreach (var copy in card.Installs)
            {
                set(copy, value);

                // The display item holds its own PluginInfo instances, so the library's copy has
                // to be updated too or the change is lost on the next load.
                if (stored.TryGetValue(copy.Path, out var libraryCopy))
                {
                    set(libraryCopy, value);
                }
            }

            card.RefreshFromItem();
        }

        SaveLibrary();
        ApplyFilters();
    }

    private void OnCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginCardViewModel.IsSelected))
        {
            SelectedCount = _all.Count(c => c.IsSelected);
        }
    }

    /// <summary>
    /// Fetches vendor logos in the background. Deliberately sequential and fire-and-forget: they
    /// are decoration, so they must never delay or block the list appearing.
    /// </summary>
    private async Task LoadLogosAsync()
    {
        foreach (var card in _all.ToList())
        {
            try
            {
                var path = await ResolveLogoPathAsync(card);
                if (path is null || !File.Exists(path))
                {
                    continue;
                }

                var bitmap = await Task.Run(() => LoadThumbnail(path));
                await Dispatcher.UIThread.InvokeAsync(() => card.Logo = bitmap);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or HttpRequestException or TaskCanceledException
                                           or ArgumentException or NotSupportedException)
            {
                // A missing or corrupt logo is not worth surfacing — the card falls back to
                // its initial and the rest of the library keeps loading.
            }
        }
    }

    /// <summary>
    /// Where a card's artwork comes from, in priority order: a local file the user chose, then a
    /// URL they set, then the curated catalog. The two override steps come first so a correction
    /// always wins, and they are the reason an uncatalogued plugin can have artwork at all.
    /// </summary>
    private async Task<string?> ResolveLogoPathAsync(PluginCardViewModel card)
    {
        // Nothing to re-download for a local file — the cached copy is the only copy.
        if (_manualLogoOverrides.IsLocalFileOverride(card.BaseName))
        {
            var cached = _logoCache.FindManualCachedFile(card.BaseName);
            if (cached is not null)
            {
                return cached;
            }
        }

        var overrideUrl = _manualLogoOverrides.GetOverrideUrl(card.BaseName);
        if (overrideUrl is not null)
        {
            var manual = await _logoCache.GetManualLogoPathAsync(card.BaseName, overrideUrl);
            if (manual is not null)
            {
                return manual;
            }
        }

        return card.Catalog is null ? null : await _logoCache.GetLogoPathAsync(card.Catalog);
    }

    // ---- platform settings -------------------------------------------------
    // Backing fields are seeded in the constructor, so these only ever run on a real user
    // toggle and need no "still loading" guard.

    /// <summary>
    /// The zoom this session should open at, from the shared library.json. Clamped on read: the
    /// file is also written by the WPF head and by hand, so an out-of-range value must not get
    /// through and leave the UI unusably large or small.
    /// </summary>
    public double SavedUiScale => Math.Clamp(_library.UiScale, UiScale.Min, UiScale.Max);

    /// <summary>Persists a new zoom, skipping the write when nothing actually changed.</summary>
    public void SaveUiScale(double factor)
    {
        if (Math.Abs(_library.UiScale - factor) < 0.001)
        {
            return;
        }

        _library.UiScale = factor;
        SaveLibrary();
    }

    /// <summary>Whether the app registers itself to launch at login.</summary>
    [ObservableProperty]
    private bool _autostartEnabled;

    /// <summary>Whether informational toasts are shown after scans and refreshes.</summary>
    [ObservableProperty]
    private bool _showNotifications;

    /// <summary>Whether closing the window hides it to the tray instead of exiting.</summary>
    [ObservableProperty]
    private bool _minimizeToTray;

    /// <summary>Set when the OS refused the autostart write, so Settings can say so rather than silently reverting.</summary>
    [ObservableProperty]
    private string? _autostartError;

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (AutostartService.SetEnabled(value))
        {
            AutostartError = null;
        }
        else
        {
            // Report it rather than leaving the checkbox claiming something untrue.
            AutostartError = "Could not change the launch-at-login setting.";
        }

        _library.AutostartEnabled = value;
        SaveLibrary();
    }

    partial void OnShowNotificationsChanged(bool value)
    {
        _library.ShowNotifications = value;
        SaveLibrary();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _library.MinimizeToTray = value;
        SaveLibrary();
    }

    /// <summary>
    /// Raised for notices the user asked to see. The view owns the toast host, so the view model
    /// stays free of Avalonia controls exactly as it is for dialogs.
    /// </summary>
    public event EventHandler<(string Title, string Message, string? ImagePath)>? NotificationRequested;

    private void Notify(string message, string? imagePath = null)
    {
        if (ShowNotifications)
        {
            NotificationRequested?.Invoke(this, ("VST Manager", message, imagePath));
        }
    }

    private void SaveLibrary() => _libraryStore.Save(_library);

    private void ApplyFilters()
    {
        var filtered = _all.Where(MatchesFilters);

        // Every sort falls back to name, so plugins sharing a vendor or type keep a stable,
        // predictable order rather than shuffling between rebuilds.
        Comparison<PluginCardViewModel> comparison = Sort switch
        {
            SortOption.Vendor => (a, b) => Compare(a.Vendor, b.Vendor) is var v and not 0
                ? v : Compare(a.Name, b.Name),
            SortOption.Type => (a, b) => a.Kind.CompareTo(b.Kind) is var k and not 0
                ? k : Compare(a.Name, b.Name),
            SortOption.UpdateStatus => (a, b) => b.IsOutdated.CompareTo(a.IsOutdated) is var o and not 0
                ? o : Compare(a.Name, b.Name),
            // Newest first by default (b compared against a, not a against b) — matching WPF's
            // PluginComparer, which reverses the natural DateTime? order for exactly this reason;
            // SortDescending still flips it like every other sort, on top of this base direction.
            SortOption.DateAdded => (a, b) => Nullable.Compare(b.FirstSeenAt, a.FirstSeenAt) is var d and not 0
                ? d : Compare(a.Name, b.Name),
            _ => (a, b) => Compare(a.Name, b.Name)
        };

        var ordered = filtered.ToList();
        ordered.Sort(comparison);
        if (SortDescending)
        {
            ordered.Reverse();
        }

        Plugins.Clear();
        foreach (var p in ordered)
        {
            Plugins.Add(p);
        }

        RebuildSections(ordered);

        UpdateStatusLine();
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(IsEmpty));

        // Rebuilt here rather than left to call separately: folders show filed plugins filtered
        // exactly like the main list, so they must never drift out of step with what Plugins
        // just became.
        RebuildFolderTree();
    }

    private static int Compare(string? a, string? b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every filter the toolbar applies, as a single predicate — shared by ApplyFilters (the main
    /// list) and the folder tree, so a plugin hidden by a filter is hidden inside a folder too
    /// rather than folders appearing to ignore the toolbar.
    /// </summary>
    private bool MatchesFilters(PluginCardViewModel p)
    {
        var search = SearchText.Trim();

        return (ShowHidden || !p.IsHidden)
            && (!FavoritesOnly || p.IsFavorite)
            && (!OutdatedOnly || p.IsOutdated)
            && (SelectedFolderId is null
                || string.Equals(_pluginFolders.GetFolderId(p.BaseName), SelectedFolderId, StringComparison.OrdinalIgnoreCase))
#if !NO_LICENSE_STATUS
            && (Mode switch
            {
                ManagementMode.Legit => p.IsLegit,
                ManagementMode.Cracked => p.IsCracked,
                _ => true
            })
#endif
            && (InstalledFilter switch
            {
                InstalledFilterOption.InstalledOnly => p.IsInstalled,
                InstalledFilterOption.NotInstalledOnly => !p.IsInstalled,
                _ => true
            })
            && (FormatFilter switch
            {
                FormatFilterOption.Vst2 => !p.IsInstalled || p.ActiveInstalls.Any(i => i.Format == PluginFormat.Vst2),
                FormatFilterOption.Vst3 => !p.IsInstalled || p.ActiveInstalls.Any(i => i.Format == PluginFormat.Vst3),
                FormatFilterOption.Aax => !p.IsInstalled || p.ActiveInstalls.Any(i => i.Format == PluginFormat.Aax),
                FormatFilterOption.Clap => !p.IsInstalled || p.ActiveInstalls.Any(i => i.Format == PluginFormat.Clap),
                FormatFilterOption.Au => !p.IsInstalled || p.ActiveInstalls.Any(i => i.Format == PluginFormat.Au),
                _ => true
            })
            // Multiple selected tags narrow rather than widen (a plugin must carry all of them),
            // matching WPF's own AND semantics for combining e.g. Synth + Analog.
            && (SelectedTagFilterIds.Count == 0
                || SelectedTagFilterIds.All(id => p.TagDefinitions.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))))
            && (search.Length == 0
                || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (p.Vendor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    // ---- folders -------------------------------------------------------------

    /// <summary>
    /// Rebuilds the tree and refills it from the current filing. Cheap enough to run on any
    /// change (folders number in the tens), and rebuilding wholesale is what keeps counts,
    /// nesting and ordering consistent after a move or delete — matching the WPF app's approach.
    /// </summary>
    private void RebuildFolderTree()
    {
        FolderTree.Clear();
        VisibleFolders.Clear();
        AllFolders.Clear();

        FolderMenuEntries.Clear();
        FolderMenuEntries.Add(new FolderMenuEntry("Unfiled", null));

        foreach (var folder in _library.Folders.OrderBy(
                     f => PluginFolderService.GetPath(_library.Folders, f.Id),
                     StringComparer.CurrentCultureIgnoreCase))
        {
            AllFolders.Add(folder);
            FolderMenuEntries.Add(new FolderMenuEntry(PluginFolderService.GetPath(_library.Folders, folder.Id), folder.Id));
        }

        foreach (var root in PluginFolderService.GetChildren(_library.Folders, null))
        {
            FolderTree.Add(BuildFolderNode(root, depth: 0));
        }

        foreach (var node in FolderTree.SelectMany(n => n.SelfAndDescendants()))
        {
            node.NotifyCountsChanged();
            node.IsSelected = string.Equals(node.Id, SelectedFolderId, StringComparison.OrdinalIgnoreCase);
            VisibleFolders.Add(node);
        }

        HasFolders = _library.Folders.Count > 0;
        UnfiledCount = Plugins.Count(p => _pluginFolders.GetFolderId(p.BaseName) is null);

        // SelectedFolder's lookup depends on the collection just rebuilt above, so a viewer
        // bound to it (the "Showing: X" header) needs telling even though SelectedFolderId
        // itself did not change.
        OnPropertyChanged(nameof(SelectedFolder));

        // A folder that no longer exists (deleted) leaves the filter stuck narrowing to nothing
        // with no way back except manually clearing it. Re-entrancy guarded: the property setter
        // calls ApplyFilters, which calls this — without the guard, clearing it here would start
        // a second full rebuild from inside the first.
        if (SelectedFolderId is not null && !_library.Folders.Any(f => f.Id == SelectedFolderId) && !_clearingSelectedFolder)
        {
            _clearingSelectedFolder = true;
            try
            {
                SelectedFolderId = null;
            }
            finally
            {
                _clearingSelectedFolder = false;
            }
        }
    }

    private bool _clearingSelectedFolder;

    private FolderNodeViewModel BuildFolderNode(PluginFolder folder, int depth)
    {
        var node = new FolderNodeViewModel(folder, depth);

        foreach (var child in PluginFolderService.GetChildren(_library.Folders, folder.Id))
        {
            node.Children.Add(BuildFolderNode(child, depth + 1));
        }

        // Filed plugins still obey the search box and filters: Plugins is already the filtered,
        // ordered list ApplyFilters just produced, so this is consistent with the main view by
        // construction rather than needing its own filter pass.
        foreach (var plugin in Plugins.Where(p =>
                     string.Equals(_pluginFolders.GetFolderId(p.BaseName), folder.Id, StringComparison.OrdinalIgnoreCase)))
        {
            node.Plugins.Add(plugin);
        }

        return node;
    }

    [RelayCommand]
    private void CreateFolder(string? parentId)
    {
        var folder = new PluginFolder
        {
            Name = NextUntitledFolderName(),
            ParentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId,
            ColorHex = string.IsNullOrWhiteSpace(_library.AccentColor) ? "#FF8A5CF6" : _library.AccentColor,
            SortOrder = _library.Folders.Count
        };

        _library.Folders.Add(folder);
        SaveLibrary();
        ApplyFilters();

        FolderEditRequested?.Invoke(this, folder);
    }

    /// <summary>Picks "New Folder", "New Folder 2", … so repeated adds don't all share a name.</summary>
    private string NextUntitledFolderName()
    {
        const string baseName = "New Folder";
        if (!_library.Folders.Any(f => string.Equals(f.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!_library.Folders.Any(f => string.Equals(f.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    /// <summary>Raised so the window can open the folder editor; the view model stays dialog-free.</summary>
    public event EventHandler<PluginFolder>? FolderEditRequested;

    [RelayCommand]
    private void EditFolder(PluginFolder? folder)
    {
        if (folder is not null)
        {
            FolderEditRequested?.Invoke(this, folder);
        }
    }

    /// <summary>Persists edits made in the folder editor and redraws.</summary>
    public void ApplyFolderEdit(PluginFolder folder, string name, string icon, string colorHex)
    {
        folder.Name = string.IsNullOrWhiteSpace(name) ? folder.Name : name.Trim();
        folder.Icon = icon.Trim();
        folder.ColorHex = string.IsNullOrWhiteSpace(colorHex) ? folder.ColorHex : colorHex.Trim();

        SaveLibrary();
        ApplyFilters();
    }

    /// <summary>
    /// What deleting a folder would affect, so the caller can confirm before DeleteFolder acts —
    /// the view model stays dialog-free, so it reports rather than asks.
    /// </summary>
    public (int SubfolderCount, int AffectedPlugins) DescribeFolderDeletion(PluginFolder folder)
    {
        var subtree = PluginFolderService.GetSubtree(_library.Folders, folder.Id);
        var affected = subtree.Sum(f => _pluginFolders.CountFor(f.Id));
        return (subtree.Count - 1, affected);
    }

    /// <summary>
    /// Deletes a folder and everything nested inside it. The plugins are only unfiled, never
    /// touched — a folder is a label on where something sits, so deleting one must not look like
    /// it deleted the user's plugins.
    /// </summary>
    [RelayCommand]
    private void DeleteFolder(PluginFolder? folder)
    {
        if (folder is null)
        {
            return;
        }

        var subtree = PluginFolderService.GetSubtree(_library.Folders, folder.Id);

        foreach (var doomed in subtree)
        {
            _pluginFolders.ClearFolder(doomed.Id, save: false);
            _library.Folders.RemoveAll(f => string.Equals(f.Id, doomed.Id, StringComparison.OrdinalIgnoreCase));
        }

        _pluginFolders.Save();
        SaveLibrary();
        ApplyFilters();
    }

    /// <summary>Promotes a nested folder back to the top level.</summary>
    [RelayCommand]
    private void MoveFolderToRoot(string? folderId)
    {
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            MoveFolder(folderId, null);
        }
    }

    /// <summary>
    /// Pure predicate version of MoveFolder's own cycle guard, for a live "can I drop here" check
    /// during drag — same as WPF's CanMoveFolder, used by FolderRow_DragOver to light up (or
    /// refuse) the drop-target highlight before the user actually releases.
    /// </summary>
    public bool CanMoveFolder(string folderId, string? newParentId) =>
        !PluginFolderService.WouldCreateCycle(_library.Folders, folderId, newParentId);

    /// <summary>
    /// Re-parents a folder. Refuses a move that would make the folder its own ancestor — the
    /// same guard WPF's drag-and-drop uses, kept here even without drag-and-drop this pass since
    /// a future menu-driven re-parent (or the deferred drag-and-drop) needs it regardless.
    /// </summary>
    public bool MoveFolder(string folderId, string? newParentId)
    {
        if (PluginFolderService.WouldCreateCycle(_library.Folders, folderId, newParentId))
        {
            return false;
        }

        var folder = _library.Folders.FirstOrDefault(f => string.Equals(f.Id, folderId, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return false;
        }

        folder.ParentId = string.IsNullOrWhiteSpace(newParentId) ? null : newParentId;
        SaveLibrary();
        ApplyFilters();
        return true;
    }

    /// <summary>
    /// Files the resolved targets (the whole selection, if the clicked card is part of one) into
    /// a folder — or back to Unfiled when folderId is null. Called from the FolderPickerWindow's
    /// result rather than bound as a command: the picker's code-behind already has both the
    /// clicked card and the chosen folder in hand, with no need for a combined CommandParameter.
    /// </summary>
    public void MovePluginsToFolder(PluginCardViewModel? clicked, string? folderId)
    {
        var targets = ResolveTargets(clicked);

        if (targets.Count == 0)
        {
            return;
        }

        foreach (var target in targets)
        {
            _pluginFolders.SetFolder(target.BaseName, folderId, save: false);
        }

        _pluginFolders.Save();
        ApplyFilters();
    }

    private void UpdateStatusLine()
    {
        var installed = _all.Count(p => p.IsInstalled);
        var outdated = _all.Count(p => p.IsOutdated);
        var remembered = _all.Count(p => p.IsRemembered);

        // "Installed" is the headline figure, matching the WPF header. Remembered entries
        // (uninstalled but still known) are counted separately rather than folded in — adding
        // them to the same total is what made this read "131 shown of 98 installed".
        var parts = new List<string>();

        if (Plugins.Count != _all.Count)
        {
            parts.Add($"{Plugins.Count} shown");
        }

        parts.Add($"{installed} installed");

        if (remembered > 0)
        {
            parts.Add($"{remembered} remembered");
        }

        if (outdated > 0)
        {
            parts.Add($"{outdated} with updates");
        }

        StatusLine = string.Join(" · ", parts);
    }
}
