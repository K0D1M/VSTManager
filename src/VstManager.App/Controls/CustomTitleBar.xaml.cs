using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace VstManager.App.Controls;

public partial class CustomTitleBar : UserControl
{
    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(CustomTitleBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleIconProperty = DependencyProperty.Register(
        nameof(TitleIcon), typeof(ImageSource), typeof(CustomTitleBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowMaximizeProperty = DependencyProperty.Register(
        nameof(ShowMaximize), typeof(bool), typeof(CustomTitleBar), new PropertyMetadata(false, OnShowMaximizeChanged));

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public ImageSource? TitleIcon
    {
        get => (ImageSource?)GetValue(TitleIconProperty);
        set => SetValue(TitleIconProperty, value);
    }

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public CustomTitleBar()
    {
        InitializeComponent();
        UpdateMaximizeVisibility();
    }

    private static void OnShowMaximizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CustomTitleBar)d).UpdateMaximizeVisibility();
    }

    private void UpdateMaximizeVisibility()
    {
        MaximizeButton.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null)
        {
            return;
        }

        if (e.ClickCount == 2 && ShowMaximize)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            SystemCommands.MinimizeWindow(window);
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            SystemCommands.CloseWindow(window);
        }
    }
}
