using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginScanner
{
    public List<PluginInfo> Scan(IEnumerable<string> vst3Folders, IEnumerable<string> vst2Folders)
    {
        var results = new List<PluginInfo>();

        foreach (var folder in vst3Folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.vst3", PluginFormat.Vst3));
        }

        foreach (var folder in vst2Folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(ScanFolder(folder, "*.dll", PluginFormat.Vst2));
        }

        return results;
    }

    private static IEnumerable<PluginInfo> ScanFolder(string folder, string searchPattern, PluginFormat format)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder, searchPattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var path in entries)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            yield return new PluginInfo
            {
                Name = name,
                Path = path,
                Format = format
            };
        }
    }
}
