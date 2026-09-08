using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>One newly-found plugin that has several installed copies awaiting classification.</summary>
public partial class ClassifyPluginViewModel : ObservableObject
{
    public PluginCardViewModel Plugin { get; }

    public IReadOnlyList<ClassifyCopyViewModel> Copies { get; }

    public ClassifyPluginViewModel(PluginCardViewModel plugin)
    {
        Plugin = plugin;
        Copies = plugin.Installs.Select(i => new ClassifyCopyViewModel(i)).ToList();
    }

    public string Name => Plugin.Name;

    public string? Vendor => Plugin.Vendor;
}

/// <summary>
/// One installed copy, and which side the user says it is. Legit and Cracked are tracked as two
/// bools rather than a single enum so each can be turned back off — leaving the copy
/// unclassified, which is a legitimate answer and the default.
/// </summary>
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

    // Mutually exclusive, but not a RadioButton group: turning the active one off again has to
    // leave the copy unclassified, which a radio group cannot express.
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
