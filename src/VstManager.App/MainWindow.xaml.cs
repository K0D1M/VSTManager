using System.Collections;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VstManager.App.ViewModels;
using VstManager.App.Views;
using VstManager.Core.Services;

namespace VstManager.App;

public partial class MainWindow : Window
{
    private PluginDisplayViewModel? _lastClickedForRange;

    public MainWindow()
    {
        InitializeComponent();

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
            SelectRange(element, _lastClickedForRange, plugin, vm);
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            vm.SetSelected(plugin, !plugin.IsSelected);
            _lastClickedForRange = plugin;
            e.Handled = true;
            return;
        }

        if (vm.IsSelectionMode)
        {
            vm.SetSelected(plugin, !plugin.IsSelected);
            _lastClickedForRange = plugin;
            e.Handled = true;
            return;
        }

        OpenDetailWindow(vm, plugin);
    }

    private static void SelectRange(FrameworkElement clickedElement, PluginDisplayViewModel anchor, PluginDisplayViewModel target, MainViewModel vm)
    {
        var itemsControl = FindAncestorItemsControl(clickedElement);
        var items = (itemsControl?.ItemsSource as IEnumerable)?.OfType<PluginDisplayViewModel>().ToList();

        if (items is null)
        {
            vm.SetSelected(target, true);
            return;
        }

        var anchorIndex = items.IndexOf(anchor);
        var targetIndex = items.IndexOf(target);
        if (anchorIndex < 0 || targetIndex < 0)
        {
            vm.SetSelected(target, true);
            return;
        }

        var (start, end) = anchorIndex <= targetIndex ? (anchorIndex, targetIndex) : (targetIndex, anchorIndex);
        for (var i = start; i <= end; i++)
        {
            vm.SetSelected(items[i], true);
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
