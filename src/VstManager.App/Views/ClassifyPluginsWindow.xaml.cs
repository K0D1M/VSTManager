using System.Collections.ObjectModel;
using System.Windows;
using VstManager.App.ViewModels;

namespace VstManager.App.Views;

public partial class ClassifyPluginsWindow : Window
{
    private readonly MainViewModel _mainViewModel;

    public ObservableCollection<ClassifyPluginViewModel> Plugins { get; }

    public ClassifyPluginsWindow(MainViewModel mainViewModel, IReadOnlyList<PluginDisplayViewModel> plugins)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        Plugins = new ObservableCollection<ClassifyPluginViewModel>(
            plugins.Select(p => new ClassifyPluginViewModel(p)));
        DataContext = this;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in Plugins)
        {
            foreach (var copy in plugin.Copies)
            {
                _mainViewModel.SetCopyTag(plugin.Plugin, copy.Copy, copy.SelectedTag);
            }
        }

        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
