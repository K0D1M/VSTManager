using System.IO;
using System.Windows.Forms;

namespace VstManager.App.Services;

/// <summary>
/// Shows Windows notifications via a hidden NotifyIcon's balloon tip — the simplest mechanism
/// that works from an unpackaged WPF app with no MSIX identity, unlike the WinRT toast APIs
/// which require one. The icon itself is never shown in the tray; it only exists to host the
/// balloon.
///
/// Reaching Action Center (rather than degrading to a transient legacy tray tooltip that leaves
/// no trace) additionally requires the process to have an AppUserModelID and a registered sender
/// entry — see <see cref="AppIdentityService"/>, which must run at startup for these to stick.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;

    public NotificationService(string iconPath)
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(iconPath),
            Visible = false
        };
    }

    /// <summary>
    /// Loads the app icon at the size Windows wants for a notification. Plain
    /// <c>new Icon(path)</c> hands back whichever frame the file lists first (16x16 here), which
    /// the shell then upscales into a blurry thumbnail; asking for the large-icon metric picks
    /// the right frame out of the multi-size .ico instead.
    /// </summary>
    private static System.Drawing.Icon LoadIcon(string iconPath)
    {
        if (!File.Exists(iconPath))
        {
            return System.Drawing.SystemIcons.Application;
        }

        try
        {
            var size = SystemInformation.IconSize;
            return new System.Drawing.Icon(iconPath, size.Width, size.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    public void Show(string title, string message)
    {
        // ToolTipIcon.None is deliberate: any other value makes Windows draw its own generic
        // info/warning/error glyph in place of the app icon. With None the shell falls back to
        // the NotifyIcon's own icon — i.e. the app logo — which is what we want on every one of
        // these notifications, since they are all informational anyway.
        _icon.Visible = true;
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.None);
        _icon.Visible = false;
    }

    public void Dispose() => _icon.Dispose();
}
