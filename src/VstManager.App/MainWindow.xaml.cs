using System.Collections;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.App.ViewModels;
using VstManager.App.Views;
using VstManager.Core.Services;

namespace VstManager.App;

public partial class MainWindow : Window
{
    private PluginDisplayViewModel? _lastClickedForRange;
    private readonly TrayIconService _trayIconService = new(
        Path.Combine(AppContext.BaseDirectory, "a_clean_modern_app_icon_logo_design_on_a_dark_b.ico"));
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
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

    /// <summary>Natural width that fits the toolbar on one row; the locked restored width.</summary>
    private double _lockedWidth;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Lock the restored width to exactly where the toolbar's buttons end: measure the
        // toolbar unconstrained to get its natural single-row width, then add back the root
        // Grid's margins and the window chrome.
        if (Content is not FrameworkElement root)
        {
            return;
        }

        ToolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var nonContentWidth = ActualWidth - root.ActualWidth;
        var target = Math.Ceiling(ToolbarPanel.DesiredSize.Width + nonContentWidth);

        // Never demand more width than the monitor actually has — otherwise the window opens
        // partly offscreen and can't be dragged back to a usable size.
        var work = WindowSizing.GetWorkArea(this);
        _lockedWidth = Math.Min(target, work.Width - 16);
        Width = _lockedWidth;

        // The width lock is enforced by intercepting the live drag-resize message rather than
        // by setting MaxWidth. MaxWidth also constrains the *maximized* window, and clearing
        // it from OnStateChanged is too late — Win32 has already committed the size by then,
        // which left "maximize" stuck at the toolbar width. Blocking the drag instead leaves
        // maximizing completely unconstrained.
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }

        WindowSizing.FitToScreen(this);
    }

    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszTopLeft = 4;
    private const int WmszBottomLeft = 7;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Only interferes with interactive edge-dragging; maximize/restore/minimize are
        // untouched because Windows doesn't send WM_SIZING for them.
        if (msg != WmSizing || _lockedWidth <= 0 || WindowState != WindowState.Normal)
        {
            return IntPtr.Zero;
        }

        var rect = Marshal.PtrToStructure<Win32Rect>(lParam);
        var scaleX = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var lockedPixels = (int)Math.Round(_lockedWidth * scaleX);

        // Hold the edge *opposite* the one being dragged still. Always pinning Right would
        // make a left-edge drag slide the whole window sideways instead of doing nothing.
        var edge = wParam.ToInt32();
        var draggingLeftEdge = edge is WmszLeft or WmszTopLeft or WmszBottomLeft;

        if (draggingLeftEdge)
        {
            rect.Left = rect.Right - lockedPixels;
        }
        else
        {
            rect.Right = rect.Left + lockedPixels;
        }

        Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);

        handled = true;
        return new IntPtr(1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        }
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
