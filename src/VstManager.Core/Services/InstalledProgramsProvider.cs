using Microsoft.Win32;

namespace VstManager.Core.Services;

/// <summary>One entry from the Windows uninstall registry.</summary>
public record UninstallEntry(string DisplayName, string UninstallCommand, string? DisplayVersion);

/// <summary>
/// Reads the Windows "installed programs" list, used as the last-resort source for a plugin's
/// installed version when the plugin files themselves carry none.
///
/// This lives in Core rather than in the WPF app so both UI heads share one implementation.
/// Microsoft.Win32.Registry is available on plain net10.0; only the *reads* are Windows-only, and
/// they are guarded, so off Windows this simply reports nothing and the version chain ends one
/// step earlier. The matching half below is pure string work and runs everywhere.
/// </summary>
public class InstalledProgramsProvider
{
    private static readonly string[] RegistryPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    /// <summary>
    /// Finds the installed program whose display name matches a plugin. Pure and portable — the
    /// caller supplies the list, so this is the half that works on every platform.
    /// </summary>
    public static UninstallEntry? FindUninstaller(IReadOnlyList<UninstallEntry> installedPrograms, string pluginName, string? vendor)
    {
        var targetName = PluginNameMatcher.Normalize(pluginName);
        var targetVendor = vendor is null ? null : PluginNameMatcher.Normalize(vendor);

        foreach (var entry in installedPrograms)
        {
            var normalizedDisplayName = PluginNameMatcher.Normalize(entry.DisplayName);

            if (normalizedDisplayName.Contains(targetName) || targetName.Contains(normalizedDisplayName))
            {
                return entry;
            }

            if (targetVendor is not null &&
                normalizedDisplayName.Contains(targetVendor) &&
                normalizedDisplayName.Contains(targetName))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Every program registered as installed. Empty off Windows, and empty rather than throwing
    /// when the registry cannot be read — a version fallback is not worth failing a refresh over.
    /// </summary>
    public IReadOnlyList<UninstallEntry> EnumerateInstalledPrograms()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return EnumerateWindows().ToList();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<UninstallEntry> EnumerateWindows()
    {
        // Many plugin installers register per-user rather than machine-wide.
        var locations = RegistryPaths.Select(p => (Root: Registry.LocalMachine, Path: p))
            .Append((Root: Registry.CurrentUser, Path: RegistryPaths[0]));

        foreach (var (root, basePath) in locations)
        {
            using var baseKey = root.OpenSubKey(basePath);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                var uninstallString = subKey?.GetValue("UninstallString") as string;
                var displayVersion = subKey?.GetValue("DisplayVersion") as string;

                if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(uninstallString))
                {
                    yield return new UninstallEntry(displayName, uninstallString, string.IsNullOrWhiteSpace(displayVersion) ? null : displayVersion);
                }
            }
        }
    }
}
