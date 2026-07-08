using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.App.ViewModels;

public partial class PluginDisplayViewModel : ObservableObject
{
    private readonly PluginDisplayItem _item;

    public PluginDisplayViewModel(PluginDisplayItem item)
    {
        _item = item;
    }

    public string Name => _item.Name;
    public string? Vendor => _item.Vendor;
    public bool IsInstalled => _item.IsInstalled;
    public PluginFormat? Format => _item.Format;
    public string? Path => _item.Installed?.Path;

    [ObservableProperty]
    private string? _logoPath;

    [ObservableProperty]
    private PluginTag _tag;

    public PluginInfo? Installed => _item.Installed;
    public CatalogEntry? Catalog => _item.Catalog;

    public void SyncTagFrom(PluginDisplayItem item)
    {
        Tag = item.Tag;
    }
}
