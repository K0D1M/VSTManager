using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VstManager.WinUI.ViewModels;

namespace VstManager.WinUI.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; } = new();

    public LibraryPage()
    {
        InitializeComponent();
        GroupedPlugins.Source = ViewModel.Groups;
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void PluginGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PluginDisplayViewModel plugin)
        {
            return;
        }

        var dialog = new PluginDetailDialog(plugin) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PluginDisplayViewModel plugin })
        {
            ViewModel.ToggleFavoriteCommand.Execute(plugin);
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<LibraryFilter>(tag, out var filter))
        {
            ViewModel.Filter = filter;
        }
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<LibrarySort>(tag, out var sort))
        {
            ViewModel.Sort = sort;
        }
    }
}
