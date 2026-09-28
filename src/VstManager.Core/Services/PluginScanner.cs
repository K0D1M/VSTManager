using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginScanner
{
    private readonly ExclusionListService _exclusionList;
    private readonly VendorDetector _vendorDetector;
    private readonly PluginVersionDetector _versionDetector = new();

    public PluginScanner(ExclusionListService? exclusionList = null, VendorDetector? vendorDetector = null)
    {
        _exclusionList = exclusionList ?? new ExclusionListService();
        _vendorDetector = vendorDetector ?? new VendorDetector();
    }

    public List<PluginInfo> Scan(
        IEnumerable<string> vst3Folders,
        IEnumerable<string> vst2Folders,
        IEnumerable<string>? aaxFolders = null,
        IEnumerable<string>? clapFolders = null,
        IEnumerable<string>? auFolders = null)
    {
        // On macOS every format ships as a bundle folder, and VST2 is ".vst" rather than ".dll".
        var mac = OperatingSystem.IsMacOS();

        var results = new List<PluginInfo>();

        foreach (var folder in vst3Folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.vst3", PluginFormat.Vst3));
        }

        foreach (var folder in vst2Folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, mac ? "*.vst" : "*.dll", PluginFormat.Vst2));
        }

        // AAX ships as a ".aaxplugin" bundle folder with a same-named binary inside, exactly
        // like VST3, so the same bundle de-duplication applies.
        foreach (var folder in (aaxFolders ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.aaxplugin", PluginFormat.Aax));
        }

        // On Windows a CLAP plugin is a single ".clap" file (a renamed DLL).
        foreach (var folder in (clapFolders ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.clap", PluginFormat.Clap));
        }

        foreach (var folder in (auFolders ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.component", PluginFormat.Au));
        }

        return results;
    }

    private IEnumerable<PluginInfo> ScanFolder(string folder, string searchPattern, PluginFormat format)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        IEnumerable<string> entries;
        try
        {
            entries = OperatingSystem.IsMacOS()
                ? OutermostBundles(folder, searchPattern)
                : Directory.EnumerateFileSystemEntries(folder, searchPattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var path in OperatingSystem.IsMacOS() ? entries : DeduplicateBundles(entries))
        {
            if (_exclusionList.IsExcluded(path))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            yield return new PluginInfo
            {
                Name = name,
                Path = path,
                Format = format,

                // Read here, while the path is in hand, and persisted by LibraryStore so the
                // per-plugin file reads only happen for entries the library hasn't seen before.
                Vendor = _vendorDetector.Detect(path, name),

                // Read on every scan: a reinstall or update replaces the file at the same path,
                // so the version stored from an earlier scan can't be trusted to still be true.
                CurrentVersion = _versionDetector.DetectFromFile(path)
            };
        }
    }

    /// <summary>
    /// macOS: the bundle folder itself is the plugin, and anything nested inside one (a
    /// shell's inner components, helper bundles) is not a separate plugin. The extension is
    /// re-checked because a "*.vst" pattern must not pick up ".vst3". UNVERIFIED on a real Mac.
    /// </summary>
    private static List<string> OutermostBundles(string folder, string searchPattern)
    {
        var extension = searchPattern.TrimStart('*');
        var bundles = Directory.EnumerateDirectories(folder, searchPattern, SearchOption.AllDirectories)
            .Where(d => string.Equals(Path.GetExtension(d), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Length)
            .ToList();

        var kept = new List<string>();
        foreach (var bundle in bundles)
        {
            if (!kept.Any(outer => bundle.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                kept.Add(bundle);
            }
        }

        return kept;
    }

    private static IEnumerable<string> DeduplicateBundles(IEnumerable<string> entries)
    {
        // VST3 and AAX bundle folders (e.g. "Serum.vst3\") share the same extension as the binary
        // inside them (".vst3\Contents\<arch>\Serum.vst3"). Folders never count as plugins -
        // only the actual binary file does.
        return entries.Where(path => !Directory.Exists(path));
    }
}
