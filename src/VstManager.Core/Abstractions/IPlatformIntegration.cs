namespace VstManager.Core.Abstractions;

/// <summary>
/// Operations whose implementation differs per operating system: revealing a file in the system
/// file manager, opening a URL, and running a vendor uninstaller.
///
/// Some of these have no meaning on every platform — Windows vendor uninstallers are registry
/// entries with no macOS equivalent — so callers check <see cref="SupportsUninstallers"/> rather
/// than assuming the action is available.
/// </summary>
public interface IPlatformIntegration
{
    /// <summary>
    /// True when this platform can look up and run vendor uninstallers. False on macOS, where the
    /// UI should hide the uninstall action rather than offer one that cannot work.
    /// </summary>
    bool SupportsUninstallers { get; }

    /// <summary>
    /// Opens the system file manager with <paramref name="path"/> selected. Silently does nothing
    /// when the path no longer exists.
    /// </summary>
    void RevealInFileManager(string path);

    /// <summary>Opens a URL, or a local file, in whatever application the OS associates with it.</summary>
    void OpenExternal(string target);

    /// <summary>
    /// Runs a vendor uninstall command and waits for it to finish, so the caller can rescan
    /// afterwards. Returns false when the platform has no uninstaller support or the command
    /// could not be started.
    ///
    /// Best-effort by nature: an uninstaller that hands off to a second process and exits early
    /// will return before the removal actually completes.
    ///
    /// Implementations must let a launch failure propagate (as <c>Win32Exception</c> or
    /// <c>IOException</c>) rather than swallowing it — callers report "couldn't launch the
    /// uninstaller for X" separately from a run that completed.
    /// </summary>
    Task<bool> RunUninstallerAsync(string uninstallCommand);
}
