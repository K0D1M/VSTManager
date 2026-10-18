using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VstManager.Core.Models;
using VstManager.Ui.Services;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Offered after a scan finds new plugins that still need a category (mandatory) and a per-copy
/// Legit/Cracked call. A plugin with everything answered is saved at once and, after a short
/// pause, leaves the list on its own; the window closes when none are left. "Later" persists
/// nothing for the rest, so their NeedsCategoryReview flag stays set and they come back on the
/// next scan — see MainViewModel's trigger for why that's keyed off a persistent flag.
/// </summary>
public partial class ClassifyPluginsWindow : Window
{
    /// <summary>How long a completed plugin stays visible, so the user sees their last click land.</summary>
    private static readonly TimeSpan LeaveDelay = TimeSpan.FromSeconds(2.5);

    private readonly MainViewModel _main;
    private readonly ObservableCollection<ClassifyPluginViewModel> _plugins = [];
    private readonly Dictionary<ClassifyPluginViewModel, CancellationTokenSource> _leaving = [];

    public ClassifyPluginsWindow()
    {
        InitializeComponent();
        _main = null!;
    }

    public ClassifyPluginsWindow(MainViewModel main, IReadOnlyList<PluginCardViewModel> plugins) : this()
    {
        _main = main;
        foreach (var plugin in plugins.Select(p => new ClassifyPluginViewModel(p)))
        {
            _plugins.Add(plugin);
            plugin.PropertyChanged += OnPluginChanged;
        }
        PluginList.ItemsSource = _plugins;

        // The category chip row binds AvailableCategories straight off MainViewModel.
        DataContext = main;

        Closed += (_, _) =>
        {
            foreach (var cts in _leaving.Values)
            {
                cts.Cancel();
            }
        };

        UpdateSaveEnabled();
    }

    /// <summary>Every listed plugin needs a category before Save will do anything.</summary>
    public bool CanSave => _plugins.All(p => p.HasCategorySelected);

    private void UpdateSaveEnabled() => SaveButton.IsEnabled = _plugins.Count > 0 && CanSave;

    private void CategoryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CategoryDefinition category, Tag: ClassifyPluginViewModel plugin })
        {
            plugin.SelectedCategoryId = category.Id;
        }
    }

    /// <summary>
    /// Any change restarts the countdown: a completed plugin is saved as shown right away, and
    /// leaves only if it is still complete when the pause runs out. A change made during the
    /// pause cancels the exit, and the current choices are saved again so disk matches the screen.
    /// </summary>
    private void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateSaveEnabled();
        if (sender is not ClassifyPluginViewModel plugin || e.PropertyName != nameof(ClassifyPluginViewModel.IsComplete))
        {
            return;
        }

        if (_leaving.Remove(plugin, out var previous))
        {
            previous.Cancel();
            if (plugin.HasCategorySelected)
            {
                Apply(plugin);
            }
        }

        if (plugin.IsComplete)
        {
            Apply(plugin);
            var cts = new CancellationTokenSource();
            _leaving[plugin] = cts;
            _ = LeaveAfterDelayAsync(plugin, cts.Token);
        }
    }

    private async Task LeaveAfterDelayAsync(ClassifyPluginViewModel plugin, CancellationToken token)
    {
        try
        {
            await Task.Delay(LeaveDelay, token);

            if (FindCard(plugin) is { } card)
            {
                await Motion.Conceal(card, dy: -6, durationMs: 200, token: token);
                card.Opacity = 0;   // hold the faded state while the height collapses
                await CollapseAsync(card, token);
            }

            token.ThrowIfCancellationRequested();
            _leaving.Remove(plugin);
            plugin.PropertyChanged -= OnPluginChanged;
            _plugins.Remove(plugin);
            UpdateSaveEnabled();

            if (_plugins.Count == 0)
            {
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            // A choice changed during the pause (or the window closed): the plugin stays.
            if (FindCard(plugin) is { } card)
            {
                card.Opacity = 1;
            }
        }
    }

    /// <summary>The card shrinks to nothing so the ones below slide up rather than jump.</summary>
    private static Task CollapseAsync(Control card, CancellationToken token)
    {
        var from = card.Bounds.Height;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 180),
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward
        };
        var start = new KeyFrame { Cue = new Cue(0) };
        start.Setters.Add(new Setter(HeightProperty, from));
        start.Setters.Add(new Setter(MarginProperty, card.Margin));
        var end = new KeyFrame { Cue = new Cue(1) };
        end.Setters.Add(new Setter(HeightProperty, 0d));
        end.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        animation.Children.Add(start);
        animation.Children.Add(end);
        return animation.RunAsync(card, token);
    }

    private Control? FindCard(ClassifyPluginViewModel plugin) =>
        PluginList.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("classifyCard") && ReferenceEquals(b.DataContext, plugin));

    /// <summary>Writes one plugin's staged choices — the same calls Save makes for each.</summary>
    private void Apply(ClassifyPluginViewModel plugin)
    {
        var category = _main.AvailableCategories.FirstOrDefault(c => c.Id == plugin.SelectedCategoryId);
        _main.SetCategory(plugin.Plugin, category, expandSelection: false);

#if !NO_LICENSE_STATUS
        foreach (var copy in plugin.Copies)
        {
            _main.SetCopyTag(plugin.Plugin, copy.Copy, copy.SelectedTag);
        }
#endif
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanSave)
        {
            return;
        }

        foreach (var plugin in _plugins)
        {
            Apply(plugin);
        }

        Close();
    }

    private void Later_Click(object? sender, RoutedEventArgs e) => Close();
}
