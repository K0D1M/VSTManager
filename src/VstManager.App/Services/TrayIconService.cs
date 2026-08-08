using System.IO;
using System.Windows.Forms;

namespace VstManager.App.Services;

/// <summary>
/// The persistent tray icon shown while "Minimize to tray" is active and the main window is
/// hidden — distinct from <see cref="NotificationService"/>'s icon, which stays invisible and
/// only exists to host balloon tips. This one is visible for as long as the app is running in
/// the background, with a context menu to restore the window or exit for good.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(string iconPath)
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open VST Manager", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
            Text = "VST Manager",
            Visible = false,
            ContextMenuStrip = contextMenu
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show() => _icon.Visible = true;

    public void Hide() => _icon.Visible = false;

    public void Dispose() => _icon.Dispose();
}
