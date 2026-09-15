using VstManager.Core.Services;

namespace VstManager.App.Services;

/// <summary>
/// The registry-backed installed-programs lookup. The implementation now lives in Core as
/// <see cref="InstalledProgramsProvider"/> so the Avalonia head shares it rather than carrying a
/// second copy; this stays as the WPF app's existing entry point so its call sites are unchanged.
/// </summary>
public class UninstallerLookup
{
    private readonly InstalledProgramsProvider _provider = new();

    public UninstallEntry? FindUninstaller(string pluginName, string? vendor) =>
        FindUninstaller(EnumerateInstalledPrograms().ToList(), pluginName, vendor);

    public static UninstallEntry? FindUninstaller(IReadOnlyList<UninstallEntry> installedPrograms, string pluginName, string? vendor) =>
        InstalledProgramsProvider.FindUninstaller(installedPrograms, pluginName, vendor);

    public IEnumerable<UninstallEntry> EnumerateInstalledPrograms() => _provider.EnumerateInstalledPrograms();
}
