using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Offered after a scan finds newly-installed plugins that have several copies on disk, so each
/// copy can be marked Legit or Cracked individually rather than the plugin as a whole.
/// </summary>
public partial class ClassifyPluginsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IReadOnlyList<ClassifyPluginViewModel> _plugins;

    public ClassifyPluginsWindow()
    {
        InitializeComponent();
        _main = null!;
        _plugins = [];
    }

    public ClassifyPluginsWindow(MainViewModel main, IReadOnlyList<PluginCardViewModel> plugins) : this()
    {
        _main = main;
        _plugins = plugins.Select(p => new ClassifyPluginViewModel(p)).ToList();
        PluginList.ItemsSource = _plugins;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var plugin in _plugins)
        {
            foreach (var copy in plugin.Copies)
            {
                _main.SetCopyTag(plugin.Plugin, copy.Copy, copy.SelectedTag);
            }
        }

        Close();
    }

    private void Later_Click(object? sender, RoutedEventArgs e) => Close();
}
