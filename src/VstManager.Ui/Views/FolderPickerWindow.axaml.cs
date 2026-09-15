using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Picks a folder to file a plugin into — "Unfiled" first, then every folder by path. A plain
/// list in its own window rather than the "Move to Folder" context-menu submenu that idea started
/// as: Avalonia's MenuItem submenus (ItemsSource-driven, nested inside a MenuFlyout) did not open
/// on hover here, confirmed by actually running the app — mouse hover with a verified-focused
/// window, and keyboard Right-arrow on a genuinely focused item, both left the submenu closed;
/// even a hardcoded static child MenuItem with no bindings at all did the same. Rather than keep
/// fighting that, this reuses the picker-window mechanism already proven in this codebase
/// (MatchPickerWindow).
/// </summary>
public partial class FolderPickerWindow : Window
{
    /// <summary>Wraps the choice so a null FolderId ("Unfiled") is distinguishable from no choice at all.</summary>
    public sealed record PickResult(string? FolderId);

    public FolderPickerWindow()
    {
        InitializeComponent();
    }

    public FolderPickerWindow(IReadOnlyList<FolderMenuEntry> entries) : this()
    {
        EntryList.ItemsSource = entries;
    }

    /// <summary>Shows the picker and resolves to the chosen folder, or null if the user cancelled.</summary>
    public static async Task<PickResult?> PickAsync(Window owner, IReadOnlyList<FolderMenuEntry> entries)
    {
        var dialog = new FolderPickerWindow(entries);
        return await dialog.ShowDialog<PickResult?>(owner);
    }

    private void Entry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: FolderMenuEntry entry })
        {
            Close(new PickResult(entry.FolderId));
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
