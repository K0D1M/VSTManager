using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Core.Models;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Offered after a scan finds new plugins that still need a category and a per-copy
/// Legit/Cracked call. Choices are staged here and written only when the user agrees to "Apply
/// changes?" — asked once every plugin is fully set, and again on closing if they said no the
/// first time. Closing with plugins unfinished writes nothing, so their NeedsCategoryReview flag
/// stays set and they come back on the next scan.
/// </summary>
public partial class ClassifyPluginsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IReadOnlyList<ClassifyPluginViewModel> _plugins;

    private bool _applied;
    private bool _askedWhileOpen;
    private bool _promptOpen;
    private bool _closeConfirmed;

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

        // The category chip row binds AvailableCategories straight off MainViewModel.
        DataContext = main;

        foreach (var plugin in _plugins)
        {
            plugin.PropertyChanged += OnPluginChanged;
        }

        Closing += OnClosing;
    }

    private bool AllComplete => _plugins.Count > 0 && _plugins.All(p => p.IsComplete);

    private void CategoryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CategoryDefinition category, Tag: ClassifyPluginViewModel plugin })
        {
            plugin.SelectedCategoryId = category.Id;
        }
    }

    /// <summary>The moment the last plugin is fully set, offer to apply — once while open.</summary>
    private async void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClassifyPluginViewModel.IsComplete) || !AllComplete
            || _askedWhileOpen || _promptOpen || _applied)
        {
            return;
        }

        _askedWhileOpen = true;
        if (await AskToApplyAsync())
        {
            Apply();
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>
    /// Closing with everything set but not applied (the user said no earlier, or closed straight
    /// after) asks once more; "Don't apply" then discards. Unfinished windows close silently.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || _applied || !AllComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_promptOpen)
        {
            return;
        }

        if (await AskToApplyAsync())
        {
            Apply();
        }

        _closeConfirmed = true;
        Close();
    }

    private async Task<bool> AskToApplyAsync()
    {
        _promptOpen = true;
        try
        {
            var what = _plugins.Count == 1 ? $"\"{_plugins[0].Name}\"" : $"all {_plugins.Count} plugins";
#if NO_LICENSE_STATUS
            var message = $"Save the category for {what}?";
#else
            var message = $"Save the category and Legit/Cracked choices for {what}?";
#endif
            return await ConfirmDialog.ShowAsync(this, "Apply changes?", message, "Apply", "Don't apply");
        }
        finally
        {
            _promptOpen = false;
        }
    }

    /// <summary>Writes every plugin's staged choices — to that plugin only, never widened by a
    /// multi-selection in the main window.</summary>
    private void Apply()
    {
        foreach (var plugin in _plugins)
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

        _applied = true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
