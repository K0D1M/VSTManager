using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;
using VstManager.WinUI.Services;

namespace VstManager.WinUI.ViewModels;

public enum LibrarySort
{
    Name,
    Vendor,
    Type,
    UpdateStatus
}

public enum LibraryFilter
{
    All,
    Installed,
    Outdated,
    Favorites
}

/// <summary>
/// The library screen's state. Deliberately NOT a port of the WPF MainViewModel (2,784 lines): that
/// file is gated on four ListCollectionViews, 17 MessageBox calls and Application.Current, none of
/// which exist here. Rewriting the ~10% of it this prototype needs, straight against Core, is far
/// cheaper than unpicking those.
///
/// Filtering and sorting happen in memory rather than through a collection view — WinUI's
/// CollectionViewSource is much weaker than WPF's, and with ~115 plugins re-projecting the list is
/// instant.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryStore _libraryStore;
    private readonly PluginTagService _tagService;
    private readonly ManualMetadataOverrideService _metadataOverrides;
    private readonly ManualLogoOverrideService _logoOverrides;
    private readonly LogoCache _logoCache;
    private readonly PluginDisplayBuilder _displayBuilder = new();
    private readonly PluginCatalog _catalog = new();

    /// <summary>Everything loaded, before search/filter/sort.</summary>
    private readonly List<PluginDisplayViewModel> _allPlugins = new();

    private LibraryData _library = new();

    public LibraryViewModel()
    {
        IsolatedData.EnsureSeeded();

        // Every path is explicit — this is what keeps the prototype off the shipping app's data.
        _libraryStore = new LibraryStore(IsolatedData.Library);
        _tagService = new PluginTagService(IsolatedData.PluginTags);
        _metadataOverrides = new ManualMetadataOverrideService(IsolatedData.ManualMetadata);
        _logoOverrides = new ManualLogoOverrideService(IsolatedData.ManualLogos);

        // Logos are the exception: shared with the WPF app so artwork appears immediately instead
        // of re-downloading a whole library's worth. Content-addressed by slug, so reads are safe.
        _logoCache = new LogoCache(cacheDirectory: IsolatedData.LogoCacheFolder);
    }

    /// <summary>Grouped for the GridView's section headers (Favorites / Instruments / Effects / …).</summary>
    public ObservableCollection<PluginGroup> Groups { get; } = new();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private LibrarySort _sort = LibrarySort.Name;

    [ObservableProperty]
    private LibraryFilter _filter = LibraryFilter.All;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private PluginDisplayViewModel? _selectedPlugin;

    partial void OnSearchTextChanged(string value) => Project();

    partial void OnSortChanged(LibrarySort value) => Project();

    partial void OnFilterChanged(LibraryFilter value) => Project();

    [RelayCommand]
    private void SetSort(LibrarySort sort) => Sort = sort;

    [RelayCommand]
    private void SetFilter(LibraryFilter filter) => Filter = filter;

    /// <summary>
    /// Reads the stored library and builds display items. The Core calls here are blocking file
    /// I/O, so they run off the UI thread; only the projection touches the observable collections.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;

        var built = await Task.Run(() =>
        {
            _library = _libraryStore.Load();
            PresetTags.EnsureSeeded(_library.Tags);

            var items = _displayBuilder.Build(_catalog.Entries, _library.Plugins);
            _displayBuilder.ApplyManualOverrides(items, _metadataOverrides);
            _displayBuilder.ApplyTags(items, _tagService, _library.Tags);
            return items;
        });

        _allPlugins.Clear();
        _allPlugins.AddRange(built.Select(item => new PluginDisplayViewModel(item)));

        Project();
        IsLoading = false;

        // Artwork after the list is on screen, so the grid paints immediately.
        _ = LoadLogosAsync();
    }

    /// <summary>
    /// Resolves cached logo paths. Bounded so a large library doesn't launch hundreds of concurrent
    /// file/network operations, matching the ceiling the WPF head settled on.
    /// </summary>
    private async Task LoadLogosAsync()
    {
        using var gate = new SemaphoreSlim(8, 8);

        var work = _allPlugins.Select(async plugin =>
        {
            await gate.WaitAsync();
            try
            {
                var path = await ResolveLogoPathAsync(plugin);
                if (path is not null)
                {
                    plugin.LogoPath = path;
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(work);
    }

    private async Task<string?> ResolveLogoPathAsync(PluginDisplayViewModel plugin)
    {
        try
        {
            if (_logoOverrides.IsLocalFileOverride(plugin.BaseName)
                && _logoCache.FindManualCachedFile(plugin.BaseName) is { } local)
            {
                return local;
            }

            if (_logoOverrides.GetOverrideUrl(plugin.BaseName) is { } url)
            {
                return await _logoCache.GetManualLogoPathAsync(plugin.BaseName, url);
            }

            return plugin.Catalog is null ? null : await _logoCache.GetLogoPathAsync(plugin.Catalog);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            // A missing logo is cosmetic; never let it break the list.
            return null;
        }
    }

    /// <summary>Applies search, filter and sort, then rebuilds the grouped collection.</summary>
    private void Project()
    {
        IEnumerable<PluginDisplayViewModel> query = _allPlugins.Where(p => !p.IsHidden);

        query = Filter switch
        {
            LibraryFilter.Installed => query.Where(p => p.IsInstalled),
            LibraryFilter.Outdated => query.Where(p => p.IsOutdated),
            LibraryFilter.Favorites => query.Where(p => p.IsFavorite),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (p.Vendor?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Tags.Any(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        // Plugins missing the sorted value sink to the bottom, so sorting by vendor doesn't open on
        // a block of "unknown" — the same rule the WPF head's comparer uses.
        query = Sort switch
        {
            LibrarySort.Vendor => query
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Vendor))
                .ThenBy(p => p.Vendor, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Type => query
                .OrderBy(p => p.PrimaryTagName is null)
                .ThenBy(p => p.PrimaryTagName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.UpdateStatus => query
                .OrderBy(p => p.IsOutdated ? 0 : string.IsNullOrWhiteSpace(p.LatestVersion) ? 2 : 1)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        var visible = query.ToList();

        // Fixed group order rather than alphabetical, so the sections don't reshuffle as the
        // filter changes.
        var order = new[] { "Favorites", "Instruments", "Effects", "Unclassified" };
        var grouped = visible
            .GroupBy(p => p.GroupName)
            .OrderBy(g => Array.IndexOf(order, g.Key))
            .Select(g => new PluginGroup(g.Key, g))
            .ToList();

        Groups.Clear();
        foreach (var group in grouped)
        {
            Groups.Add(group);
        }

        var installed = _allPlugins.Count(p => p.IsInstalled);
        var outdated = _allPlugins.Count(p => p.IsOutdated);
        SummaryText = visible.Count == _allPlugins.Count
            ? $"{installed} installed · {outdated} with updates"
            : $"{visible.Count} of {_allPlugins.Count} shown · {outdated} with updates";
    }

    /// <summary>Toggles favourite and persists it to the isolated library.</summary>
    [RelayCommand]
    private void ToggleFavorite(PluginDisplayViewModel? plugin)
    {
        if (plugin is null)
        {
            return;
        }

        var makeFavorite = !plugin.IsFavorite;

        foreach (var copy in plugin.Installs)
        {
            copy.IsFavorite = makeFavorite;

            var stored = _library.Plugins.FirstOrDefault(
                p => string.Equals(p.Path, copy.Path, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.IsFavorite = makeFavorite;
            }
        }

        plugin.RefreshInstallInfo();
        _libraryStore.Save(_library);

        // The group depends on favourite state, so the sections must be rebuilt.
        Project();
    }
}

/// <summary>One section in the grouped grid. IGrouping-shaped so it binds to CollectionViewSource.</summary>
public class PluginGroup : List<PluginDisplayViewModel>
{
    public PluginGroup(string name, IEnumerable<PluginDisplayViewModel> items) : base(items) =>
        Name = name;

    public string Name { get; }

    public string CountText => Count == 1 ? "1 plugin" : $"{Count} plugins";
}
