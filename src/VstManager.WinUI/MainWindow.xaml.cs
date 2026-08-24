using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VstManager.WinUI.Views;

namespace VstManager.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = "VST Manager — WinUI";

        // Mica: the window tints with the desktop wallpaper behind it. This is the single biggest
        // visual difference from the WPF head, whose background is a painted gradient.
        SystemBackdrop = new MicaBackdrop();

        // Native caption behaviour (snap layouts, correct maximise, rounded corners) instead of a
        // hand-built title bar.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));

        ContentFrame.Navigate(typeof(LibraryPage), null, new EntranceNavigationTransitionInfo());
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var page = item.Tag as string == "settings" ? typeof(SettingsPage) : typeof(LibraryPage);

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new DrillInNavigationTransitionInfo());
        }
    }
}
