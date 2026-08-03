using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.App.ViewModels;

/// <summary>
/// Local, uncommitted edit state for the Fix Metadata window. Deliberately separate from
/// PluginDisplayViewModel's own properties: Name drives list sort order, card titles, and
/// search filtering, so live-binding it would visibly reorder/rename the main grid while the
/// user is mid-edit, and closing without saving would leave a half-typed value stuck in the
/// shared vm until the next rescan. Everything here only reaches the real vm on Save.
/// </summary>
public partial class PluginEditFormViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _vendor;

    [ObservableProperty]
    private string? _currentVersion;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private PluginKind _kind;

    /// <summary>Null means "leave Tag alone on Save" — never force a mixed-tag plugin to one tag unless the user explicitly picks a radio.</summary>
    [ObservableProperty]
    private PluginTag? _selectedTag;

    [ObservableProperty]
    private string? _logoUrl;

    /// <summary>
    /// A product-page URL the user pasted (KVR or any plugin database/vendor page). The app
    /// reads name, vendor, version and artwork straight off that page — replacing the old
    /// flow where the user had to hunt down a raw image address by hand.
    /// </summary>
    [ObservableProperty]
    private string? _infoUrl;

    [ObservableProperty]
    private bool _isFetchingInfo;

    [ObservableProperty]
    private string? _logoPreviewLocalPath;

    [ObservableProperty]
    private bool _isLogoPreviewValid;

    /// <summary>
    /// Path to an image the user picked with the "Browse..." file dialog, to be used as the
    /// plugin's artwork on Save. Set instead of LogoUrl when the image comes from disk rather
    /// than a fetched web page — the two are mutually exclusive on Save.
    /// </summary>
    [ObservableProperty]
    private string? _localLogoFilePath;

    [ObservableProperty]
    private bool _isAutoDetecting;

    /// <summary>Busy state for "Search Manually", kept separate from IsAutoDetecting so the two buttons don't disable each other.</summary>
    [ObservableProperty]
    private bool _isSearchingManually;

    /// <summary>Busy state for the Installed-version reload button, again kept independent of the other lookups.</summary>
    [ObservableProperty]
    private bool _isDetectingCurrentVersion;

    [ObservableProperty]
    private string? _autoDetectStatusText;

    [ObservableProperty]
    private string? _logoStatusText;

    public PluginEditFormViewModel(PluginDisplayViewModel vm)
    {
        _name = vm.Name;
        _vendor = vm.Vendor;
        _currentVersion = vm.CurrentVersion;
        _latestVersion = vm.LatestVersion;
        _kind = vm.Kind;
        _selectedTag = vm.Tag switch
        {
            PluginTagSummary.Legit => PluginTag.Legit,
            PluginTagSummary.Cracked => PluginTag.Cracked,
            _ => null
        };
    }
}
