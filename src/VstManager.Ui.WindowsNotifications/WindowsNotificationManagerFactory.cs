using DesktopNotifications;
using DesktopNotifications.Windows;

namespace VstManager.Ui.WindowsNotifications;

/// <summary>
/// The only public surface of this satellite assembly. Returns the portable
/// <see cref="INotificationManager"/> interface (defined in the DesktopNotifications core
/// package, which VstManager.Ui already references directly and which needs no Windows TFM), so
/// the caller in that plain net10.0 project never has to name the concrete
/// <see cref="WindowsNotificationManager"/> type or its own dependencies at all.
/// </summary>
public static class WindowsNotificationManagerFactory
{
    /// <summary>
    /// Deliberately the same AppUserModelID the WPF app registers under
    /// (VstManager.App/Services/AppIdentityService.cs). The two heads are one app to the user, so
    /// Windows should treat notifications from either as coming from the same sender rather than
    /// showing two separate entries in notification settings.
    /// </summary>
    private const string AppUserModelId = "K0D1M.VstManager";

    public static INotificationManager Create()
    {
        var context = WindowsApplicationContext.FromCurrentProcess("VST Manager", AppUserModelId);
        return new WindowsNotificationManager(context);
    }
}
