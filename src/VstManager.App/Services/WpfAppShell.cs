using System.Windows;
using VstManager.App.Views;
using VstManager.Core.Abstractions;
using VstManager.Core.Services.Cloud;

namespace VstManager.App.Services;

/// <summary>WPF implementation of <see cref="IAppShell"/>.</summary>
public class WpfAppShell : IAppShell
{
    public void Shutdown() => Application.Current?.Shutdown();

    /// <summary>
    /// Puts the conflict in front of the user. Sync runs on a background thread, so the dialog
    /// has to be marshalled to the UI one and its answer awaited before the sync continues.
    /// </summary>
    public async Task<ConflictResolution> AskAboutCloudConflictAsync(DateTime localChangedAt, DateTime remoteChangedAt)
    {
        var app = Application.Current;
        if (app is null)
        {
            return ConflictResolution.Skip;
        }

        return await app.Dispatcher.InvokeAsync(() =>
        {
            var owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? app.MainWindow;

            var dialog = new CloudConflictWindow(localChangedAt, remoteChangedAt);
            if (owner is not null && !ReferenceEquals(owner, dialog))
            {
                dialog.Owner = owner;
            }

            dialog.ShowDialog();
            return dialog.Resolution;
        });
    }
}
