using System.Diagnostics;

namespace VstManager.Ui.Services;

/// <summary>
/// Opens the OS file manager with a file selected. The WPF app shells explorer.exe directly;
/// this project targets plain net10.0, so each platform gets its own command.
/// </summary>
public static class Reveal
{
    /// <summary>
    /// The macOS and Linux branches are written to spec and UNVERIFIED — there is no Mac or
    /// Linux desktop available to this build. Windows is the path actually exercised.
    /// </summary>
    public static void InFolder(string? path)
    {
        if (path is null || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
            }
            else
            {
                // No cross-desktop "select this file" on Linux; opening the containing folder
                // is the portable approximation.
                Process.Start("xdg-open", [Path.GetDirectoryName(path) ?? path]);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A missing file manager is not worth a crash; the path is still visible in the UI.
        }
    }
}
