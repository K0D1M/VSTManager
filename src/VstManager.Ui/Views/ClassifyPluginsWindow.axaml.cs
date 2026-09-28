using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Core.Models;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Offered after a scan finds new plugins that still need a category (mandatory) and, for ones
/// with several copies on disk, a per-copy Legit/Cracked call (still optional, unchanged from
/// when this window covered only that). "Later" persists nothing, so an incomplete plugin's
/// NeedsCategoryReview flag stays set and it reappears on the next scan — see MainViewModel's
/// trigger for why that's keyed off a persistent flag rather than the transient IsNew badge.
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

        // The window itself has no dedicated view model (everything is driven from code-behind
        // via ItemsSource), but the Category chip row needs somewhere to bind AvailableCategories
        // from — MainViewModel already exposes it, so this window's DataContext points straight
        // at it rather than introducing a new wrapper type for one property.
        DataContext = main;

        foreach (var plugin in _plugins)
        {
            plugin.PropertyChanged += (_, _) => UpdateSaveEnabled();
        }

        UpdateSaveEnabled();
    }

    /// <summary>Every listed plugin needs a category before Save will do anything — this is the
    /// "mandatory" half of the feature, enforced here rather than only by convention.</summary>
    public bool CanSave => _plugins.All(p => p.HasCategorySelected);

    private void UpdateSaveEnabled() => SaveButton.IsEnabled = CanSave;

    private void CategoryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: CategoryDefinition category, Tag: ClassifyPluginViewModel plugin })
        {
            return;
        }

        plugin.SelectedCategoryId = category.Id;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanSave)
        {
            return;
        }

        foreach (var plugin in _plugins)
        {
            var category = _main.AvailableCategories.FirstOrDefault(c => c.Id == plugin.SelectedCategoryId);
            _main.SetCategory(plugin.Plugin, category);

#if !NO_LICENSE_STATUS
            foreach (var copy in plugin.Copies)
            {
                _main.SetCopyTag(plugin.Plugin, copy.Copy, copy.SelectedTag);
            }
#endif
        }

        Close();
    }

    private void Later_Click(object? sender, RoutedEventArgs e) => Close();
}
