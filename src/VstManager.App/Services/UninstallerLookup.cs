using Microsoft.Win32;
using VstManager.Core.Services;

namespace VstManager.App.Services;

public record UninstallEntry(string DisplayName, string UninstallCommand);

public class UninstallerLookup
{
    private static readonly string[] RegistryPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public UninstallEntry? FindUninstaller(string pluginName, string? vendor)
    {
        var targetName = PluginNameMatcher.Normalize(pluginName);
        var targetVendor = vendor is null ? null : PluginNameMatcher.Normalize(vendor);

        foreach (var entry in EnumerateInstalledPrograms())
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

    private static IEnumerable<UninstallEntry> EnumerateInstalledPrograms()
    {
        foreach (var basePath in RegistryPaths)
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                var uninstallString = subKey?.GetValue("UninstallString") as string;

                if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(uninstallString))
                {
                    yield return new UninstallEntry(displayName, uninstallString);
                }
            }
        }
    }
}
