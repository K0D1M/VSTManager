using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.App.ViewModels;

/// <summary>
/// One folder in the tree, with its child folders and the plugins filed directly in it.
///
/// Built fresh each time the tree is rebuilt rather than kept in sync incrementally: the folder
/// list is small (tens of entries, not thousands), and rebuilding is what keeps counts, nesting
/// and ordering from drifting out of step after a move or delete.
/// </summary>
public partial class FolderNodeViewModel : ObservableObject
{
    public FolderNodeViewModel(PluginFolder folder, int depth)
    {
        Folder = folder;
        Depth = depth;
        _isExpanded = true;
    }

    public PluginFolder Folder { get; }

    public string Id => Folder.Id;
    public string Name => Folder.Name;
    public string ColorHex => Folder.ColorHex;

    /// <summary>The chosen emoji, or a generic folder glyph so the row never shows a blank gap.</summary>
    public string DisplayIcon => string.IsNullOrWhiteSpace(Folder.Icon) ? "\U0001F4C1" : Folder.Icon;

    /// <summary>How deep this folder sits, used to indent the row. Root folders are 0.</summary>
    public int Depth { get; }

    /// <summary>Left indent in pixels, so nesting reads visually without a full TreeView.</summary>
    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    public ObservableCollection<FolderNodeViewModel> Children { get; } = new();

    /// <summary>Plugins filed directly in this folder — not those in its sub-folders.</summary>
    public ObservableCollection<PluginDisplayViewModel> Plugins { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Highlights the row while a drag hovers over it as a drop target.</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>
    /// Plugins here plus everywhere beneath. A folder that only contains sub-folders would
    /// otherwise read as empty, which is misleading when its children hold plenty.
    /// </summary>
    public int TotalCount => Plugins.Count + Children.Sum(c => c.TotalCount);

    /// <summary>
    /// Whether this folder has anything to show once filters are applied. An empty folder is
    /// still rendered (it is a place the user made, and a drop target), but this drives the
    /// "no plugins in here yet" hint.
    /// </summary>
    public bool IsEmpty => TotalCount == 0;

    /// <summary>Every node in this subtree, this one first — used to flatten for display.</summary>
    public IEnumerable<FolderNodeViewModel> SelfAndDescendants()
    {
        yield return this;

        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }

    /// <summary>Signals the count-derived properties after the tree is populated.</summary>
    public void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
