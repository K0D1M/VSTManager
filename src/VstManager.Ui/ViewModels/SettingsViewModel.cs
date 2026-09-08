using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;
using VstManager.Ui.Themes;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// Everything the Settings window edits. Writes straight through to the shared library.json the
/// WPF app also reads, so a change made here is visible there and vice versa.
///
/// Autostart, notifications and minimize-to-tray are backed by this project's own per-platform
/// services rather than VstManager.App's registry/WinForms ones, so they work on macOS too — see
/// AutostartService, NotificationService and TrayIconService.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly LibraryStore _libraryStore;
    private readonly PluginTagService _pluginTags;
    private readonly DataPortabilityService _dataPortability = new();
    private LibraryData _library;

    /// <summary>Raised when a change needs the library rescanned or redrawn to take effect.</summary>
    public event EventHandler? LibraryReloadRequested;

    public SettingsViewModel(MainViewModel main, LibraryStore libraryStore, PluginTagService pluginTags, LibraryData library)
    {
        _main = main;
        _libraryStore = libraryStore;
        _pluginTags = pluginTags;
        _library = library;

        _themeMode = ThemeService.Mode;
        _accentHex = library.AccentColor;

        // The sync service reports progress on the main view model; mirror those so this tab's
        // status line updates live rather than only when reopened.
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CloudStatusMessage)
                or nameof(MainViewModel.CloudState)
                or nameof(MainViewModel.CloudSyncEnabled))
            {
                OnPropertyChanged(nameof(CloudStatusMessage));
                OnPropertyChanged(nameof(CloudSyncEnabled));
            }

            // AutostartEnabled can be reverted by the view model itself when the OS refuses the
            // write, so the checkbox has to follow it rather than keep the user's click.
            if (e.PropertyName is nameof(MainViewModel.AutostartEnabled)
                or nameof(MainViewModel.AutostartError))
            {
                OnPropertyChanged(nameof(AutostartEnabled));
                OnPropertyChanged(nameof(AutostartError));
                OnPropertyChanged(nameof(HasAutostartError));
            }
        };

        foreach (var folder in library.CustomScanFolders)
        {
            ScanFolders.Add(folder);
        }

        RebuildTags();
    }

    // ---- appearance ---------------------------------------------------------

    /// <summary>
    /// Three-way, unlike the main window's light/dark toggle: Avalonia can follow the OS live,
    /// which the WPF app cannot do, so System is worth exposing as a first-class choice.
    /// </summary>
    [ObservableProperty]
    private AppThemeMode _themeMode;

    [ObservableProperty]
    private string _accentHex;

    /// <summary>A spread of accents, matching the palette the rest of the app is built from.</summary>
    public IReadOnlyList<string> AccentSwatches { get; } =
    [
        "#FF8A5CF6", "#FF3B82F6", "#FF06B6D4", "#FF10B981",
        "#FF84CC16", "#FFFBBF24", "#FFF97316", "#FFEF4444",
        "#FFEC4899", "#FFA855F7"
    ];

    public bool IsSystemTheme
    {
        get => ThemeMode == AppThemeMode.System;
        set { if (value) { ThemeMode = AppThemeMode.System; } }
    }

    public bool IsLightTheme
    {
        get => ThemeMode == AppThemeMode.Light;
        set { if (value) { ThemeMode = AppThemeMode.Light; } }
    }

    public bool IsDarkTheme
    {
        get => ThemeMode == AppThemeMode.Dark;
        set { if (value) { ThemeMode = AppThemeMode.Dark; } }
    }

    partial void OnThemeModeChanged(AppThemeMode value)
    {
        ThemeService.Apply(value);

        // library.json only carries a bool, shared with the WPF app. "System" is stored as
        // whichever variant the OS currently resolves to, so the WPF head still opens sensibly.
        _library.IsDarkTheme = ThemeService.IsDark;
        Save();

        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void SetAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex, out var color))
        {
            return;
        }

        AccentHex = hex;
        ThemeService.ApplyAccent(color);
        _library.AccentColor = hex;
        Save();
    }

    // ---- scanning -----------------------------------------------------------

    /// <summary>Extra folders searched on top of the OS defaults.</summary>
    public ObservableCollection<string> ScanFolders { get; } = new();

    /// <summary>The always-scanned locations, shown read-only so the list isn't a mystery.</summary>
    public IReadOnlyList<string> DefaultScanFolders { get; } =
        ScanPathProvider.DefaultVst3Paths.Concat(ScanPathProvider.DefaultVst2Paths).ToList();

    public void AddScanFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || ScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ScanFolders.Add(folder);
        _library.CustomScanFolders = ScanFolders.ToList();
        Save();
        LibraryReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveScanFolder(string? folder)
    {
        if (folder is null || !ScanFolders.Remove(folder))
        {
            return;
        }

        _library.CustomScanFolders = ScanFolders.ToList();
        Save();
        LibraryReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- tags ---------------------------------------------------------------

    public ObservableCollection<TagDefinition> Tags { get; } = new();

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private string _newTagColor = "#FF8A5CF6";

    [ObservableProperty]
    private string? _tagStatus;

    public bool HasTagStatus => !string.IsNullOrWhiteSpace(TagStatus);

    partial void OnTagStatusChanged(string? value) => OnPropertyChanged(nameof(HasTagStatus));

    [RelayCommand]
    private void AddTag()
    {
        var name = NewTagName.Trim();
        if (name.Length == 0)
        {
            TagStatus = "Give the tag a name first.";
            return;
        }

        if (_library.Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            TagStatus = $"A tag called \"{name}\" already exists.";
            return;
        }

        // Slugified id, matching the preset convention, so assignments stay readable on disk.
        var id = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (id.Length == 0 || _library.Tags.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = Guid.NewGuid().ToString("N")[..8];
        }

        _library.Tags.Add(new TagDefinition
        {
            Id = id,
            Name = name,
            ColorHex = Color.TryParse(NewTagColor, out _) ? NewTagColor : "#FF8A5CF6",
            IsPreset = false
        });

        Save();
        RebuildTags();
        NewTagName = string.Empty;
        TagStatus = $"Added \"{name}\".";
    }

    /// <summary>
    /// Deletes a custom tag and strips it from every plugin. Presets are refused: the KVR
    /// category mapping targets their ids, so removing one would silently break auto-tagging.
    /// </summary>
    [RelayCommand]
    private void DeleteTag(TagDefinition? tag)
    {
        if (tag is null || tag.IsPreset)
        {
            return;
        }

        _pluginTags.RemoveTagEverywhere(tag.Id);
        _library.Tags.RemoveAll(t => string.Equals(t.Id, tag.Id, StringComparison.OrdinalIgnoreCase));
        Save();
        RebuildTags();
        TagStatus = $"Deleted \"{tag.Name}\".";
        LibraryReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildTags()
    {
        Tags.Clear();
        foreach (var tag in _library.Tags
                     .OrderByDescending(t => t.IsPreset)
                     .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Tags.Add(tag);
        }
    }

    // ---- cloud --------------------------------------------------------------

    /// <summary>
    /// Proxied straight through to the main view model, which owns the sync service — the toolbar
    /// indicator and this tab must never disagree about whether sync is on.
    /// </summary>
    public bool CloudSyncEnabled
    {
        get => _main.CloudSyncEnabled;
        set
        {
            if (_main.CloudSyncEnabled != value)
            {
                _main.CloudSyncEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string CloudStatusMessage => _main.CloudStatusMessage;

    public string CloudDeviceId => _main.CloudDeviceId;

    [ObservableProperty]
    private string _deviceIdInput = string.Empty;

    [RelayCommand]
    private async Task SyncNowAsync() => await _main.SyncCloudNowCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task UploadAsync() => await _main.UploadToCloudAsync();

    [RelayCommand]
    private async Task RestoreAsync() => await _main.RestoreFromCloudAsync();

    /// <summary>Pairs this machine with another by adopting its sync id.</summary>
    [RelayCommand]
    private void ApplyDeviceId()
    {
        var id = DeviceIdInput.Trim();
        if (id.Length == 0)
        {
            return;
        }

        _main.SetCloudDeviceId(id);
        DeviceIdInput = string.Empty;
        OnPropertyChanged(nameof(CloudDeviceId));
    }

    // ---- data ---------------------------------------------------------------

    [ObservableProperty]
    private string? _dataStatus;

    public bool HasDataStatus => !string.IsNullOrWhiteSpace(DataStatus);

    partial void OnDataStatusChanged(string? value) => OnPropertyChanged(nameof(HasDataStatus));

    /// <summary>Serialises everything portable; the window owns choosing where it lands.</summary>
    public string ExportBundle() => _dataPortability.ExportBundle();

    /// <summary>
    /// Replaces the local data files from a bundle, then reloads. Throws InvalidDataException on
    /// a file that isn't a VST Manager export — the window reports that.
    /// </summary>
    public void ImportBundle(string json)
    {
        _dataPortability.ImportBundle(json);

        // Everything downstream is now stale: re-read the library and re-resolve tags.
        _library = _libraryStore.Load();
        _pluginTags.Reload();
        RebuildTags();

        ScanFolders.Clear();
        foreach (var folder in _library.CustomScanFolders)
        {
            ScanFolders.Add(folder);
        }

        LibraryReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Save() => _libraryStore.Save(_library);

    // ---- general / platform ------------------------------------------------
    // Pass-throughs to the main view model, which owns the per-platform services and the writes
    // to library.json — the same shape the cloud properties above use.

    /// <summary>Launch VST Manager when the user signs in.</summary>
    public bool AutostartEnabled
    {
        get => _main.AutostartEnabled;
        set
        {
            if (_main.AutostartEnabled != value)
            {
                _main.AutostartEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Non-null when the OS refused the autostart change, so the tab can say why.</summary>
    public string? AutostartError => _main.AutostartError;

    public bool HasAutostartError => !string.IsNullOrEmpty(_main.AutostartError);

    /// <summary>Show a toast after scans and metadata refreshes.</summary>
    public bool ShowNotifications
    {
        get => _main.ShowNotifications;
        set
        {
            if (_main.ShowNotifications != value)
            {
                _main.ShowNotifications = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public bool MinimizeToTray
    {
        get => _main.MinimizeToTray;
        set
        {
            if (_main.MinimizeToTray != value)
            {
                _main.MinimizeToTray = value;
                OnPropertyChanged();
            }
        }
    }
}
