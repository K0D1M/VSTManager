using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.App.ViewModels;

/// <summary>
/// One selectable tag in the detail window's picker. Applies immediately on toggle rather than
/// waiting for Save: the rest of that window edits a draft that Save commits, but tags are
/// stored outside the library file and the user gets clearer feedback seeing chips update as
/// they click.
/// </summary>
public partial class TagPickerItemViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly PluginDisplayViewModel _plugin;

    public TagDefinition Tag { get; }

    /// <summary>True when this tag came from KVR rather than the user.</summary>
    public bool IsAuto { get; }

    public string Hint => IsAuto
        ? $"{Tag.Name} — detected automatically. Turning it off keeps it off."
        : Tag.Name;

    [ObservableProperty]
    private bool _isApplied;

    public TagPickerItemViewModel(MainViewModel mainViewModel, PluginDisplayViewModel plugin, TagDefinition tag)
    {
        _mainViewModel = mainViewModel;
        _plugin = plugin;
        Tag = tag;
        IsAuto = plugin.IsAutoTag(tag);
        _isApplied = mainViewModel.PluginHasTag(plugin, tag);
    }

    partial void OnIsAppliedChanged(bool value)
    {
        // Toggling routes through the same command the context menu uses, so a change made here
        // behaves identically to one made there — including applying to a whole selection.
        if (value != _mainViewModel.PluginHasTag(_plugin, Tag))
        {
            _mainViewModel.ToggleTagCommand.Execute(new TagCommandArgs(_plugin, Tag));
        }
    }
}
