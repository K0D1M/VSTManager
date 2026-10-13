using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>Version, app updates, credits and licence — formerly Settings' last tab.</summary>
public partial class AboutWindow : Window
{
    private readonly SettingsViewModel _vm;

    public AboutWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>No Skip here: choosing Update from About means the user wants it now.</summary>
    private async void UpdateNow_Click(object? sender, RoutedEventArgs e) =>
        await UpdateAvailableWindow.ShowAsync(this, _vm.Main, offerSkip: false);

    private void AuthorLink_Click(object? sender, PointerPressedEventArgs e) => OpenUrl("https://github.com/K0D1M");

    private void RepoLink_Click(object? sender, PointerPressedEventArgs e) =>
        OpenUrl("https://github.com/K0D1M/VSTManager");

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>UseShellExecute lets the OS pick the default browser on Windows and macOS.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser, or a URL the shell refuses — not worth interrupting over.
        }
    }
}
