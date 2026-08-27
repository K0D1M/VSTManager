using System.Diagnostics;
using System.IO;
using VstManager.Core.Abstractions;

namespace VstManager.App.Services;

/// <summary>Windows implementation of <see cref="IPlatformIntegration"/>.</summary>
public class WindowsPlatformIntegration : IPlatformIntegration
{
    public bool SupportsUninstallers => true;

    public void RevealInFileManager(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public void OpenExternal(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    /// <summary>
    /// Lets a failure to launch propagate as <see cref="System.ComponentModel.Win32Exception"/> or
    /// <see cref="IOException"/> — callers distinguish "couldn't start the uninstaller" (reported
    /// to the user by name) from "it ran", so swallowing that here would lose the distinction.
    /// </summary>
    public async Task<bool> RunUninstallerAsync(string uninstallCommand)
    {
        var process = Process.Start(
            new ProcessStartInfo("cmd.exe", $"/c \"{uninstallCommand}\"") { UseShellExecute = true });

        // Wait for the vendor uninstaller to finish so the caller can rescan. Best-effort: if the
        // launched process hands off to another and exits early, the rescan simply finds nothing
        // changed and the user can rescan again later.
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync();
        return true;
    }
}
