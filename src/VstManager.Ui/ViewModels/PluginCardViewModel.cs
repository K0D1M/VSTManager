using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// One plugin as the library shows it. A thin projection over <see cref="PluginDisplayItem"/> —
/// all the real logic stays in Core.
/// </summary>
public partial class PluginCardViewModel : ObservableObject
{
    private readonly PluginDisplayItem _item;

    public PluginCardViewModel(PluginDisplayItem item)
    {
        _item = item;
        RefreshFromItem();
    }

    public PluginDisplayItem Item => _item;

    public string Name => _item.Name;
    public string? Vendor => _item.Vendor;
    public string BaseName => _item.BaseName;
    public bool IsInstalled => _item.IsInstalled;
    public bool IsRemembered => _item.IsRemembered;
    public CatalogEntry? Catalog => _item.Catalog;
    public IReadOnlyList<PluginInfo> Installs => _item.Installs;

    public string? CurrentVersion { get; private set; }
    public string? LatestVersion { get; private set; }
    public string Formats { get; private set; } = string.Empty;

    /// <summary>Chip labels, capped so a heavily tagged plugin can't stretch its card.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>
    /// Every tag id this plugin carries — uncapped, unlike <see cref="Tags"/>. The context menu
    /// ticks against this, so a tag past the display cap still shows as applied.
    /// </summary>
    public IReadOnlyCollection<string> TagIds { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasTag(string tagId) => TagIds.Contains(tagId);

    /// <summary>The tags this plugin carries as full definitions, manual first — for the detail window's picker.</summary>
    public IReadOnlyList<TagDefinition> TagDefinitions => _item.Tags;

    public bool IsAutoTag(TagDefinition tag) => _item.AutoTagIds.Contains(tag.Id);

    /// <summary>Only the copies actually on disk — anywhere a real file is needed.</summary>
    public IReadOnlyList<PluginInfo> ActiveInstalls => _item.ActiveInstalls.ToList();

    /// <summary>Applies a name/vendor correction to the underlying item and re-announces what depends on it.</summary>
    public void ApplyMetadataOverride(string? name, string? vendor)
    {
        if (name is not null)
        {
            _item.Name = name;
        }

        if (vendor is not null)
        {
            _item.Vendor = vendor;
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Vendor));
        OnPropertyChanged(nameof(Initial));
    }

    [ObservableProperty]
    private bool _isOutdated;

    [ObservableProperty]
    private PluginTagSummary _tag;

    [ObservableProperty]
    private PluginKind _kind;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Discovered by the most recent scan. Not persisted — resets on the next one.</summary>
    [ObservableProperty]
    private bool _isNew;

    /// <summary>Vendor logo, loaded lazily off the UI thread; null until it arrives.</summary>
    [ObservableProperty]
    private Bitmap? _logo;

    /// <summary>Drives the row's spinner while a metadata refresh is in flight for this plugin.</summary>
    [ObservableProperty]
    private bool _isRefreshingMetadata;

    /// <summary>
    /// The user chose to suppress the OUTDATED badge for this plugin specifically. Leaves
    /// <see cref="IsOutdated"/> itself alone — only the badge's visibility changes.
    /// </summary>
    [ObservableProperty]
    private bool _ignoreVersionCheck;

    /// <summary>What the badge actually keys off: genuinely outdated, and not silenced.</summary>
    public bool ShowOutdatedBadge => IsOutdated && !IgnoreVersionCheck;

    public bool IsLegit => Tag == PluginTagSummary.Legit;
    public bool IsCracked => Tag == PluginTagSummary.Cracked;

    /// <summary>Menu label, so one item covers both directions rather than showing a dead "Hide".</summary>
    public string HideMenuLabel => IsHidden ? "Unhide" : "Hide";

    /// <summary>Drives the quiet chip styling — no classification means nothing to highlight.</summary>
    public bool IsUnclassified => Tag == PluginTagSummary.Unclassified;

    public string TagLabel => Tag switch
    {
        PluginTagSummary.Legit => "Legit",
        PluginTagSummary.Cracked => "Cracked",
        PluginTagSummary.Both => "Mixed",
        _ => "Unclassified"
    };

    /// <summary>First letter, shown in place of a logo that hasn't loaded or doesn't exist.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();

    /// <summary>"v1.2.3 · VST3", or just the formats when no version is known.</summary>
    public string SummaryLine => string.IsNullOrWhiteSpace(CurrentVersion)
        ? Formats
        : $"v{CurrentVersion.TrimStart('v', 'V')} · {Formats}";

    /// <summary>Spells the update out, since the badge alone doesn't say what's available.</summary>
    public string? UpdateTooltip => IsOutdated
        ? $"Installed v{CurrentVersion?.TrimStart('v', 'V')} · latest v{LatestVersion?.TrimStart('v', 'V')}"
        : null;

    public bool HasTags => Tags.Count > 0;

    /// <summary>Re-reads everything derived from the underlying item after it changed.</summary>
    public void RefreshFromItem()
    {
        Tag = _item.TagSummary;
        Kind = _item.KindSummary;
        IsFavorite = _item.IsFavoriteSummary;
        IsHidden = _item.IsHiddenSummary;
        IgnoreVersionCheck = _item.IgnoreVersionCheckSummary;

        CurrentVersion = _item.Installs
            .Select(i => i.CurrentVersion)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        LatestVersion = _item.Installs
            .Select(i => i.LatestVersion)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        IsOutdated = VersionComparer.IsNewer(LatestVersion, CurrentVersion);

        Formats = string.Join(" + ", _item.ActiveInstalls
            .Select(i => i.Format)
            .Distinct()
            .OrderBy(f => f)
            .Select(f => f == PluginFormat.Vst2 ? "VST2" : "VST3"));

        Tags = _item.Tags.Take(3).Select(t => t.Name).ToList();
        TagIds = new HashSet<string>(_item.Tags.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(Formats));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagIds));
        OnPropertyChanged(nameof(TagDefinitions));
        OnPropertyChanged(nameof(ActiveInstalls));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(UpdateTooltip));
    }

    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HideMenuLabel));

    partial void OnIsOutdatedChanged(bool value) => OnPropertyChanged(nameof(ShowOutdatedBadge));

    partial void OnIgnoreVersionCheckChanged(bool value) => OnPropertyChanged(nameof(ShowOutdatedBadge));

    // Tag drives four bindings; Avalonia won't infer that from the generated setter.
    partial void OnTagChanged(PluginTagSummary value)
    {
        OnPropertyChanged(nameof(IsLegit));
        OnPropertyChanged(nameof(IsCracked));
        OnPropertyChanged(nameof(IsUnclassified));
        OnPropertyChanged(nameof(TagLabel));
    }
}
