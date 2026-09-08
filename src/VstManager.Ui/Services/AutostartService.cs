using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VstManager.Ui.Services;

/// <summary>
/// Launch-at-login, which has no shared mechanism across the platforms: Windows uses a registry
/// Run value, macOS a LaunchAgents plist, and Linux an XDG autostart .desktop file. The WPF app's
/// AutostartService only implements the Windows half — this is the portable equivalent.
///
/// The macOS and Linux branches are written to spec and UNVERIFIED; only the Windows path,
/// which matches the WPF service byte for byte, has actually been exercised.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Deliberately the same value name the WPF app's AutostartService uses. The two heads are
    /// one app to the user, so they must not both register to launch at login — whichever was
    /// configured last owns the entry, and enabling it here repoints it at this executable.
    /// </summary>
    private const string ValueName = "VstManager";
    private const string LaunchAgentLabel = "com.vstmanager.app";

    /// <summary>
    /// Whether launch-at-login is currently registered. Presence of the entry is the whole
    /// answer — there is no separate enabled flag to consult on any of the three platforms.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return IsEnabledWindows();
            }

            return File.Exists(UnixEntryPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // An unreadable registry hive or home directory means "we can't tell"; reporting off
            // is better than failing to open Settings at all.
            return false;
        }
    }

    /// <summary>Registers or removes the launch-at-login entry. Returns false if it could not be written.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetEnabledWindows(enabled);
                return true;
            }

            var path = UnixEntryPath();

            if (!enabled)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, OperatingSystem.IsMacOS() ? LaunchAgentPlist() : DesktopEntry());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    [SupportedOSPlatform("windows")]
    private static void SetEnabledWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath()}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Where the autostart entry lives on the two Unix-likes. Both are per-user paths under the
    /// home directory, so neither needs elevation.
    /// </summary>
    private static string UnixEntryPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "LaunchAgents", $"{LaunchAgentLabel}.plist")
            : Path.Combine(home, ".config", "autostart", "vstmanager.desktop");
    }

    /// <summary>
    /// On macOS the running binary sits inside the .app bundle at Contents/MacOS/, but launchd
    /// should start the *bundle* so the app gets its Dock entry and icon rather than running as a
    /// bare process. Walk up to the .app when there is one.
    /// </summary>
    private static string ExecutablePath()
    {
        var exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

        if (!OperatingSystem.IsMacOS())
        {
            return exe;
        }

        var dir = Path.GetDirectoryName(exe);

        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return exe;
    }

    private static string LaunchAgentPlist()
    {
        var target = ExecutablePath();

        // "open" is used rather than a direct ProgramArguments path so a .app bundle launches as
        // a proper GUI application; for a bare binary it still works.
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{LaunchAgentLabel}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>/usr/bin/open</string>
                    <string>{System.Security.SecurityElement.Escape(target)}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
            </dict>
            </plist>

            """;
    }

    private static string DesktopEntry() => $"""
        [Desktop Entry]
        Type=Application
        Name=VST Manager
        Exec="{ExecutablePath()}"
        X-GNOME-Autostart-enabled=true

        """;
}
