using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.App.ViewModels;

public partial class ClassifyPluginViewModel : ObservableObject
{
    public PluginDisplayViewModel Plugin { get; }
    public IReadOnlyList<ClassifyCopyViewModel> Copies { get; }

    public ClassifyPluginViewModel(PluginDisplayViewModel plugin)
    {
        Plugin = plugin;
        Copies = plugin.Installs.Select(i => new ClassifyCopyViewModel(i)).ToList();
    }

    public string Name => Plugin.Name;
    public string? Vendor => Plugin.Vendor;
}

public partial class ClassifyCopyViewModel : ObservableObject
{
    public PluginInfo Copy { get; }

    public ClassifyCopyViewModel(PluginInfo copy)
    {
        Copy = copy;
        _isLegit = copy.Tag == PluginTag.Legit;
        _isCracked = copy.Tag == PluginTag.Cracked;
    }

    public string FormatLabel => Copy.Format == PluginFormat.Vst2 ? "VST2" : "VST3";
    public string Path => Copy.Path;

    [ObservableProperty]
    private bool _isLegit;

    [ObservableProperty]
    private bool _isCracked;

    partial void OnIsLegitChanged(bool value)
    {
        if (value)
        {
            IsCracked = false;
        }
    }

    partial void OnIsCrackedChanged(bool value)
    {
        if (value)
        {
            IsLegit = false;
        }
    }

    public PluginTag SelectedTag => IsLegit ? PluginTag.Legit
        : IsCracked ? PluginTag.Cracked
        : PluginTag.Unclassified;
}
