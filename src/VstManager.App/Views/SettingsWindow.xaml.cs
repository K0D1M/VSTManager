using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using VstManager.App.Services;
using VstManager.App.ViewModels;

namespace VstManager.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => WindowSizing.FitToScreen(this);

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a VST plugin folder"
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel vm)
        {
            vm.AddScanFolderCommand.Execute(dialog.FolderName);
        }
    }

    private async void ExportData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export VST Manager Data",
            Filter = "VST Manager Export (*.vstmanager.json)|*.vstmanager.json|All Files (*.*)|*.*",
            FileName = $"VstManager-Export-{DateTime.Now:yyyy-MM-dd}.vstmanager.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var (success, message) = await vm.ExportDataAsync(dialog.FileName);
        MessageBox.Show(this, message, "Export Data", MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void ImportData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import VST Manager Data",
            Filter = "VST Manager Export (*.vstmanager.json)|*.vstmanager.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmResult = MessageBox.Show(this,
            "This will replace your current plugin tags, scan folders, and preferences with the ones from the imported file. Continue?",
            "Import Data", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmResult != MessageBoxResult.Yes)
        {
            return;
        }

        var (success, message) = await vm.ImportDataAsync(dialog.FileName);
        MessageBox.Show(this, message, "Import Data", MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private void GitHubLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/K0D1M") { UseShellExecute = true });

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
