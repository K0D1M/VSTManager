using System.Text.RegularExpressions;

namespace VstManager.Core.Services;

public static class VersionComparer
{
    /// <summary>
    /// True only when both strings parse as versions and latest is strictly greater. No
    /// string-inequality fallback (unlike the app-update checker): this drives the OUTDATED
    /// badge, where a format mismatch between a scraped web version and a file-detected one
    /// must never produce a false positive.
    /// </summary>
    public static bool IsNewer(string? latest, string? current)
    {
        var latestParsed = TryParse(latest);
        var currentParsed = TryParse(current);

        return latestParsed is not null && currentParsed is not null && latestParsed > currentParsed;
    }

    private static Version? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('v', 'V').Trim();

        // Windows file-version metadata is frequently comma-separated ("1,3,6,8") rather
        // than dotted, since that's how the VERSIONINFO resource is authored. Without this
        // those versions never parse, so the plugin can never be flagged outdated.
        trimmed = trimmed.Replace(',', '.');

        // Take only the leading numeric-dotted run, dropping trailing qualifiers like
        // "2.8.7c" or "5.5.4 64 bit". This deliberately ignores the suffix rather than
        // trying to order it: "2.8.7c" and "2.8.7" compare equal, so a build-letter
        // difference alone never produces a false OUTDATED flag.
        var match = Regex.Match(trimmed, @"^\d+(\.\d+)*");
        if (!match.Success)
        {
            return null;
        }

        trimmed = match.Value;

        // Version.TryParse needs at least major.minor; pad a bare "4" to "4.0".
        if (!trimmed.Contains('.'))
        {
            trimmed += ".0";
        }

        // Version caps at four components; extra segments are noise for this comparison.
        var parts = trimmed.Split('.');
        if (parts.Length > 4)
        {
            trimmed = string.Join('.', parts.Take(4));
        }

        return Version.TryParse(trimmed, out var parsed) ? Normalize(parsed) : null;
    }

    /// <summary>Pads undefined components to 0 so "4.13" and "4.13.0" compare as equal.</summary>
    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));
}
