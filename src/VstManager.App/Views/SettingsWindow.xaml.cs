using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.App.ViewModels;
using VstManager.Core.Models;

namespace VstManager.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
        WindowIcon.ApplyDefault(this);
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

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var name = NewTagNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Give the tag a name first.", "New Tag", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (vm.CreateTag(name, NewTagColorBox.Text.Trim()) is null)
        {
            MessageBox.Show(this, $"There's already a tag called \"{name}\".", "New Tag",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NewTagNameBox.Clear();
    }

    private void EditTag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { Tag: TagDefinition tag })
        {
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Tag name:", "Edit Tag", tag.Name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var color = Microsoft.VisualBasic.Interaction.InputBox(
            "Colour as #AARRGGBB or #RRGGBB:", "Edit Tag", tag.ColorHex);

        vm.UpdateTag(tag, name, string.IsNullOrWhiteSpace(color) ? tag.ColorHex : color.Trim());
    }

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { Tag: TagDefinition tag })
        {
            return;
        }

        var inUse = vm.CountPluginsWithTag(tag);
        var message = inUse == 0
            ? $"Delete the tag \"{tag.Name}\"?"
            : $"Delete the tag \"{tag.Name}\"?\n\nIt's currently applied to {inUse} plugin(s), and will be removed from all of them.";

        var confirmResult = MessageBox.Show(this, message, "Delete Tag",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmResult == MessageBoxResult.Yes)
        {
            vm.DeleteTag(tag);
        }
    }

    private async void CloudUpload_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var confirmResult = MessageBox.Show(this,
            "This replaces the cloud copy with the settings on this machine. Anything only in the cloud will be lost. Continue?",
            "Upload to Cloud", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmResult == MessageBoxResult.Yes)
        {
            await vm.UploadToCloudAsync();
        }
    }

    private async void CloudRestore_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var confirmResult = MessageBox.Show(this,
            "This replaces this machine's plugin tags, scan folders, and preferences with the cloud copy. Continue?",
            "Restore from Cloud", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmResult == MessageBoxResult.Yes)
        {
            await vm.RestoreFromCloudAsync();
        }
    }

    private void CloudChangeId_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var entered = Microsoft.VisualBasic.Interaction.InputBox(
            "Paste the Sync ID from your other machine. Both machines will then share one settings copy.",
            "Sync ID", vm.CloudDeviceId);

        if (!string.IsNullOrWhiteSpace(entered))
        {
            vm.SetCloudDeviceId(entered);
            MessageBox.Show(this, vm.CloudStatusMessage, "Sync ID", MessageBoxButton.OK, MessageBoxImage.Information);
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
