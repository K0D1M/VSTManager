using System.Windows;
using Microsoft.Win32;
using VstManager.App.Services;
using VstManager.App.ViewModels;

namespace VstManager.App.Views;

public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _viewModel = new();

    public WelcomeWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => WindowSizing.FitToScreen(this);

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a VST plugin folder"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddFolderCommand.Execute(dialog.FolderName);
        }
    }

    private void GetStarted_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Finish();
        Close();
    }
}
