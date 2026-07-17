using System.Text;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginNameMatcher
{
    private static readonly string[] SuffixesToStrip = { "x64", "x86", "vst3", "vst2", "vst" };

    private const int MinVendorPrefixMatchLength = 4;

    public CatalogEntry? FindMatch(string foundName, IEnumerable<CatalogEntry> catalog)
    {
        var normalizedFound = Normalize(foundName);
        if (normalizedFound.Length == 0)
        {
            return null;
        }

        var catalogList = catalog as ICollection<CatalogEntry> ?? catalog.ToList();

        var exactMatch = catalogList.FirstOrDefault(entry => Normalize(entry.Name) == normalizedFound);
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        // Known alternate filenames that don't resemble the product name (e.g. abbreviated
        // internal names like "uaudio_ua_1176" for the "1176 Classic Limiter Collection").
        // Matched as a prefix so per-model variants (e.g. "uaudio_ua_1176se", "...ln",
        // "...ae") sharing the same internal family name all resolve to the same entry.
        var aliasMatch = catalogList
            .Where(entry => entry.Aliases.Any(alias =>
            {
                var normalizedAlias = Normalize(alias);
                return normalizedAlias.Length > 0 && normalizedFound.StartsWith(normalizedAlias, StringComparison.Ordinal);
            }))
            .OrderByDescending(entry => entry.Aliases.Max(alias => Normalize(alias).Length))
            .FirstOrDefault();
        if (aliasMatch is not null)
        {
            return aliasMatch;
        }

        // Handles installed filenames that bake the vendor name in as a prefix,
        // e.g. "FabFilter Saturn 2.vst3" matching the catalog entry "Saturn 2".
        var vendorPrefixMatch = catalogList
            .Where(entry =>
            {
                var normalizedEntry = Normalize(entry.Name);
                return normalizedEntry.Length >= MinVendorPrefixMatchLength
                       && normalizedFound.EndsWith(normalizedEntry, StringComparison.Ordinal)
                       && normalizedFound.Length > normalizedEntry.Length;
            })
            .OrderByDescending(entry => Normalize(entry.Name).Length)
            .FirstOrDefault();

        if (vendorPrefixMatch is not null)
        {
            return vendorPrefixMatch;
        }

        // Handles versioned installs matching an unversioned catalog entry,
        // e.g. "Absynth 6" matching "Absynth". The remainder after the entry name
        // must be purely numeric (optionally "v"-prefixed) so that e.g. "MassiveX"
        // never matches "Massive".
        return catalogList
            .Where(entry =>
            {
                var normalizedEntry = Normalize(entry.Name);
                if (normalizedEntry.Length < MinVendorPrefixMatchLength
                    || !normalizedFound.StartsWith(normalizedEntry, StringComparison.Ordinal)
                    || normalizedFound.Length <= normalizedEntry.Length)
                {
                    return false;
                }

                var remainder = normalizedFound[normalizedEntry.Length..];
                if (remainder.StartsWith('v') && remainder.Length > 1)
                {
                    remainder = remainder[1..];
                }

                return remainder.All(char.IsDigit);
            })
            .OrderByDescending(entry => Normalize(entry.Name).Length)
            .FirstOrDefault();
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
