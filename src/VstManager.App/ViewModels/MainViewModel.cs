using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.App.Services;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.App.ViewModels;

public enum ManagementMode
{
    Legit,
    Cracked
}

public enum InstalledFilterOption
{
    All,
    InstalledOnly,
    NotInstalledOnly
}

public enum FormatFilterOption
{
    All,
    Vst2,
    Vst3
}

public partial class MainViewModel : ObservableObject
{
    private readonly ScanPathProvider _scanPathProvider = new();
    private readonly PluginScanner _scanner = new();
    private readonly LibraryStore _libraryStore = new();
    private readonly PluginCatalog _catalog = new();
    private readonly PluginDisplayBuilder _displayBuilder = new();
    private readonly LogoCache _logoCache = new();
    private readonly UninstallerLookup _uninstallerLookup = new();
    private readonly AutostartService _autostartService = new();
    private readonly UpdateChecker _updateChecker = new();

    private LibraryData _library = new();

    public ObservableCollection<PluginDisplayViewModel> Plugins { get; } = new();

    public ICollectionView PluginsView { get; }

    [ObservableProperty]
    private ManagementMode _mode = ManagementMode.Legit;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private InstalledFilterOption _installedFilter = InstalledFilterOption.All;

    [ObservableProperty]
    private FormatFilterOption _formatFilter = FormatFilterOption.All;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private Color _accentColor = (Color)ColorConverter.ConvertFromString("#FF8A5CF6");

    [ObservableProperty]
    private string _accentHexInput = "#FF8A5CF6";

    public IReadOnlyList<Color> AccentSwatches { get; } = new[]
    {
        "#FF8A5CF6", "#FF2DD4BF", "#FFF97316", "#FFEF4444",
        "#FF3B82F6", "#FFEC4899", "#FF22C55E", "#FFEAB308"
    }.Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToList();

    [ObservableProperty]
    private bool _autostartEnabled;

    [ObservableProperty]
    private DateTime? _lastUpdateCheck;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public string CurrentVersion => UpdateChecker.CurrentVersion;

    public IEnumerable<string> CustomScanFolders => _library.CustomScanFolders;

