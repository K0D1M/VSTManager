using System.Text;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginNameMatcher
{
    private static readonly string[] SuffixesToStrip = { "x64", "x86", "vst3", "vst2", "vst" };

    public CatalogEntry? FindMatch(string foundName, IEnumerable<CatalogEntry> catalog)
    {
        var normalizedFound = Normalize(foundName);
        if (normalizedFound.Length == 0)
        {
            return null;
        }

        return catalog.FirstOrDefault(entry => Normalize(entry.Name) == normalizedFound);
    }

    public static string Normalize(string name)
    {
        var lower = name.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);

        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        foreach (var suffix in SuffixesToStrip)
        {
            if (result.EndsWith(suffix, StringComparison.Ordinal) && result.Length > suffix.Length)
            {
                result = result[..^suffix.Length];
            }
        }

        return result;
    }
}
