using DesktopNotifications;
using DesktopNotifications.Apple;
using VstManager.Ui.WindowsNotifications;

namespace VstManager.Ui.Services;

/// <summary>
/// Shows the app's informational notices ("Plugin scan complete", update summaries) as real OS
/// notifications — Action Center on Windows, Notification Center on macOS — via the
/// DesktopNotifications package rather than Avalonia's in-window toast.
///
/// This replaces an earlier in-window-only implementation (WindowNotificationManager). That one
/// had a real, stated limitation: a notification raised while the window was hidden in the tray
/// had nowhere to appear and was silently dropped, since there was no window to draw a toast
/// into. A native OS notification has no such dependency — it is the whole reason background
/// apps use them — so this is a strict improvement, not just a platform-integration nicety.
///
/// The Windows manager is constructed via VstManager.Ui.WindowsNotifications rather than
/// referenced directly here — DesktopNotifications.Windows wraps the WinRT toast API, and that
/// only activates correctly when built against a Windows-versioned TFM. Confirmed by an isolated
/// reproduction: the identical code and package version threw a TypeLoadException loading
/// Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat under this project's plain
/// net10.0, and completed cleanly under net10.0-windows10.0.19041.0. This project has to stay
/// plain net10.0 for macOS, so the Windows-specific half lives in a small satellite project
/// instead and is referenced back in via a SetTargetFramework override (see
/// VstManager.Ui.csproj) — this file only ever sees the portable INotificationManager interface,
/// never the concrete Windows type or its own dependencies.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private INotificationManager? _manager;

    /// <summary>
    /// Sets up the platform notification manager. Best-effort: a machine that refuses the
    /// Windows shortcut/registry writes, or a platform this package doesn't cover (Linux — see
    /// the class doc for why that's out of scope), must still get a working app with silent
    /// no-op notifications rather than a startup failure.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _manager = CreateManagerForPlatform();

            if (_manager is not null)
            {
                await _manager.Initialize();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException
                                       or PlatformNotSupportedException or TypeLoadException)
        {
            _manager = null;
        }
    }

    private static INotificationManager? CreateManagerForPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsNotificationManagerFactory.Create();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new AppleNotificationManager();
        }

        // Linux would need DesktopNotifications.FreeDesktop, a separate package this project
        // does not reference — this app's two platforms are Windows and macOS.
        return null;
    }

    /// <summary>
    /// Shows an informational notification. Safe to call from any thread — scans and syncs
    /// complete on background threads. Fire-and-forget by design, matching the rest of this
    /// app's notifications: nothing here is worth blocking a background operation to await, and a
    /// failure is swallowed rather than surfaced, since a missed notification is not worth
    /// interrupting the user over.
    ///
    /// <paramref name="imagePath"/> is a local file path (e.g. a cached plugin logo) shown as the
    /// notification's thumbnail — optional, since most notifications (scan complete, several
    /// plugins outdated at once) have no single image to represent them.
    /// </summary>
    public void Show(string title, string message, string? imagePath = null)
    {
        if (_manager is null)
        {
            return;
        }

        _ = ShowCoreAsync(title, message, imagePath);
    }

    private async Task ShowCoreAsync(string title, string message, string? imagePath)
    {
        try
        {
            var notification = new Notification { Title = title, Body = message };

            // Only set when the file actually exists: a stale/missing path handed to the native
            // toast API is exactly the kind of thing that silently breaks the whole notification
            // on some platforms rather than just omitting the image.
            if (imagePath is not null && File.Exists(imagePath))
            {
                notification.BodyImagePath = imagePath;
            }

            await _manager!.ShowNotification(notification);
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException)
        {
            // A notification that fails to show is not worth crashing a background scan over.
        }
    }

    public void Dispose()
    {
        if (_manager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
