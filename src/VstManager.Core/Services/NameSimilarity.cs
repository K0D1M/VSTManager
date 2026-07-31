namespace VstManager.Core.Services;

/// <summary>
/// Scores how closely a candidate product name matches the name scanned off disk, so
/// auto-detect can tell a confident hit ("Pro-Q 4" vs "FabFilter Pro-Q 4") from a coincidental
/// one ("Serum" vs "Serum Presets Expansion Vol. 3"). Deliberately conservative: anything below
/// the confident threshold is surfaced to the user to choose rather than applied silently.
/// </summary>
public static class NameSimilarity
{
    /// <summary>At or above this, a single candidate can be applied without asking.</summary>
    public const double ConfidentThreshold = 0.82;

    /// <summary>Below this a candidate is treated as noise and not offered at all.</summary>
    public const double PlausibleThreshold = 0.35;

    /// <summary>
    /// Returns 0..1. Combines whole-string edit distance with token containment so that a
    /// candidate which fully contains the scanned name (a very common real pattern, e.g.
    /// scanned "Diva" vs listed "Diva by u-he") still scores high, while a candidate that
    /// merely shares a word with lots of extra terms is penalised for the extra terms.
    /// </summary>
    public static double Score(string scannedName, string candidateName, string? candidateVendor = null)
    {
        var a = PluginNameMatcher.Normalize(scannedName ?? string.Empty);
        var b = PluginNameMatcher.Normalize(candidateName ?? string.Empty);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var direct = Ratio(a, b);

        // Installed filenames very often bake the vendor in ("FabFilter Pro-Q 4.vst3" for the
        // product "Pro-Q 4"), so also compare with the vendor stripped off the scanned name.
        if (!string.IsNullOrWhiteSpace(candidateVendor))
        {
            var vendorNorm = PluginNameMatcher.Normalize(candidateVendor);
            if (vendorNorm.Length > 0 && a.StartsWith(vendorNorm, StringComparison.Ordinal) && a.Length > vendorNorm.Length)
            {
                direct = Math.Max(direct, Ratio(a[vendorNorm.Length..], b));
            }

            // ...and the mirror case, where the candidate title carries the vendor.
            if (vendorNorm.Length > 0 && b.StartsWith(vendorNorm, StringComparison.Ordinal) && b.Length > vendorNorm.Length)
            {
                direct = Math.Max(direct, Ratio(a, b[vendorNorm.Length..]));
            }
        }

        return direct;
    }

    private static double Ratio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        // Containment is strong evidence, but scale it by how much extra text the longer side
        // carries: "serum" inside "serum2" is near-certain, inside a long preset-pack title is not.
        if (b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal))
        {
            var shorter = Math.Min(a.Length, b.Length);
            var longer = Math.Max(a.Length, b.Length);
            return 0.60 + 0.40 * ((double)shorter / longer);
        }

        var distance = Levenshtein(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return Math.Max(0, 1.0 - (double)distance / maxLength);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
