using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.App.ViewModels;

public partial class PluginDisplayViewModel : ObservableObject
{
    private readonly PluginDisplayItem _item;

    public PluginDisplayViewModel(PluginDisplayItem item)
    {
        _item = item;
        RefreshInstallInfo();
    }

    public string Name => _item.Name;
    public string? Vendor => _item.Vendor;
    public string BaseName => _item.BaseName;
    public bool IsInstalled => _item.IsInstalled;

    /// <summary>True when this plugin was uninstalled but its details are still remembered.</summary>
    public bool IsRemembered => _item.IsRemembered;

    public IReadOnlyList<PluginInfo> Installs => _item.Installs;

    /// <summary>Only the copies actually on disk — used anywhere a real file is needed.</summary>
    public IReadOnlyList<PluginInfo> ActiveInstalls => _item.ActiveInstalls.ToList();

    /// <summary>
    /// Prefers a copy that exists on disk (this feeds Show in Folder and version detection),
    /// falling back to a remembered one so the detail window can still show last-known info.
    /// </summary>
    public PluginInfo? Installed => _item.ActiveInstalls.FirstOrDefault() ?? _item.Installs.FirstOrDefault();
    public CatalogEntry? Catalog => _item.Catalog;

    public PluginFormat? Format => Installed?.Format;

    /// <summary>
    /// Paths of the copies present on disk. Remembered copies are excluded so that search
    /// and the uninstall dialog never surface a file that no longer exists.
    /// </summary>
    public string? Path => !_item.IsInstalled
        ? null
        : string.Join(Environment.NewLine, _item.ActiveInstalls.Select(i => i.Path));

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

    /// <summary>
    /// True if discovered by the most recent scan (not persisted — resets to false once a
    /// subsequent scan runs and this plugin is already in the stored library).
    /// </summary>
    [ObservableProperty]
    private bool _isNew;

    /// <summary>True when a known LatestVersion is strictly newer than the installed CurrentVersion.</summary>
    [ObservableProperty]
    private bool _isOutdated;

    public bool HasFormat(PluginFormat format) => _item.Installs.Any(i => i.Format == format);

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

        // Format/summary text describes what's on disk, so it comes from the active copies
        // only — unlike the tag/kind/version values above, which intentionally still reflect
        // remembered copies.
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
