using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VstManager.Core.Services;

/// <summary>
/// Works out who makes a plugin by reading what's already on disk, rather than asking the web.
///
/// This matters more than it sounds: the online lookup can build a direct KVR product URL
/// ("/product/{name}-by-{vendor}") only when a vendor is known, and that path is both fast and
/// reliable. Without a vendor it falls back to public search engines, which rate-limit hard and
/// fail silently — so a missing vendor string is the difference between an instant answer and no
/// answer at all.
///
/// Sources are tried strongest-first, because they differ a lot in trustworthiness: the VST3
/// bundle's own manifest is vendor-authored and exact, the Windows version resource is usually
/// right but sometimes dirty or outright forged, and the containing folder is only a hint.
/// </summary>
public class VendorDetector
{
    /// <summary>
    /// Release groups, matched as substrings because they appear inside longer strings
    /// ("TEAM R2R", "R2R RELEASE"). Cracked plugins report the group here instead of the author —
    /// an installed Roland instrument on the test machine reports "TEAM R2R" in CompanyName.
    /// Kept deliberately short and distinctive: a substring rule is easy to over-apply, and
    /// wrongly rejecting a real vendor costs a lookup.
    /// </summary>
    private static readonly string[] CrackerMarkers =
    {
        "team r2r", "r2r", "audioutopia", "team air"
    };

    /// <summary>
    /// Placeholder values that carry no information. Matched exactly rather than as substrings,
    /// so a real vendor whose name merely contains one of these words survives.
    /// </summary>
    private static readonly string[] PlaceholderVendors =
    {
        "unknown", "n/a", "na", "none", "null", "vendor", "company", "wrapper", "todo", "test"
    };

    /// <summary>
    /// Names that differ from how the product database lists the same company, where no
    /// mechanical rule gets from one to the other. Each was verified against KVR's developer
    /// pages: the left-hand form 404s, the right-hand form resolves.
    ///
    /// Kept small on purpose — suffix stripping in <see cref="Sanitize"/> already covers the
    /// common shapes ("YAMAHA CORPORATION", "Synapse Audio Software"), and this list only exists
    /// for genuine renames and publisher/developer splits.
    /// </summary>
    private static readonly Dictionary<string, string> VendorAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // VST2 builds report a bare "XFER" in CompanyName; the VST3 bundles and KVR both say
        // "Xfer Records". Without this the same maker splits into two vendors.
        ["xfer"] = "Xfer Records",

