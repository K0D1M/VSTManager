using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Ui.Services;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Details / Files for one plugin. The view model stays dialog-free, so every confirmation and
/// validation message lives here.
/// </summary>
public partial class PluginDetailWindow : Window
{
    private readonly PluginDetailViewModel _vm;

    public PluginDetailWindow(MainViewModel main, PluginCardViewModel plugin)
    {
        InitializeComponent();
        _vm = new PluginDetailViewModel(main, plugin);
        _vm.CloseRequested += (_, _) => Close();
        DataContext = _vm;
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!_vm.Save())
        {
            await ConfirmDialog.ShowMessageAsync(this, "Fix Metadata", "Name can't be empty.");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void ShowInFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string path })
        {
            Reveal.InFolder(path);
        }
    }

    /// <summary>
    /// Excludes just this one install location from future scans (the file stays on disk). The
    /// rescan that follows rebuilds every card, so this window's copy is stale afterwards and
    /// its list cannot refresh in place — it closes on success, leaving a correct list to reopen.
    /// </summary>
    private async void RemoveFromScanning_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string path })
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(this, "Remove from Scanning",
            $"Remove this location from scanning?\n\n{Path.GetFileName(path)}\n\n"
            + "The file stays on disk, but VST Manager will skip it in all future scans on this "
            + "and any other machine running this app.",
            "Remove");

        if (confirmed && await _vm.ExcludePathAsync(path))
        {
            Close();
        }
    }

    private async void MarkAsNotAPlugin_Click(object? sender, RoutedEventArgs e)
    {
        var fileNames = string.Join("\n", _vm.Plugin.ActiveInstalls.Select(i => Path.GetFileName(i.Path)));

        var confirmed = await ConfirmDialog.ShowAsync(this, "Mark as Not a Plugin",
            $"Mark the following as not a plugin?\n\n{fileNames}\n\n"
            + "They will be excluded from all future scans on this and any other machine running this app.",
            "Exclude");

        if (confirmed && await _vm.MarkAsNotAPluginAsync())
        {
            Close();
        }
    }
}
