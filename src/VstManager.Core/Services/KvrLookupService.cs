using System.Net;
using System.Text.RegularExpressions;

namespace VstManager.Core.Services;

public record KvrLookupResult(string ProductName, string Vendor, string? LogoUrl);

/// <summary>
/// Best-effort live lookup against KVR Audio's product database, used as a fallback when a
/// plugin isn't in the local catalog. KVR's own site search ("Quick Search") requires being
/// logged into a KVR account even to view results, so a bare HttpClient with no session can
/// never use it directly. Instead this searches via DuckDuckGo's login-free HTML endpoint
/// restricted to kvraudio.com/product pages, then fetches and scrapes the top real KVR result
/// with plain regexes — no HTML parser dependency, but inherently fragile: if KVR or DuckDuckGo
/// change their page layout, this silently starts returning null (never throws out to the
/// caller) rather than breaking anything.
/// </summary>
public class KvrLookupService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public virtual async Task<KvrLookupResult?> SearchAsync(string pluginName)
    {
        try
        {
            var query = "site:kvraudio.com/product " + pluginName;
            var searchUrl = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            var searchHtml = await Client.GetStringAsync(searchUrl);

            var productUrl = ExtractFirstProductLink(searchHtml);
            if (productUrl is null)
            {
                return null;
            }

            var productHtml = await Client.GetStringAsync(productUrl);

            return ParseProductPage(productHtml);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// DuckDuckGo's HTML-only results wrap each link in a redirect, e.g.
    /// "//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2F...&amp;rut=...".
    /// Scans every result in order and returns the first one that's a real KVR product page
    /// (the site: filter usually guarantees this, but isn't airtight).
    /// </summary>
    public static string? ExtractFirstProductLink(string searchHtml)
    {
        foreach (Match match in Regex.Matches(searchHtml, "uddg=([^&\"']+)", RegexOptions.IgnoreCase))
        {
            var decoded = Uri.UnescapeDataString(match.Groups[1].Value);
            if (decoded.Contains("kvraudio.com/product/", StringComparison.OrdinalIgnoreCase))
            {
                return decoded;
            }
        }

        return null;
    }

    /// <summary>
    /// KVR product page titles follow the pattern "{Product} by {Vendor} - {category/type text}",
    /// e.g. "Pro-Q 4 by FabFilter - EQ Plugin VST VST3 Audio Unit AAX CLAP".
    /// </summary>
    public static KvrLookupResult? ParseProductPage(string productHtml)
    {
        var titleMatch = Regex.Match(productHtml, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!titleMatch.Success)
        {
            return null;
        }

        var title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
        var byIndex = title.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIndex < 0)
        {
            return null;
        }

        var productName = title[..byIndex].Trim();
        var afterBy = title[(byIndex + 4)..].Trim();
        var dashIndex = afterBy.IndexOf(" - ", StringComparison.Ordinal);
        var vendor = (dashIndex >= 0 ? afterBy[..dashIndex] : afterBy).Trim();

        if (string.IsNullOrWhiteSpace(productName) || string.IsNullOrWhiteSpace(vendor))
        {
            return null;
        }

        var logoMatch = Regex.Match(productHtml, "https://static\\.kvraudio\\.com/i/[a-z]/[^\"'\\s]+\\.(jpg|jpeg|png|webp)", RegexOptions.IgnoreCase);
        var logoUrl = logoMatch.Success ? logoMatch.Value : null;

        return new KvrLookupResult(productName, vendor, logoUrl);
    }
}
