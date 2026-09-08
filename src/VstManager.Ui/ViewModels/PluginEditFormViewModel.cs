using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// Local, uncommitted edit state for the detail window. Deliberately separate from the card's
/// own properties: Name drives sort order, card titles and search, so live-binding it would
/// visibly reorder and rename the library while the user is mid-edit, and closing without
/// saving would leave a half-typed value stuck in the shared card. Everything here only reaches
/// the real plugin on Save.
/// </summary>
public partial class PluginEditFormViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _vendor;

    [ObservableProperty]
    private string? _currentVersion;

    /// <summary>Detected online only; shown but not hand-editable.</summary>
    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private PluginKind _kind;

    /// <summary>
    /// Null means "leave the tag alone on Save" — a mixed-tag plugin is never forced to one
    /// value unless the user explicitly picks a radio.
    /// </summary>
    [ObservableProperty]
    private PluginTag? _selectedTag;

    [ObservableProperty]
    private bool _isDetectingCurrentVersion;

    [ObservableProperty]
    private string? _statusText;

    public PluginEditFormViewModel(PluginCardViewModel card)
    {
        _name = card.Name;
        _vendor = card.Vendor;
        _currentVersion = card.CurrentVersion;
        _latestVersion = card.LatestVersion;
        _kind = card.Kind;
        _selectedTag = card.Tag switch
        {
            PluginTagSummary.Legit => PluginTag.Legit,
            PluginTagSummary.Cracked => PluginTag.Cracked,
            _ => null
        };
    }

    public bool HasLatestVersion => !string.IsNullOrWhiteSpace(LatestVersion);

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    // Settable bools so the segment RadioButtons can bind IsChecked two-way, the same pattern
    // the main window's layout and mode toggles use. Only the true case acts; the false case
    // is just the other radio in the group becoming true.
    public bool IsInstrument
    {
        get => Kind == PluginKind.Instrument;
        set { if (value) { Kind = PluginKind.Instrument; } }
    }

    public bool IsEffect
    {
        get => Kind == PluginKind.Effect;
        set { if (value) { Kind = PluginKind.Effect; } }
    }

    public bool IsLegit
    {
        get => SelectedTag == PluginTag.Legit;
        set { if (value) { SelectedTag = PluginTag.Legit; } }
    }

    public bool IsCracked
    {
        get => SelectedTag == PluginTag.Cracked;
        set { if (value) { SelectedTag = PluginTag.Cracked; } }
    }

    partial void OnKindChanged(PluginKind value)
    {
        OnPropertyChanged(nameof(IsInstrument));
        OnPropertyChanged(nameof(IsEffect));
    }

    partial void OnSelectedTagChanged(PluginTag? value)
    {
        OnPropertyChanged(nameof(IsLegit));
        OnPropertyChanged(nameof(IsCracked));
    }

    partial void OnLatestVersionChanged(string? value) => OnPropertyChanged(nameof(HasLatestVersion));

    partial void OnStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasStatus));
}
