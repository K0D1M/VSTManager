using System.IO;
using System.Windows.Forms;

namespace VstManager.App.Services;

/// <summary>
/// Shows Windows notifications (routed through Action Center) via a hidden NotifyIcon's balloon
/// tip — the simplest mechanism that works from an unpackaged WPF app with no MSIX identity,
/// unlike the WinRT toast APIs which require one. The icon itself is never shown in the tray;
/// it only exists to host the balloon.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;

    public NotificationService(string iconPath)
    {
        _icon = new NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
            Visible = false
        };
    }

    public void Show(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        // Visible must be true for the balloon to surface, but the icon should otherwise stay
        // out of the tray — toggle it on just long enough to raise the notification.
        _icon.Visible = true;
        _icon.ShowBalloonTip(5000, title, message, icon);
        _icon.Visible = false;
    }

    public void Dispose() => _icon.Dispose();
}
