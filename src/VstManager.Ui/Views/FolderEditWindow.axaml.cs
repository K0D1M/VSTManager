using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Core.Models;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Name, icon and colour for one folder. A small modal rather than inline editing: creating a
/// folder and personalising it is a single deliberate act, and a dialog can show a live preview
/// of the resulting row.
/// </summary>
public partial class FolderEditWindow : Window
{
    private readonly FolderEditViewModel _vm;

    public FolderEditWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public FolderEditWindow(PluginFolder folder) : this()
    {
        _vm = new FolderEditViewModel(folder);
        DataContext = _vm;
        Title = _vm.IsNew ? "New Folder" : $"Edit — {folder.Name}";

        Opened += (_, _) => NameBox.Focus();
    }

    /// <summary>
    /// Shows the dialog and resolves to the edited (Name, Icon, ColorHex), or null if cancelled —
    /// the caller applies the result onto the real PluginFolder itself.
    /// </summary>
    public static async Task<(string Name, string Icon, string ColorHex)?> EditAsync(Window owner, PluginFolder folder)
    {
        var dialog = new FolderEditWindow(folder);
        return await dialog.ShowDialog<(string, string, string)?>(owner);
    }

    private void IconChoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string icon })
        {
            _vm.Icon = icon;
        }
    }

    private void ColorChoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string hex })
        {
            _vm.ColorHex = hex;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.TryBuildResult(out var name, out var icon, out var colorHex))
        {
            Close((name, icon, colorHex));
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
