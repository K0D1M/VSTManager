using System.Windows;
using Microsoft.Win32;
using VstManager.Core.Abstractions;

namespace VstManager.App.Services;

/// <summary>WPF implementation of <see cref="IDialogService"/>, over MessageBox and the Win32 pickers.</summary>
public class WpfDialogService : IDialogService
{
    public void ShowMessage(string message, string title, DialogSeverity severity = DialogSeverity.Information) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, ToImage(severity));

    public bool Confirm(string message, string title, DialogSeverity severity = DialogSeverity.Question) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, ToImage(severity)) == MessageBoxResult.Yes;

    // The pickers are synchronous in WPF; the interface is async because Avalonia's StorageProvider
    // is. Task.FromResult keeps the WPF side honest rather than faking work on a thread pool.
    public Task<string?> PickFolderAsync(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    public Task<string?> PickFileAsync(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickSaveFileAsync(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    private static MessageBoxImage ToImage(DialogSeverity severity) => severity switch
    {
        DialogSeverity.Question => MessageBoxImage.Question,
        DialogSeverity.Warning => MessageBoxImage.Warning,
        DialogSeverity.Error => MessageBoxImage.Error,
        _ => MessageBoxImage.Information
    };
}