    private bool _isInitializing;
    private bool _isSyncingHexInput;

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeManager.Apply(value ? AppTheme.Dark : AppTheme.Light);
        SaveThemeSettings();
    }

    partial void OnAccentColorChanged(Color value)
    {
        ThemeManager.ApplyAccent(value);
        SaveThemeSettings();

        _isSyncingHexInput = true;
        AccentHexInput = value.ToString();
        _isSyncingHexInput = false;
    }

    partial void OnAccentHexInputChanged(string value)
    {
        if (_isSyncingHexInput)
        {
            return;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                AccentColor = color;
            }
        }
        catch (FormatException)
        {
            // Ignore invalid hex input until the user finishes typing a valid value.
        }
    }

    [RelayCommand]
    private void SelectAccentColor(Color color) => AccentColor = color;

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _autostartService.SetEnabled(value);
        _library.AutostartEnabled = value;
        _libraryStore.Save(_library);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingForUpdates = true;
        UpdateStatusText = string.Empty;

        var result = await _updateChecker.CheckForUpdateAsync();

        LastUpdateCheck = DateTime.Now;
        _library.LastUpdateCheck = LastUpdateCheck;
        _libraryStore.Save(_library);

        UpdateStatusText = result switch
        {
            { Error: not null } => $"Update check failed: {result.Error}",
            { UpdateAvailable: true } => $"Update available: v{result.LatestVersion}",
            _ => "You're up to date."
        };

        if (result.UpdateAvailable && result.ReleaseUrl is not null)
        {
            var openResult = MessageBox.Show(
                $"A new version (v{result.LatestVersion}) is available. Open the release page?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openResult == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
            }
        }

        IsCheckingForUpdates = false;
    }

    private void SaveThemeSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        _library.IsDarkTheme = IsDarkTheme;
        _library.AccentColor = AccentColor.ToString();
        _libraryStore.Save(_library);
    }

    public MainViewModel()
    {
        PluginsView = CollectionViewSource.GetDefaultView(Plugins);
        PluginsView.Filter = FilterPredicate;
        PluginsView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.IsInstalled), ListSortDirection.Descending));
        PluginsView.SortDescriptions.Add(new SortDescription(nameof(PluginDisplayViewModel.Name), ListSortDirection.Ascending));

        _isInitializing = true;
        var settings = _libraryStore.Load();
        _isDarkTheme = settings.IsDarkTheme;
        if (ColorConverter.ConvertFromString(settings.AccentColor) is Color accent)
        {
            _accentColor = accent;
            _accentHexInput = accent.ToString();
        }
        ThemeManager.Apply(_isDarkTheme ? AppTheme.Dark : AppTheme.Light);
        ThemeManager.ApplyAccent(_accentColor);

        _autostartEnabled = _autostartService.IsEnabled();
        _lastUpdateCheck = settings.LastUpdateCheck;
        _isInitializing = false;

        LoadAndScan();

        _ = CheckForUpdatesCommand.ExecuteAsync(null);
    }

    partial void OnModeChanged(ManagementMode value) => PluginsView.Refresh();
    partial void OnSearchTextChanged(string value) => PluginsView.Refresh();
    partial void OnInstalledFilterChanged(InstalledFilterOption value) => PluginsView.Refresh();
    partial void OnFormatFilterChanged(FormatFilterOption value) => PluginsView.Refresh();

    [RelayCommand]
    private void ResetFilters()
    {
        InstalledFilter = InstalledFilterOption.All;
        FormatFilter = FormatFilterOption.All;
        SearchText = string.Empty;
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not PluginDisplayViewModel vm)
        {
            return false;
        }

        var matchesMode = Mode switch
        {
            ManagementMode.Legit => vm.Tag is PluginTag.Legit or PluginTag.Unclassified,
            ManagementMode.Cracked => vm.Tag is PluginTag.Cracked or PluginTag.Unclassified,
            _ => true
        };

        if (!matchesMode)
        {
            return false;
        }

        var matchesInstalled = InstalledFilter switch
        {
            InstalledFilterOption.InstalledOnly => vm.IsInstalled,
            InstalledFilterOption.NotInstalledOnly => !vm.IsInstalled,
            _ => true
        };

        if (!matchesInstalled)
        {
            return false;
        }

        var matchesFormat = FormatFilter switch
        {
            FormatFilterOption.Vst2 => vm.Format == PluginFormat.Vst2,
            FormatFilterOption.Vst3 => vm.Format == PluginFormat.Vst3,
            _ => true
        };

        if (!matchesFormat)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || (vm.Vendor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
               || (vm.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand]
    private void Rescan() => LoadAndScan();

    private void LoadAndScan()
    {
        _library = _libraryStore.Load();

        var vst3Paths = _scanPathProvider.GetVst3Paths(_library.CustomScanFolders);
        var vst2Paths = _scanPathProvider.GetVst2Paths(_library.CustomScanFolders);
        var scanned = _scanner.Scan(vst3Paths, vst2Paths);

        var merged = _libraryStore.MergeOnRescan(_library.Plugins, scanned);
        _library.Plugins = merged;
        _libraryStore.Save(_library);

        var displayItems = _displayBuilder.Build(_catalog.Entries, merged);

        Plugins.Clear();
        foreach (var item in displayItems)
        {
            var vm = new PluginDisplayViewModel(item) { Tag = item.Tag };
            Plugins.Add(vm);
            _ = LoadLogoAsync(vm, item);
        }
    }

    private async Task LoadLogoAsync(PluginDisplayViewModel vm, PluginDisplayItem item)
    {
        if (item.Catalog is null)
        {
            return;
        }

        var path = await _logoCache.GetLogoPathAsync(item.Catalog);
        vm.LogoPath = path;
    }

    [RelayCommand]
    private void MarkLegit(PluginDisplayViewModel? vm) => SetTag(vm, PluginTag.Legit);

    [RelayCommand]
    private void MarkCracked(PluginDisplayViewModel? vm) => SetTag(vm, PluginTag.Cracked);

    [RelayCommand]
    private async Task RefreshMetadata(PluginDisplayViewModel? vm)
    {
        if (vm?.Catalog is null)
        {
            return;
        }

        var path = await _logoCache.RefreshLogoAsync(vm.Catalog);
        vm.LogoPath = path;
    }

    private void SetTag(PluginDisplayViewModel? vm, PluginTag tag)
    {
        if (vm?.Installed is null)
        {
            return;
        }

        vm.Installed.Tag = tag;
        vm.Tag = tag;

        var stored = _library.Plugins.FirstOrDefault(p => string.Equals(p.Path, vm.Installed.Path, StringComparison.OrdinalIgnoreCase));
        if (stored is not null)
        {
            stored.Tag = tag;
        }

        _libraryStore.Save(_library);
        PluginsView.Refresh();
    }

    [RelayCommand]
    private void ShowInFolder(PluginDisplayViewModel? vm)
    {
        var path = vm?.Installed?.Path;
        if (path is null || !File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void Uninstall(PluginDisplayViewModel? vm)
    {
        if (vm?.Installed is null)
        {
            return;
        }

        var uninstaller = _uninstallerLookup.FindUninstaller(vm.Name, vm.Vendor);
        if (uninstaller is not null)
        {
            var result = MessageBox.Show(
                $"Run the uninstaller for \"{uninstaller.DisplayName}\"?",
                "Uninstall Plugin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes || !ConfirmFinalUninstall(vm.Name))
            {
                return;
            }

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{uninstaller.UninstallCommand}\"") { UseShellExecute = true });
            return;
        }

        var deleteResult = MessageBox.Show(
            $"No registered uninstaller was found for \"{vm.Name}\".\n\nDelete the plugin file directly instead?\n\n{vm.Path}",
            "No Uninstaller Found",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (deleteResult != MessageBoxResult.Yes || !ConfirmFinalUninstall(vm.Name))
        {
            return;
        }

        DeletePluginFile(vm.Installed.Path);
        LoadAndScan();
    }

    private static bool ConfirmFinalUninstall(string pluginName)
    {
        var result = MessageBox.Show(
            $"This will permanently uninstall \"{pluginName}\" from this computer.\n\nThis action cannot be undone. Are you sure you want to continue?",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    private static void DeletePluginFile(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [RelayCommand]
    private void AddScanFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!_library.CustomScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _library.CustomScanFolders.Add(folder);
            _libraryStore.Save(_library);
            OnPropertyChanged(nameof(CustomScanFolders));
        }
    }

    [RelayCommand]
    private void RemoveScanFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _library.CustomScanFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _libraryStore.Save(_library);
        OnPropertyChanged(nameof(CustomScanFolders));
    }

    public IReadOnlyList<string> DefaultVst3Paths => ScanPathProvider.DefaultVst3Paths;
    public IReadOnlyList<string> DefaultVst2Paths => ScanPathProvider.DefaultVst2Paths;
}
