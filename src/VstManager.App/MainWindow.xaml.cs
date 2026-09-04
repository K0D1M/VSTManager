using System.Collections;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.App.ViewModels;
using VstManager.App.Views;
using VstManager.Core.Services;

namespace VstManager.App;

public partial class MainWindow : Window
{
    private PluginDisplayViewModel? _lastClickedForRange;
    private readonly TrayIconService _trayIconService = new(AppIdentityService.IconPath);
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
        WindowCorners.Apply(this);
        WindowIcon.ApplyDefault(this);

        if (!File.Exists(LibraryStore.GetDefaultPath()))
        {
            new WelcomeWindow().ShowDialog();
        }

        var vm = new MainViewModel();
        vm.FixMetadataRequested += (_, plugin) => OpenDetailWindow(vm, plugin);
        vm.NewMultiCopyPluginsFound += (_, plugins) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                var classifyWindow = new ClassifyPluginsWindow(vm, plugins) { Owner = this };
                classifyWindow.ShowDialog();
            });
        };
        DataContext = vm;

        // Set before the StartupUri machinery calls Show(), so the window never flashes at
        // full size before collapsing to the taskbar.
        if (vm.StartMinimized)
        {
            WindowState = WindowState.Minimized;
        }

        _trayIconService.OpenRequested += (_, _) => RestoreFromTray();
        _trayIconService.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            Close();
        };
        Closing += MainWindow_Closing;
        Closed += (_, _) => _trayIconService.Dispose();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting || DataContext is not MainViewModel { MinimizeToTray: true })
        {
            return;
        }

        // Hide instead of closing: the app keeps scanning and firing notifications in the
        // background, and the tray icon's "Open"/"Exit" are the only way back — closing for
        // real happens only via the tray's Exit, which sets _isExiting first.
        e.Cancel = true;
        Hide();
        _trayIconService.Show();
    }

    private void RestoreFromTray()
    {
        _trayIconService.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The window is freely resizable in both dimensions; just keep it inside the monitor's
        // work area on open (and grow it for the current zoom — see WindowSizing.FitToScreen).
        WindowSizing.FitToScreen(this);
    }

    private void OpenDetailWindow(MainViewModel vm, PluginDisplayViewModel plugin)
    {
        var detailWindow = new PluginDetailWindow(vm, plugin) { Owner = this };
        detailWindow.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(vm) { Owner = this };
        settingsWindow.ShowDialog();
    }

    /// <summary>
    /// A row in the startup "updates available" panel. No multi-select/range-click here — unlike
    /// the main grid, these rows exist only to jump straight to the plugin's detail window.
    /// </summary>
    private void StartupOutdatedRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { DataContext: PluginDisplayViewModel plugin })
        {
            return;
        }

        OpenDetailWindow(vm, plugin);
    }

    private void PluginCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { DataContext: PluginDisplayViewModel plugin } element)
        {
            return;
        }

        // Let inner interactive controls (e.g. the favorite star button) handle their own
        // click instead of also opening the detail window or toggling selection.
        if (e.OriginalSource is DependencyObject originalSource && IsWithinButton(originalSource, element))
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (shift && _lastClickedForRange is not null)
        {
            // Ctrl+Shift extends; plain Shift replaces, so an overshot range can be pulled back.
            SelectRange(element, _lastClickedForRange, plugin, vm, additive: ctrl);
            e.Handled = true;
            return;
        }

        if (ctrl || vm.IsSelectionMode)
        {
            vm.SetSelected(plugin, !plugin.IsSelected);
            _lastClickedForRange = plugin;
            e.Handled = true;
            return;
        }

        OpenDetailWindow(vm, plugin);
    }

    /// <summary>
    /// Selects the range between the anchor and the clicked card, replacing the current
    /// selection rather than adding to it — the Explorer behaviour people expect, and the only
    /// way to *shrink* a range once you've overshot it. Ctrl+Shift+click extends instead, for
    /// building a selection out of several ranges.
    /// </summary>
    private static void SelectRange(
        FrameworkElement clickedElement,
        PluginDisplayViewModel anchor,
        PluginDisplayViewModel target,
        MainViewModel vm,
        bool additive)
    {
        var itemsControl = FindAncestorItemsControl(clickedElement);
        var items = (itemsControl?.ItemsSource as IEnumerable)?.OfType<PluginDisplayViewModel>().ToList();

        var anchorIndex = items?.IndexOf(anchor) ?? -1;
        var targetIndex = items?.IndexOf(target) ?? -1;

        // The anchor can be in a different section (or gone after a rescan), in which case
        // there's no meaningful range — fall back to selecting just what was clicked.
        if (items is null || anchorIndex < 0 || targetIndex < 0)
        {
            vm.SetSelected(target, true);
            return;
        }

        var (start, end) = anchorIndex <= targetIndex ? (anchorIndex, targetIndex) : (targetIndex, anchorIndex);
        vm.SelectRange(items.Skip(start).Take(end - start + 1).ToList(), additive);
    }

    /// <summary>
    /// Escape leaves selection mode, Ctrl+A selects everything currently visible. Handled at the
    /// window rather than per-card so they work wherever focus happens to be — except while
    /// typing in the search box, where both keys mean what they normally mean in a text field.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (e.Key == Key.Escape && (vm.IsSelectionMode || vm.SelectedCount > 0))
        {
            vm.ExitSelectionModeCommand.Execute(null);
            _lastClickedForRange = null;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            vm.SelectAllVisibleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl +/-/0 zoom, the familiar browser/VS Code binding. OemPlus/OemMinus are the main
        // keys; Add/Subtract/NumPad0 cover the numpad. Ctrl+0 resets to 100%.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    vm.ZoomInCommand.Execute(null);
                    break;
                case Key.OemMinus or Key.Subtract:
                    vm.ZoomOutCommand.Execute(null);
                    break;
                case Key.D0 or Key.NumPad0:
                    vm.ZoomResetCommand.Execute(null);
                    break;
                default:
                    return;
            }

            // Flash the current zoom, since the keyboard shortcut has no other feedback. Read the
            // value back so it reflects clamping at the 80%/150% ends.
            ShowZoomIndicator(vm.UiScalePercent);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl + wheel zooms, matching the keyboard shortcut and browsers. This fires while the
    /// event tunnels down from the window, before SmoothScroll's ScrollViewer handler, so marking
    /// it handled here both zooms and stops the list from scrolling at the same time.
    /// </summary>
    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm
            || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || e.Delta == 0)
        {
            return;
        }

        if (e.Delta > 0)
        {
            vm.ZoomInCommand.Execute(null);
        }
        else
        {
            vm.ZoomOutCommand.Execute(null);
        }

        ShowZoomIndicator(vm.UiScalePercent);
        e.Handled = true;
    }

    private Storyboard? _zoomIndicatorAnimation;

    /// <summary>
    /// Briefly shows the centred zoom readout, then fades it out. Restarts on each call so rapid
    /// presses keep it on screen with an up-to-date number.
    /// </summary>
    private void ShowZoomIndicator(int percent)
    {
        ZoomIndicatorText.Text = $"{percent}%";

        _zoomIndicatorAnimation ??= BuildZoomIndicatorAnimation();
        _zoomIndicatorAnimation.Begin();
    }

    private Storyboard BuildZoomIndicatorAnimation()
    {
        // Quick fade in, hold ~0.85s, fade out. FillBehavior defaults to HoldEnd, so it settles
        // back to fully hidden.
        var fade = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1350)))
            }
        };

        Storyboard.SetTarget(fade, ZoomIndicator);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        return storyboard;
    }

    private static bool IsWithinButton(DependencyObject source, DependencyObject boundary)
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, boundary))
        {
            if (current is Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static ItemsControl? FindAncestorItemsControl(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is ItemsControl itemsControl)
            {
                return itemsControl;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
