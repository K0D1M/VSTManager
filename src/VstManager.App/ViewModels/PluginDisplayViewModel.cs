using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

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
    public IReadOnlyList<PluginInfo> Installs => _item.Installs;
    public PluginInfo? Installed => _item.Installs.FirstOrDefault();
    public CatalogEntry? Catalog => _item.Catalog;

    public PluginFormat? Format => Installed?.Format;

    public string? Path => _item.Installs.Count == 0
        ? null
        : string.Join(Environment.NewLine, _item.Installs.Select(i => i.Path));

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

        if (_item.Installs.Count == 0)
        {
            InstalledFormatsText = null;
            InstalledSummaryText = null;
            return;
        }

        var formats = _item.Installs
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
