using System.Collections.ObjectModel;
using System.Windows;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.App.ViewModels;

namespace VstManager.App.Views;

public partial class ClassifyPluginsWindow : Window
{
    private readonly MainViewModel _mainViewModel;

    public ObservableCollection<ClassifyPluginViewModel> Plugins { get; }

    public ClassifyPluginsWindow(MainViewModel mainViewModel, IReadOnlyList<PluginDisplayViewModel> plugins)
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
        WindowCorners.Apply(this);
        WindowIcon.ApplyDefault(this);
        _mainViewModel = mainViewModel;
        Plugins = new ObservableCollection<ClassifyPluginViewModel>(
            plugins.Select(p => new ClassifyPluginViewModel(p)));
        DataContext = this;

        // Grow the window for the current zoom and keep it on-screen. This window has no Loaded
        // handler in XAML, so subscribe here rather than adding one.
        Loaded += (_, _) => WindowSizing.FitToScreen(this);
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
