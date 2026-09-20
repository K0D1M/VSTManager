using System.Collections.ObjectModel;
using System.Diagnostics;
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

/// <summary>Which tag bucket the library list is filtered to.</summary>
public enum ManagementMode
{
    All,
    Legit,
    Cracked
}

/// <summary>How the library list is ordered.</summary>
public enum SortOption
{
    Name,
    Vendor,
    Type,
    UpdateStatus
}

public enum LayoutMode
{
    Grid,
    List
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
    Vst3
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
    private readonly ICloudSyncProvider _cloudProvider;
    private readonly CloudSyncService _cloudSync;

    private readonly List<PluginCardViewModel> _all = new();
    private LibraryData _library = new();

    public MainViewModel()
    {
        _scanner = new PluginScanner(_exclusionList);

        var settings = _libraryStore.Load();
        _library = settings;

        // Carry the saved appearance across from the shared library.json, so the Avalonia head
        // opens looking the way the user last left the app rather than resetting to a default.
        ThemeService.Apply(settings.IsDarkTheme ? AppThemeMode.Dark : AppThemeMode.Light);
        if (Avalonia.Media.Color.TryParse(settings.AccentColor, out var accent))
        {
            ThemeService.ApplyAccent(accent);
        }

        _layout = Enum.TryParse<LayoutMode>(settings.LayoutMode, out var lm) ? lm : LayoutMode.Grid;
        _sort = Enum.TryParse<SortOption>(settings.SortOption, out var so) ? so : SortOption.Name;
        _sortDescending = settings.SortDescending;
        _cloudSyncEnabled = settings.CloudSyncEnabled;
        _showNotifications = settings.ShowNotifications;
        _minimizeToTray = settings.MinimizeToTray;

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

        _cloudProvider = new MegaS4SyncProvider(settings.CloudDeviceId);
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

    // ---- grouped sections (Instruments / Effects), matching the WPF sidebar ----------------

    public ObservableCollection<PluginCardViewModel> InstrumentPlugins { get; } = new();
    public ObservableCollection<PluginCardViewModel> EffectPlugins { get; } = new();
    public ObservableCollection<PluginCardViewModel> UnclassifiedKindPlugins { get; } = new();

    [ObservableProperty]
    private int _instrumentsCount;

    [ObservableProperty]
    private int _effectsCount;

    [ObservableProperty]
    private int _unclassifiedKindCount;

    [ObservableProperty]
    private bool _isInstrumentsSectionExpanded = true;

    [ObservableProperty]
    private bool _isEffectsSectionExpanded = true;

    [ObservableProperty]
    private bool _isUnclassifiedKindSectionExpanded = true;

    /// <summary>
    /// Every tag that exists, presets first then the user's own alphabetically — the order the
    /// WPF tag list uses, so the two heads agree. Assigning tags belongs to the plugin detail
    /// window rather than the context menu; this is kept ready for it.
    /// </summary>
    public ObservableCollection<TagDefinition> AvailableTags { get; } = new();

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

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ManagementMode _mode = ManagementMode.All;

    [ObservableProperty]
    private SortOption _sort = SortOption.Name;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private LayoutMode _layout = LayoutMode.Grid;

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

    partial void OnModeChanged(ManagementMode value)
    {
        OnPropertyChanged(nameof(IsModeAll));
        OnPropertyChanged(nameof(IsModeLegit));
        OnPropertyChanged(nameof(IsModeCracked));
        ApplyFilters();
    }

    partial void OnShowHiddenChanged(bool value) => ApplyFilters();
    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilters();
    partial void OnOutdatedOnlyChanged(bool value) => ApplyFilters();

    partial void OnInstalledFilterChanged(InstalledFilterOption value)
    {
        OnPropertyChanged(nameof(IsStatusAll));
        OnPropertyChanged(nameof(IsStatusInstalled));
        OnPropertyChanged(nameof(IsStatusNotInstalled));
        ApplyFilters();
    }

    partial void OnFormatFilterChanged(FormatFilterOption value)
    {
        OnPropertyChanged(nameof(IsFormatAll));
        OnPropertyChanged(nameof(IsFormatVst2));
        OnPropertyChanged(nameof(IsFormatVst3));
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
    }

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>Scans disk and rebuilds the list, mirroring the WPF app's load pipeline.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsScanning = true;
        StatusLine = "Scanning…";

        try
        {
            var (items, badgePaths) = await Task.Run(() =>
            {
                _library = _libraryStore.Load();
                var previousPaths = new HashSet<string>(
                    _library.Plugins.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

                var vst3 = _scanPathProvider.GetVst3Paths(_library.CustomScanFolders);
                var vst2 = _scanPathProvider.GetVst2Paths(_library.CustomScanFolders);
                var scanned = _scanner.Scan(vst3, vst2);

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

                var display = _displayBuilder.Build(_catalog.Entries, merged);
                _displayBuilder.ApplyManualOverrides(display, _manualMetadataOverrides);
                _displayBuilder.ApplyTags(display, _pluginTags, _library.Tags);
                return (display, badges);
            });

            RebuildAvailableTags();

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

            // Newly-found plugins with more than one copy: ask which copy is which. Scoped to
            // ones this scan just discovered, so an existing multi-copy plugin the user already
            // answered for is never asked about again.
            var newMultiCopy = _all
                .Where(c => c.IsNew && c.Installs.Count > 1 && c.Tag == PluginTagSummary.Unclassified)
                .ToList();

            if (newMultiCopy.Count > 0)
            {
                NewMultiCopyPluginsFound?.Invoke(this, newMultiCopy);
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

    [RelayCommand]
    private void MarkInstrument(PluginCardViewModel? card) => SetKind(card, PluginKind.Instrument);

    [RelayCommand]
    private void MarkEffect(PluginCardViewModel? card) => SetKind(card, PluginKind.Effect);

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

    private void SetKind(PluginCardViewModel? card, PluginKind kind)
    {
        if (card is null || card.Installs.Count == 0)
        {
            return;
        }

        ApplyToTargets(card, (info, _) => info.Kind = kind, true);
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
    /// The toolbar's "Refresh All Metadata" — every installed plugin in the library, not just the
    /// resolved selection. The confirmation dialog lives in code-behind (this project's
    /// established pattern; see MainWindow.axaml.cs), so by the time this runs the user has
    /// already agreed to the wait.
    /// </summary>
    public async Task RefreshAllMetadataAsync()
    {
        var targets = _all.Where(c => c.IsInstalled).ToList();
        IsScanning = true;
        try
        {
            // RefreshMetadataCoreAsync already raises "Metadata refresh complete." when it
            // finishes (shared with the single-plugin path), so nothing further is needed here.
            await RefreshMetadataCoreAsync(targets);
        }
        finally
        {
            IsScanning = false;
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

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private readonly CancellationTokenSource _updateChecks = new();
    private bool _isCheckingForUpdates;

    /// <summary>Runs one check right away, then repeats every few hours while the app is open.</summary>
    public void StartUpdateChecks() => _ = RunUpdateChecksAsync();

    public void StopUpdateChecks() => _updateChecks.Cancel();

    private async Task RunUpdateChecksAsync()
    {
        // The launch check trusts the lookup cache, so a normal start costs no network; the
        // repeating ones bypass it, since the cache stays "fresh" for a week and would otherwise
        // hide every release that lands while the app is running.
        await CheckForUpdatesAsync(ignoreCache: false, summariseAll: true);

        using var timer = new PeriodicTimer(UpdateCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_updateChecks.Token))
            {
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
            var targets = _all.Where(c => c.IsInstalled).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in targets.Where(c => c.ShowOutdatedBadge))
            {
                before[card.BaseName] = card.LatestVersion;
            }

            if (_library.CheckForPluginUpdatesOnStartup)
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

            Notify(report.Count == 1
                ? $"{report[0].Name} has a newer version available"
                    + (string.IsNullOrWhiteSpace(report[0].LatestVersion) ? "." : $" ({report[0].LatestVersion}).")
                : $"{report.Count} plugins have newer versions available. Use the Updates button to see all of them.");
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
        Mode = ManagementMode.All;
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
    /// Raised when a scan finds newly-installed plugins that have several copies on disk, so the
    /// user can mark each copy separately. The view owns the window.
    /// </summary>
    public event EventHandler<IReadOnlyList<PluginCardViewModel>>? NewMultiCopyPluginsFound;

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
            _ = SyncCloudNowCommand.ExecuteAsync(null);
        }
        else
        {
            CloudState = CloudSyncState.NotConfigured;
            CloudStatusMessage = "Cloud sync is off.";
        }
    }

    [RelayCommand]
    private async Task SyncCloudNowAsync()
    {
        if (!CloudSyncEnabled)
        {
            CloudStatusMessage = "Cloud sync is off — turn it on in Settings → Cloud.";
            return;
        }

        if (!_cloudProvider.IsConfigured)
        {
            CloudState = CloudSyncState.NotConfigured;
            CloudStatusMessage = "The cloud service isn't set up in this build yet.";
            return;
        }

        await _cloudSync.SyncAsync();
    }

    public Task UploadToCloudAsync() => _cloudSync.ForceUploadAsync();

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
    private async Task ApplyLogoFileAsync(string baseName, string path)
    {
        try
        {
            var bitmap = await Task.Run(() => new Bitmap(path));

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

                var bitmap = await Task.Run(() => new Bitmap(path));
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
    public event EventHandler<(string Title, string Message)>? NotificationRequested;

    private void Notify(string message)
    {
        if (ShowNotifications)
        {
            NotificationRequested?.Invoke(this, ("VST Manager", message));
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

        // Grouped by Kind, same three buckets WPF's sidebar sections use, and following the
        // same ordering/sort already computed above rather than re-deriving it.
        InstrumentPlugins.Clear();
        EffectPlugins.Clear();
        UnclassifiedKindPlugins.Clear();
        foreach (var p in ordered)
        {
            switch (p.Kind)
            {
                case PluginKind.Instrument:
                    InstrumentPlugins.Add(p);
                    break;
                case PluginKind.Effect:
                    EffectPlugins.Add(p);
                    break;
                default:
                    UnclassifiedKindPlugins.Add(p);
                    break;
            }
        }

        InstrumentsCount = InstrumentPlugins.Count;
        EffectsCount = EffectPlugins.Count;
        UnclassifiedKindCount = UnclassifiedKindPlugins.Count;

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
            && (Mode switch
            {
                ManagementMode.Legit => p.IsLegit,
                ManagementMode.Cracked => p.IsCracked,
                _ => true
            })
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
