using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>One newly-found plugin awaiting classification — a category (mandatory), and, if it
/// has several installed copies, a per-copy Legit/Cracked call (still optional).</summary>
public partial class ClassifyPluginViewModel : ObservableObject
{
    public PluginCardViewModel Plugin { get; }

    public IReadOnlyList<ClassifyCopyViewModel> Copies { get; }

    public ClassifyPluginViewModel(PluginCardViewModel plugin)
    {
        Plugin = plugin;
        // A bundle lists only its newly found plugins' files — not every file of all 38 SWAM
        // instruments — while the category picked here still applies to the whole bundle.
        Copies = (plugin is BundleCardViewModel bundle
                ? bundle.Members.Where(m => m.NeedsCategoryReview).SelectMany(m => m.Installs)
                : plugin.Installs)
            .Select(i => new ClassifyCopyViewModel(i))
            .ToList();
        _selectedCategoryId = plugin.CategoryId;
        Name = plugin is BundleCardViewModel b
            ? $"{plugin.Name} — {b.Members.Count(m => m.NeedsCategoryReview)} new"
            : plugin.Name;

        foreach (var copy in Copies)
        {
            copy.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsComplete));
        }
    }

    /// <summary>
    /// Everything answered: a category, and (with license status) every copy marked Legit or
    /// Cracked. The window saves a complete plugin and lets it leave the list on its own.
    /// </summary>
#if NO_LICENSE_STATUS
    public bool IsComplete => HasCategorySelected;
#else
    public bool IsComplete => HasCategorySelected && Copies.All(c => c.SelectedTag != PluginTag.Unclassified);
#endif

    public string Name { get; }

    public string? Vendor => Plugin.Vendor;

    /// <summary>Staged category id — null until the user picks one. Save is refused while any
    /// plugin in the batch still has this null, matching the "mandatory" part of the feature.</summary>
    [ObservableProperty]
    private string? _selectedCategoryId;

    public bool HasCategorySelected => SelectedCategoryId is not null;

    partial void OnSelectedCategoryIdChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCategorySelected));
        OnPropertyChanged(nameof(IsComplete));
    }
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
#if !NO_LICENSE_STATUS
        _isLegit = copy.Tag == PluginTag.Legit;
        _isCracked = copy.Tag == PluginTag.Cracked;
#endif
    }

    public string FormatLabel => Copy.Format.Label();

    public string Path => Copy.Path;

#if !NO_LICENSE_STATUS
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
#endif
}
