using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// First-run window, shown when no library file exists yet. Folder picking goes through
/// Avalonia's StorageProvider rather than a Win32 dialog.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _vm = new();

    public WelcomeWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a VST plugin folder",
            AllowMultiple = false
        });

        _vm.AddFolder(folders.FirstOrDefault()?.TryGetLocalPath());
    }

    private void GetStarted_Click(object? sender, RoutedEventArgs e)
    {
        _vm.Finish();
        Close();
    }
}