        // Avenger's binary names its developer; it's published under Vengeance Sound.
        ["keilwerth audio"] = "Vengeance Sound"
    };

    /// <summary>
    /// Folder names that are scan roots or layout scaffolding rather than a vendor. Compared
    /// against the immediate parent folder before it's accepted as a vendor.
    /// </summary>
    private static readonly string[] NonVendorFolders =
    {
        "vst3", "vst2", "vst", "vstplugins", "steinberg", "common files",
        "program files", "program files (x86)", "plugins", "contents", "resources", "x86_64-win"
    };

    /// <summary>
    /// Reads the vendor for a plugin file, or null when nothing trustworthy is available.
    /// </summary>
    /// <param name="path">The scanned plugin path (bundle folder or the binary inside it).</param>
    /// <param name="pluginName">
    /// Used to reject a folder that just repeats the product name — "Pianoteq 9\Pianoteq 9.vst3"
    /// must not report a vendor of "Pianoteq 9" (it's Modartt).
    /// </param>
    public string? Detect(string path, string pluginName)
    {
        return DetectFromModuleInfo(path)
               ?? DetectFromVersionResource(path)
               ?? DetectFromVendorManifest(path)
               ?? DetectFromFolder(path, pluginName);
    }

    /// <summary>
    /// VST3 bundles carry a "moduleinfo.json" whose "Factory Info".Vendor is written by the
    /// plugin's own author — the single most reliable source available offline. Mirrors the
    /// probing in <see cref="PluginVersionDetector"/>, which already reads Version from the same
    /// file: location varies by SDK version, and real files contain comments and trailing commas
    /// that strict JSON rejects.
    /// </summary>
    private static string? DetectFromModuleInfo(string path)
    {
        var bundleRoot = PluginVersionDetector.FindBundleRoot(path);
        if (bundleRoot is null)
        {
            return null;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(bundleRoot, "Contents", "moduleinfo.json"),
                     Path.Combine(bundleRoot, "Contents", "Resources", "moduleinfo.json")
                 })
        {
            var vendor = ReadModuleInfoVendor(candidate);
            if (vendor is not null)
            {
                return vendor;
            }
        }

        return null;
    }

    private static string? ReadModuleInfoVendor(string moduleInfoPath)
    {
        try
        {
            if (!File.Exists(moduleInfoPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                File.ReadAllText(moduleInfoPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Spec spells it "Factory Info" / "Vendor"; matched case-insensitively for safety.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "Factory Info", StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in property.Value.EnumerateObject())
                {
                    if (string.Equals(field.Name, "Vendor", StringComparison.OrdinalIgnoreCase)
                        && field.Value.ValueKind == JsonValueKind.String)
                    {
                        return Sanitize(field.Value.GetString());
                    }
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Windows version resource's CompanyName. Usually correct, but needs cleaning — Serum
    /// reports "XFER  / steve@xferrecords.com " — and needs the blocklist, since a cracked
    /// plugin reports its release group here.
    /// </summary>
    private static string? DetectFromVersionResource(string path)
    {
        try
        {
            return Sanitize(FileVersionInfo.GetVersionInfo(path).CompanyName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or ArgumentException)
        {
            // Handing this a bundle *directory* rather than a binary throws — same guard as
            // PluginVersionDetector's version read.
            return null;
        }
    }

    /// <summary>
    /// Universal Audio's plugins ship no version resource and no moduleinfo.json; their bundle
    /// manifest is already parsed for the version, and carries a vendor alongside it.
    /// </summary>
    private static string? DetectFromVendorManifest(string path)
    {
        var bundleRoot = PluginVersionDetector.FindBundleRoot(path);
        if (bundleRoot is null)
        {
            return null;
        }

        var manifestPath = Path.Combine(bundleRoot, "Contents", "Resources", "manifest.json");

        try
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                File.ReadAllText(manifestPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "vendor", "manufacturer", "company" })
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Sanitize(value.GetString()) is { } vendor)
                {
                    return vendor;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The containing folder, which on this kind of install is often the vendor
    /// ("VST3\Roland\EARTH Piano.vst3"). Only a hint though — roughly half of real folders are
    /// product-named instead — so it's accepted only when it can't be one of those, and only
    /// after every authored source above has declined.
    /// </summary>
    private static string? DetectFromFolder(string path, string pluginName)
    {
        // For a bundle, step up past Contents/<arch> to the .vst3 folder before taking a parent.
        var bundleRoot = PluginVersionDetector.FindBundleRoot(path);
        var folder = Path.GetDirectoryName(bundleRoot ?? path);
        var candidate = Path.GetFileName(folder);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (NonVendorFolders.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalizedFolder = PluginNameMatcher.Normalize(candidate);
        var normalizedPlugin = PluginNameMatcher.Normalize(pluginName);

        // A folder that repeats or extends the product name is product-named, not vendor-named:
        // "Pianoteq 9\Pianoteq 9.vst3" would otherwise report a vendor of "Pianoteq 9" (Modartt).
        //
        // The reverse containment is deliberately NOT rejected. "FabFilter\FabFilter Pro-Q 4"
        // is the single most common vendor-folder layout there is — the plugin name carrying the
        // folder as a prefix is evidence the folder is the vendor, not evidence against it.
        if (normalizedFolder.Length == 0
            || normalizedFolder == normalizedPlugin
            || normalizedFolder.Contains(normalizedPlugin, StringComparison.Ordinal))
        {
            return null;
        }

        // "v2", "1.4.5", "x64" and friends are layout, not a company.
        if (Regex.IsMatch(candidate, @"^v?\d+(\.\d+)*$") || normalizedFolder.All(char.IsDigit))
        {
            return null;
        }

        return Sanitize(candidate);
    }

    /// <summary>
    /// Normalises a raw vendor string and rejects the ones that name someone other than the
    /// author. Returns null when nothing usable survives, so callers can fall through.
    /// </summary>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var working = raw.Trim();

        // Contact details tacked onto the company name: "XFER  / steve@xferrecords.com".
        var separatorIndex = working.IndexOfAny(new[] { '/', '|', ';' });
        if (separatorIndex > 0)
        {
            working = working[..separatorIndex];
        }

        working = Regex.Replace(working, @"\S+@\S+", string.Empty);
        working = Regex.Replace(working, @"https?://\S+", string.Empty);

        // Corporate suffixes that the product database drops: "YAMAHA CORPORATION" is listed as
        // "Yamaha", "Synapse Audio Software" as "Synapse Audio". Applied repeatedly so a trailing
        // pair ("... Software Inc.") collapses fully.
        for (var i = 0; i < 3; i++)
        {
            var trimmed = Regex.Replace(
                working,
                @"[,.]?\s*\b(incorporated|inc|ltd|limited|llc|gmbh|corporation|corp|company|software|s\.?a\.?s|b\.?v|ab|oy|kg|ug)\b\.?$",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();

            if (trimmed == working || trimmed.Length < 2)
            {
                break;
            }

            working = trimmed;
        }

        working = Regex.Replace(working, @"\s+", " ").Trim().Trim(',', '.', '-', '_');

        if (working.Length < 2)
        {
            return null;
        }

        var comparable = working.ToLowerInvariant();
        if (PlaceholderVendors.Contains(comparable, StringComparer.Ordinal)
            || CrackerMarkers.Any(marker => comparable.Contains(marker, StringComparison.Ordinal)))
        {
            return null;
        }

        return VendorAliases.TryGetValue(comparable, out var canonical) ? canonical : working;
    }
}
