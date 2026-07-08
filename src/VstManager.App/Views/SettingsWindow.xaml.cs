using System.Windows;
using Microsoft.Win32;
using VstManager.App.ViewModels;

namespace VstManager.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
