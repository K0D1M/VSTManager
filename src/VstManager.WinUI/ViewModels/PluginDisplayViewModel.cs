using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.WinUI.ViewModels;

/// <summary>
/// Copied verbatim from the WPF head (only the namespace differs) — it has no WPF dependencies at
/// all, but the WPF project can't be referenced from here because it's a UseWPF project. The
/// duplication is the deliberate cost of leaving the shipping app untouched.
/// </summary>
public partial class PluginDisplayViewModel : ObservableObject
{
    private readonly PluginDisplayItem _item;

    public PluginDisplayViewModel(PluginDisplayItem item)
    {
        _item = item;
        RefreshInstallInfo();
    }

    public PluginDisplayItem Item => _item;

    public string Name => _item.Name;
    public string? Vendor => _item.Vendor;
    public string BaseName => _item.BaseName;
    public bool IsInstalled => _item.IsInstalled;

    /// <summary>True when this plugin was uninstalled but its details are still remembered.</summary>
    public bool IsRemembered => _item.IsRemembered;

    public IReadOnlyList<PluginInfo> Installs => _item.Installs;

    /// <summary>Only the copies actually on disk — used anywhere a real file is needed.</summary>
    public IReadOnlyList<PluginInfo> ActiveInstalls => _item.ActiveInstalls.ToList();

    public PluginInfo? Installed => _item.ActiveInstalls.FirstOrDefault() ?? _item.Installs.FirstOrDefault();
    public CatalogEntry? Catalog => _item.Catalog;

    public PluginFormat? Format => Installed?.Format;

    public string? Path => !_item.IsInstalled
        ? null
        : string.Join(Environment.NewLine, _item.ActiveInstalls.Select(i => i.Path));

    /// <summary>Vendor, or a readable stand-in — the card always shows a second line.</summary>
    public string VendorDisplay => string.IsNullOrWhiteSpace(_item.Vendor) ? "Unknown vendor" : _item.Vendor!;

    [ObservableProperty]
    private string? _logoPath;

    [ObservableProperty]
    private PluginTagSummary _tag;

    [ObservableProperty]
    private PluginKind _kind;

    [ObservableProperty]
    private string? _currentVersion;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private string? _installedFormatsText;

    [ObservableProperty]
    private string? _installedSummaryText;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private bool _isNew;

    /// <summary>True when a known LatestVersion is strictly newer than the installed CurrentVersion.</summary>
    [ObservableProperty]
    private bool _isOutdated;

    public bool HasFormat(PluginFormat format) => _item.Installs.Any(i => i.Format == format);

    public IReadOnlyList<TagDefinition> Tags => _item.Tags;

    /// <summary>The first few tags, for the card chips where space is tight.</summary>
    public IReadOnlyList<TagDefinition> VisibleTags => _item.Tags.Take(MaxVisibleTags).ToList();

    public string? OverflowTagsText =>
        _item.Tags.Count > MaxVisibleTags ? $"+{_item.Tags.Count - MaxVisibleTags}" : null;

    public bool HasOverflowTags => _item.Tags.Count > MaxVisibleTags;

    /// <summary>Sort key for "by Type": the leading tag's name, or null when untagged.</summary>
    public string? PrimaryTagName => _item.Tags.FirstOrDefault()?.Name;

    private const int MaxVisibleTags = 3;

    public bool IsAutoTag(TagDefinition tag) => _item.AutoTagIds.Contains(tag.Id);

    /// <summary>The group this plugin belongs to in the library view's section headers.</summary>
    public string GroupName => IsFavorite
        ? "Favorites"
        : Kind switch
        {
            PluginKind.Instrument => "Instruments",
            PluginKind.Effect => "Effects",
            _ => "Unclassified"
        };

    public void RefreshTags()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(OverflowTagsText));
        OnPropertyChanged(nameof(HasOverflowTags));
        OnPropertyChanged(nameof(PrimaryTagName));
    }

    public void ApplyMetadataOverride(string? name, string? vendor)
    {
        if (name is not null)
        {
            _item.Name = name;
            OnPropertyChanged(nameof(Name));
        }

        if (vendor is not null)
        {
            _item.Vendor = vendor;
            OnPropertyChanged(nameof(Vendor));
            OnPropertyChanged(nameof(VendorDisplay));
        }
    }

    public void RefreshInstallInfo()
    {
        Tag = _item.TagSummary;
        Kind = _item.KindSummary;
        IsFavorite = _item.IsFavoriteSummary;
        IsHidden = _item.IsHiddenSummary;
        CurrentVersion = _item.Installs.Select(i => i.CurrentVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        LatestVersion = _item.Installs.Select(i => i.LatestVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        IsOutdated = VersionComparer.IsNewer(LatestVersion, CurrentVersion);

        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsRemembered));
        OnPropertyChanged(nameof(ActiveInstalls));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(GroupName));

        if (!_item.IsInstalled)
        {
            InstalledFormatsText = null;
            InstalledSummaryText = null;
            return;
        }

        var formats = _item.ActiveInstalls
            .Select(i => i.Format)
            .Distinct()
            .OrderBy(f => f)
            .Select(f => f == PluginFormat.Vst2 ? "VST2" : "VST3");
        InstalledFormatsText = string.Join(" + ", formats);

        InstalledSummaryText = string.IsNullOrWhiteSpace(CurrentVersion)
            ? InstalledFormatsText
            : $"v{CurrentVersion.TrimStart('v', 'V')} · {InstalledFormatsText}";
    }
}
