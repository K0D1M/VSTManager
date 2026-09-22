using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VstManager.Ui.ViewModels;

/// <summary>One collapsible group in the library — a category, or the trailing Unclassified bucket
/// (<see cref="CategoryId"/> null). Instances are reused across filter passes so the expanded
/// state survives re-filtering.</summary>
public partial class CategorySectionViewModel : ObservableObject
{
    public CategorySectionViewModel(string? categoryId, string icon, string title)
    {
        CategoryId = categoryId;
        _icon = icon;
        _title = title;
    }

    public string? CategoryId { get; }

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isExpanded = true;

    public bool HasItems => Count > 0;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasItems));

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = new();
}
