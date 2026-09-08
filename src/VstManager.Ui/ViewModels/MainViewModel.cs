using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;
using VstManager.Core.Services.Cloud;
using VstManager.Ui.Services;
using VstManager.Ui.Themes;

namespace VstManager.Ui.ViewModels;

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
    private readonly LogoCache _logoCache = new();
    private readonly PluginVersionDetector _versionDetector = new();
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

    /// <summary>
    /// Every tag that exists, presets first then the user's own alphabetically — the order the
    /// WPF tag list uses, so the two heads agree. Assigning tags belongs to the plugin detail
    /// window rather than the context menu; this is kept ready for it.
    /// </summary>
    public ObservableCollection<TagDefinition> AvailableTags { get; } = new();

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

    [RelayCommand]
    private void SetSort(string sort)
    {
        if (Enum.TryParse<SortOption>(sort, out var s))
        {
            // Choosing the active sort again flips direction, which is what a column header
            // or sort menu is expected to do.
            if (s == Sort)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                Sort = s;
            }
        }
    }

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
    /// Re-reads the installed version from disk for the resolved targets.
    ///
    /// Deliberately narrower than the WPF app's refresh, which also asks KVR for the latest
    /// version: that path reaches the installed version through UninstallerLookup, which lives in
    /// VstManager.App and reads the Windows registry — unreferenceable from this project's plain
    /// net10.0 target, and meaningless on macOS. Only the file-based detection is portable, so
    /// that is all this does; the menu item is labelled accordingly.
    /// </summary>
    [RelayCommand]
    private async Task RefreshMetadataAsync(PluginCardViewModel? card)
    {
        var targets = ResolveTargets(card).Where(c => c.IsInstalled).ToList();
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
                        var detected = _versionDetector.DetectFromFile(copy.Path);
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
        if (card.Installs.Count == 0)
        {
            return;
        }

        var normalizedCurrent = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion.Trim();
        var normalizedLatest = string.IsNullOrWhiteSpace(latestVersion) ? null : latestVersion.Trim();

        _manualMetadataOverrides.SetVersionOverride(card.BaseName, normalizedCurrent, normalizedLatest);

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

        card.RefreshFromItem();
        SaveLibrary();
        ApplyFilters();
    }

    /// <summary>First version any installed copy's file reports, or null. Safe to call off-thread.</summary>
    public string? DetectInstalledVersion(PluginCardViewModel card) =>
        card.Item.ActiveInstalls
            .Select(copy => _versionDetector.DetectFromFile(copy.Path))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

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
            if (card.Catalog is null)
            {
                continue;
            }

            try
            {
                var path = await _logoCache.GetLogoPathAsync(card.Catalog);
                if (path is null || !File.Exists(path))
                {
                    continue;
                }

                var bitmap = await Task.Run(() => new Bitmap(path));
                await Dispatcher.UIThread.InvokeAsync(() => card.Logo = bitmap);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or HttpRequestException or TaskCanceledException
                                           or ArgumentException)
            {
                // A missing or corrupt logo is not worth surfacing — the card falls back to
                // its initial and the rest of the library keeps loading.
            }
        }
    }

    // ---- platform settings -------------------------------------------------
    // Backing fields are seeded in the constructor, so these only ever run on a real user
    // toggle and need no "still loading" guard.

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
        var search = SearchText.Trim();

        var filtered = _all.Where(p =>
            (ShowHidden || !p.IsHidden)
            && (!FavoritesOnly || p.IsFavorite)
            && (!OutdatedOnly || p.IsOutdated)
            && (Mode switch
            {
                ManagementMode.Legit => p.IsLegit,
                ManagementMode.Cracked => p.IsCracked,
                _ => true
            })
            && (search.Length == 0
                || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (p.Vendor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)));

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

        UpdateStatusLine();
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static int Compare(string? a, string? b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

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
