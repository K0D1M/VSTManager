using System.Xml.Linq;

namespace VstManager.Core.Services;

/// <summary>
/// Reads the few fields VST Manager needs from a macOS bundle's <c>Contents/Info.plist</c>.
/// Only XML plists are handled; a binary plist ("bplist00") reads as null, and the caller falls
/// back to its other sources. UNVERIFIED on a real Mac.
/// </summary>
public static class BundleInfoPlist
{
    public sealed record Info(string? ShortVersion, string? BundleVersion, string? AudioComponentName);

    public static Info? Read(string bundleRoot)
    {
        var path = Path.Combine(bundleRoot, "Contents", "Info.plist");

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using (var probe = File.OpenRead(path))
            {
                var magic = new byte[6];
                if (probe.Read(magic, 0, 6) == 6 && System.Text.Encoding.ASCII.GetString(magic) == "bplist")
                {
                    return null;
                }
            }

            var root = XDocument.Load(path, LoadOptions.None).Root?.Element("dict");
            if (root is null)
            {
                return null;
            }

            var top = ReadDict(root);

            // AU bundles describe each component as "Vendor: Product" in AudioComponents.
            string? componentName = null;
            if (top.TryGetValue("AudioComponents", out var components)
                && components is XElement { Name.LocalName: "array" } array)
            {
                var first = array.Elements("dict").FirstOrDefault();
                if (first is not null && ReadDict(first).TryGetValue("name", out var name) && name is string s)
                {
                    componentName = s;
                }
            }

            return new Info(
                top.GetValueOrDefault("CFBundleShortVersionString") as string,
                top.GetValueOrDefault("CFBundleVersion") as string,
                componentName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Plist dicts are flat key/value sibling pairs; strings come back as string, the rest as elements.</summary>
    private static Dictionary<string, object> ReadDict(XElement dict)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var children = dict.Elements().ToList();

        for (var i = 0; i + 1 < children.Count; i += 2)
        {
            if (children[i].Name.LocalName != "key")
            {
                continue;
            }

            var value = children[i + 1];
            result[children[i].Value.Trim()] = value.Name.LocalName == "string" ? value.Value.Trim() : value;
        }

        return result;
    }
}
