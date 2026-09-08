using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// One selectable tag in the detail window's picker. Applies immediately on toggle rather than
/// waiting for Save: the rest of that window edits a draft that Save commits, but tags live
/// outside the library file and the user gets clearer feedback watching the chips update as
/// they click.
/// </summary>
public partial class TagPickerItemViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly PluginCardViewModel _card;

    public TagDefinition Tag { get; }

    public string Name => Tag.Name;

    public string ColorHex => Tag.ColorHex;

    /// <summary>True when this tag came from KVR rather than the user.</summary>
    public bool IsAuto { get; }

    public string Hint => IsAuto
        ? $"{Tag.Name} — detected automatically. Turning it off keeps it off."
        : Tag.Name;

    [ObservableProperty]
    private bool _isApplied;

    public TagPickerItemViewModel(MainViewModel main, PluginCardViewModel card, TagDefinition tag)
    {
        _main = main;
        _card = card;
        Tag = tag;
        IsAuto = card.IsAutoTag(tag);
        _isApplied = main.PluginHasTag(card, tag);
    }

    partial void OnIsAppliedChanged(bool value)
    {
        // Only act on a real change of intent; the initial value set in the constructor must
        // never fire this.
        if (value != _main.PluginHasTag(_card, Tag))
        {
            _main.ToggleTag(_card, Tag);
        }
    }
}
