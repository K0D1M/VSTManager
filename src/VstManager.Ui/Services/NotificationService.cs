using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace VstManager.Ui.Services;

/// <summary>
/// Shows the app's informational notices ("Plugin scan complete", update summaries).
///
/// Deliberately different from the WPF app, which raises real Windows balloon tips through a
/// hidden WinForms NotifyIcon: that needs System.Windows.Forms, which this net10.0 project cannot
/// reference, and there is no cross-platform OS-notification API in Avalonia. So these are
/// in-window toasts drawn by Avalonia itself — visible while the window is up, on every platform,
/// with no per-OS code and no app-identity registration.
///
/// The tradeoff is real and worth stating: a notification raised while the window is hidden in
/// the tray has nowhere to appear and is dropped rather than reaching the OS notification centre.
/// Native notifications (Windows Action Center, macOS User Notifications) would each need their
/// own platform implementation and are left as separate work.
/// </summary>
public sealed class NotificationService
{
    private WindowNotificationManager? _manager;

    /// <summary>
    /// Adopts the manager declared in the window's XAML. It has to come from the visual tree —
    /// a WindowNotificationManager built in code-behind and never parented renders nothing at
    /// all, silently, which is exactly how this was missed the first time round.
    /// </summary>
    public void Attach(WindowNotificationManager manager) => _manager = manager;

    /// <summary>
    /// Shows an informational toast. Safe to call from any thread — scans and syncs complete on
    /// background threads, and Avalonia's notification manager is UI-thread-only.
    /// </summary>
    public void Show(string title, string message)
    {
        if (_manager is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
            _manager?.Show(new Notification(title, message, NotificationType.Information)));
    }
}
