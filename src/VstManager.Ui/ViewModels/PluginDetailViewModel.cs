using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// DataContext for the detail window. Bundles the plugin being viewed, the draft edit form and
/// the tag picker so the window binds to plain paths, instead of the RelativeSource chains the
/// WPF version needs to reach its code-behind's Form property.
/// </summary>
public partial class PluginDetailViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public PluginCardViewModel Plugin { get; }

    public PluginEditFormViewModel Form { get; }

    public IReadOnlyList<TagPickerItemViewModel> TagItems { get; }

    /// <summary>Raised when Save has committed everything and the window should close.</summary>
    public event EventHandler? CloseRequested;

    public PluginDetailViewModel(MainViewModel main, PluginCardViewModel plugin)
    {
        _main = main;
        Plugin = plugin;
        Form = new PluginEditFormViewModel(plugin);
        TagItems = main.AvailableTags
            .Select(tag => new TagPickerItemViewModel(main, plugin, tag))
            .ToList();

        plugin.PropertyChanged += OnPluginChanged;
    }

    public string IgnoreLabel => Plugin.IgnoreVersionCheck ? "Show Update Badge" : "Ignore Version Updates";

    private void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginCardViewModel.IgnoreVersionCheck))
        {
            OnPropertyChanged(nameof(IgnoreLabel));
        }
    }

    [RelayCommand]
    private void ToggleIgnoreVersionCheck() => _main.ToggleIgnoreVersionCheckCommand.Execute(Plugin);

    /// <summary>
    /// Re-reads the installed version from the plugin file on demand, staging it into the form
    /// for the user to keep with Save. File metadata only — the WPF app also consults the
    /// Windows uninstall registry, which has no portable equivalent.
    /// </summary>
    [RelayCommand]
    private async Task DetectCurrentVersionAsync()
    {
        Form.IsDetectingCurrentVersion = true;
        Form.StatusText = "Reading the installed version…";

        try
        {
            var detected = await Task.Run(() => _main.DetectInstalledVersion(Plugin));

            if (detected is null)
            {
                Form.StatusText = "No version found in the plugin file. Some plugins never record "
                                  + "one — you can type it in yourself.";
                return;
            }

            var previous = Form.CurrentVersion?.Trim();
            Form.CurrentVersion = detected;
            Form.StatusText = string.Equals(previous, detected, StringComparison.OrdinalIgnoreCase)
                ? $"Confirmed version {detected} from the plugin file."
                : $"Found version {detected} in the plugin file. Click Save to keep it.";
        }
        finally
        {
            Form.IsDetectingCurrentVersion = false;
        }
    }

    /// <summary>
    /// Commits the draft. Returns false, without closing, when the name is empty — the window
    /// reports that, since this view model stays dialog-free.
    /// </summary>
    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(Form.Name))
        {
            return false;
        }

        var name = Form.Name.Trim();
        var vendor = Form.Vendor?.Trim() ?? string.Empty;

        if (name != Plugin.Name || vendor != (Plugin.Vendor ?? string.Empty))
        {
            _main.ApplyMetadataOverride(Plugin, name, vendor);
        }

        // Only when the installed version was actually edited. Recording versions on every Save
        // would pin the latest-version field as a manual override each time, silently blocking
        // future online refreshes from ever updating it.
        var current = Form.CurrentVersion?.Trim();
        if (!string.Equals(current ?? string.Empty, Plugin.CurrentVersion ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            _main.SetVersions(Plugin, current, Form.LatestVersion);
        }

        if (Form.Kind != Plugin.Kind)
        {
            if (Form.Kind == PluginKind.Instrument)
            {
                _main.MarkInstrumentCommand.Execute(Plugin);
            }
            else if (Form.Kind == PluginKind.Effect)
            {
                _main.MarkEffectCommand.Execute(Plugin);
            }
        }

        if (Form.SelectedTag == PluginTag.Legit)
        {
            _main.MarkLegitCommand.Execute(Plugin);
        }
        else if (Form.SelectedTag == PluginTag.Cracked)
        {
            _main.MarkCrackedCommand.Execute(Plugin);
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task<bool> ExcludePathAsync(string path) => _main.ExcludePathFromScanAsync(path);

    public Task<bool> MarkAsNotAPluginAsync() => _main.MarkAsNotAPluginAsync(Plugin);
}
