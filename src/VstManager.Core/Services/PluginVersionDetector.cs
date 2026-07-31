using System.Diagnostics;
using System.Text.Json;

namespace VstManager.Core.Services;

public class PluginVersionDetector
{
    /// <summary>
    /// Reads the installed version of a plugin file. Tries the Windows version resource first
    /// (present on most plugins), then falls back to the VST3 bundle's own metadata — a
    /// meaningful number of plugins ship with no version resource at all, including whole
    /// vendor families like Universal Audio's UADx range, which would otherwise always show
    /// a blank version.
    /// </summary>
    public string? DetectFromFile(string path)
    {
        return DetectFromVersionResource(path)
               ?? DetectFromVst3ModuleInfo(path)
               ?? DetectFromVendorManifest(path);
    }

    private static string? DetectFromVersionResource(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// VST3 bundles carry a "moduleinfo.json" describing the module, including its Version
    /// (VST3 SDK 3.7.5+). Its location varies between Contents/ and Contents/Resources/
    /// depending on the SDK version the vendor built against, so both are checked.
    /// </summary>
    private static string? DetectFromVst3ModuleInfo(string path)
    {
        var bundleRoot = FindBundleRoot(path);
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
            var version = ReadModuleInfoVersion(candidate);
            if (version is not null)
            {
                return version;
            }
        }

        return null;
    }

    private static string? ReadModuleInfoVersion(string moduleInfoPath)
    {
        try
        {
            if (!File.Exists(moduleInfoPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                File.ReadAllText(moduleInfoPath),
                // Real-world moduleinfo.json files are hand-maintained and commonly contain
                // comments and trailing commas, which strict JSON rejects.
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The spec capitalises it "Version"; compare case-insensitively for safety.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Universal Audio's UADx plugins ship no version resource and no moduleinfo.json, so
    /// their version lives only in the vendor's own bundle manifest at
    /// Contents/Resources/manifest.json, under algo_manifest.&lt;plugin_id&gt;.version.
    /// Verified against the installed UADx range (e.g. PolyMAX Synth reports 1.0.16, matching
    /// the version published on KVR).
    /// </summary>
    private static string? DetectFromVendorManifest(string path)
    {
        var bundleRoot = FindBundleRoot(path);
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

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("algo_manifest", out var algoManifest)
                || algoManifest.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Keyed by plugin_id; a bundle normally holds exactly one, so prefer the entry
            // matching plugin_id and otherwise fall back to the single entry present.
            var preferredId = document.RootElement.TryGetProperty("plugin_id", out var pluginId)
                              && pluginId.ValueKind == JsonValueKind.String
                ? pluginId.GetString()
                : null;

            JsonElement? chosen = null;
            foreach (var entry in algoManifest.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (preferredId is not null && string.Equals(entry.Name, preferredId, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = entry.Value;
                    break;
                }

                chosen ??= entry.Value;
            }

            if (chosen is null
                || !chosen.Value.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = version.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks up from a binary inside a VST3 bundle to the ".vst3" bundle directory itself.
    /// The scanned path may be either the bundle folder or the nested binary
    /// (e.g. "X.vst3\Contents\x86_64-win\X.vst3"), so this handles both.
    /// </summary>
    public static string? FindBundleRoot(string path)
    {
        var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        // Depth-limited: a bundle binary sits at Contents/<arch>/, so the root is at most a
        // few levels up. Stops the walk from wandering into unrelated parent folders.
        for (var depth = 0; depth < 4 && !string.IsNullOrEmpty(current); depth++)
        {
            if (current.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) && Directory.Exists(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
