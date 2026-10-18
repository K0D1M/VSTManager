using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>One product bundle: several plugins the library shows as a single card.</summary>
public sealed record PluginBundle(string Key, string Name, string? Vendor, IReadOnlyList<PluginDisplayItem> Members, bool IsManual);

/// <summary>
/// Finds product bundles — e.g. SWAM's dozens of instruments or Roland's SRX expansions — so they
/// can be shown as one card. Automatic rule: same vendor and the same first word of the name
/// shared by at least <see cref="MinimumAutoSize"/> plugins, where that word isn't just part of the
/// vendor's own name (so "FabFilter Pro-Q", "FabFilter Saturn" … never merge). Hand-made bundles
/// win over automatic ones; plugins split out by hand are left alone. Pure: no I/O.
/// </summary>
public static class PluginBundleGrouper
{
    public const int MinimumAutoSize = 5;

    private static readonly char[] Separators = [' ', '-', '_'];

    public static List<PluginBundle> Group(IReadOnlyList<PluginDisplayItem> items,
                                           IReadOnlyList<PluginBundleMerge> manual,
                                           IReadOnlyCollection<string> exclusions)
    {
        var byBaseName = items
            .GroupBy(i => i.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new List<PluginBundle>();

        foreach (var merge in manual)
        {
            var members = merge.Members
                .Where(byBaseName.ContainsKey)
                .Where(taken.Add)
                .Select(m => byBaseName[m])
                .ToList();
            if (members.Count >= 2)
            {
                bundles.Add(new PluginBundle(merge.Key, merge.Name, CommonVendor(members), members, IsManual: true));
            }
        }

        var excluded = new HashSet<string>(exclusions, StringComparer.OrdinalIgnoreCase);
        var autoGroups = items
            .Where(i => !taken.Contains(i.BaseName) && !excluded.Contains(i.BaseName))
            .Where(i => !string.IsNullOrWhiteSpace(i.Vendor))
            .Select(i => (Item: i, Stem: Stem(i.Name)))
            .Where(x => x.Stem is not null && !IsVendorWord(x.Stem, x.Item.Vendor!))
            .GroupBy(x => (Vendor: x.Item.Vendor!.Trim().ToLowerInvariant(), Stem: x.Stem!.ToLowerInvariant()))
            .Where(g => g.Count() >= MinimumAutoSize);

        foreach (var group in autoGroups)
        {
            var members = group.Select(x => x.Item).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var first = group.First();
            bundles.Add(new PluginBundle(AutoKey(first.Item.Vendor!, first.Stem!), first.Stem!,
                first.Item.Vendor, members, IsManual: false));
        }

        return bundles;
    }

    /// <summary>The bundle key an automatic group gets — stable across scans.</summary>
    public static string AutoKey(string vendor, string stem) =>
        $"bundle:{vendor.Trim().ToLowerInvariant()}:{stem.ToLowerInvariant()}";

    public static bool IsBundleKey(string? baseName) =>
        baseName is not null && baseName.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first word of a name ("SWAM Violin 3" → "SWAM"). A hyphen only splits it when a model
    /// number follows ("TR-808" → "TR"): FabFilter's "Pro-Q", "Pro-C", "Pro-L" are separate
    /// products, and splitting at every hyphen bundled them as "Pro".
    /// </summary>
    private static string? Stem(string name)
    {
        var token = name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null)
        {
            return null;
        }

        var hyphen = token.IndexOf('-');
        if (hyphen > 0 && token[(hyphen + 1)..] is { Length: > 0 } model && model.All(char.IsDigit))
        {
            token = token[..hyphen];
        }

        return token.Length >= 2 ? token : null;
    }

    private static readonly char[] WordSeparators = [' ', '_'];

    private static bool IsVendorWord(string stem, string vendor) =>
        vendor.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, stem, StringComparison.OrdinalIgnoreCase))
        || vendor.Replace(" ", "").Equals(stem, StringComparison.OrdinalIgnoreCase);

    private static string? CommonVendor(IReadOnlyList<PluginDisplayItem> members)
    {
        var vendors = members.Select(m => m.Vendor).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return vendors.Count == 1 ? vendors[0] : null;
    }
}
