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
    /// Staged category id — separate from Kind, matching PluginInfo.CategoryId's own relationship
    /// to Kind (additive, not a replacement). Null means "not yet assigned"; picking one of the
    /// Instrument/Effect presets here also flips Kind, so the existing Type segment above and this
    /// stay in agreement rather than becoming two independent, contradictable controls.
    /// </summary>
    [ObservableProperty]
    private string? _selectedCategoryId;

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

    // ---- online lookup / artwork staging -----------------------------------
    // Each long-running action gets its own busy flag rather than sharing one: they can be
    // started independently, and a single flag would grey out the other buttons for no reason.

    /// <summary>Artwork URL, either typed or filled in by a lookup. Cleared when a local file is chosen.</summary>
    [ObservableProperty]
    private string? _logoUrl;

    /// <summary>Product-page URL for "Get Info".</summary>
    [ObservableProperty]
    private string? _infoUrl;

    /// <summary>Set when the user picked an image off disk; mutually exclusive with <see cref="LogoUrl"/>.</summary>
    [ObservableProperty]
    private string? _localLogoFilePath;

    /// <summary>Temp file backing the preview image, deleted when the window closes.</summary>
    [ObservableProperty]
    private string? _logoPreviewLocalPath;

    /// <summary>Whether the preview actually decoded — Save only commits artwork that displayed.</summary>
    [ObservableProperty]
    private bool _isLogoPreviewValid;

    [ObservableProperty]
    private bool _isAutoDetecting;

    [ObservableProperty]
    private bool _isSearchingManually;

    [ObservableProperty]
    private bool _isFetchingInfo;

    [ObservableProperty]
    private bool _isRefreshingMetadata;

    /// <summary>Result line for Auto-Detect / Search Manually, shown on the Details tab.</summary>
    [ObservableProperty]
    private string? _autoDetectStatusText;

    /// <summary>Result line for the artwork and URL actions, shown on the Web Info tab.</summary>
    [ObservableProperty]
    private string? _logoStatusText;

    public PluginEditFormViewModel(PluginCardViewModel card)
    {
        _name = card.Name;
        _vendor = card.Vendor;
        _currentVersion = card.CurrentVersion;
        _latestVersion = card.LatestVersion;
        _kind = card.Kind;
        _selectedCategoryId = card.CategoryId;
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

    public bool HasAutoDetectStatus => !string.IsNullOrWhiteSpace(AutoDetectStatusText);

    public bool HasLogoStatus => !string.IsNullOrWhiteSpace(LogoStatusText);

    /// <summary>True while any lookup is running, so Save can be held back mid-flight.</summary>
    public bool IsBusy => IsAutoDetecting || IsSearchingManually || IsFetchingInfo
                          || IsRefreshingMetadata || IsDetectingCurrentVersion;

    partial void OnAutoDetectStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasAutoDetectStatus));

    partial void OnLogoStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasLogoStatus));

    /// <summary>Auto-Detect or Search Manually in flight — they share one spinner.</summary>
    public bool IsLookingUp => IsAutoDetecting || IsSearchingManually;

    partial void OnIsAutoDetectingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsLookingUp));
    }

    partial void OnIsSearchingManuallyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsLookingUp));
    }

    partial void OnIsFetchingInfoChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsRefreshingMetadataChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsDetectingCurrentVersionChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    /// <summary>
    /// Re-reads the versions from the card after something wrote them directly rather than through
    /// the form — currently only "Refresh Metadata", which persists immediately. Without this the
    /// stale staged values would be written straight back over the fresh ones on Save.
    /// </summary>
    public void SyncVersionsFrom(PluginCardViewModel card)
    {
        CurrentVersion = card.CurrentVersion;
        LatestVersion = card.LatestVersion;
    }
}
