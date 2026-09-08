using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace VstManager.Ui.Services;

/// <summary>
/// The persistent tray icon shown while "Minimize to tray" is active and the main window is
/// hidden — a menu-bar item on macOS and a notification-area icon on Windows, both from the same
/// Avalonia TrayIcon rather than the WPF app's WinForms NotifyIcon.
///
/// Only shown while the window is actually hidden, matching WPF: an icon that sits in the tray
/// permanently alongside a visible window is clutter, and the app is not a background service.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon? _icon;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var icon = LoadIcon();

        if (icon is null || Application.Current is null)
        {
            // Without an icon the tray item cannot be drawn at all, so leave _icon null and let
            // IsAvailable tell the caller to keep the window visible instead.
            return;
        }

        var open = new NativeMenuItem("Open VST Manager");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new NativeMenu();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        _icon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "VST Manager",
            IsVisible = false,
            Menu = menu
        };

        // A left click on the icon restores the window, as it does in the WPF app. On macOS the
        // menu bar swallows this and only the menu opens, which is the platform convention.
        _icon.Clicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        // Registering with the Application is what actually creates the platform tray item.
        // A TrayIcon that is merely constructed has no native counterpart, so setting IsVisible
        // on it does nothing at all — silently, which is how this was missed the first time.
        // Append rather than replace, so this never clobbers icons owned by anything else.
        var icons = TrayIcon.GetIcons(Application.Current) ?? new TrayIcons();
        icons.Add(_icon);
        TrayIcon.SetIcons(Application.Current, icons);
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://VstManager.Ui/Assets/app-icon.ico")));
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether the icon exists; false means minimize-to-tray must not hide the window.</summary>
    public bool IsAvailable => _icon is not null;

    public void Show()
    {
        if (_icon is not null)
        {
            _icon.IsVisible = true;
        }
    }

    public void Hide()
    {
        if (_icon is not null)
        {
            _icon.IsVisible = false;
        }
    }

    public void Dispose()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.IsVisible = false;

        // Take it back out of the Application's collection; disposing alone leaves a dead entry
        // behind, which matters because the window can be recreated after a tray Exit.
        if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons)
        {
            icons.Remove(_icon);
        }

        _icon.Dispose();
    }
}
