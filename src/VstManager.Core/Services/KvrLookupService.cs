using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace VstManager.Core.Services;

public record KvrLookupResult(string ProductName, string Vendor, string? LogoUrl, string? LatestVersion, string? SourceUrl = null);

/// <summary>A candidate match plus how well its name matches the plugin found on disk (0..1).</summary>
public record PluginInfoCandidate(KvrLookupResult Info, double Confidence);

/// <summary>
/// Best-effort live lookup against KVR Audio's product database. KVR's own site search
/// ("Quick Search") requires being logged into a KVR account even to view results, and
/// public search engines are unreliable from plain HTTP clients (bot walls, and some
/// networks block them outright) — so the primary strategy skips searching entirely:
/// KVR product URLs are predictable ("/product/{name}-by-{vendor}") and KVR 301-corrects
/// near-miss slugs (e.g. "serum-by-xfer-records" redirects to the real
/// "serum-2-by-xfer-records" page), while a miss redirects to the generic /plugins/ page.
///
/// A search-engine fallback is used only when no vendor is known (so the direct URL can't be
/// guessed). Multiple engines are tried in order and the first that returns a usable KVR
/// product link wins — search-engine access is inherently unreliable per-network (DuckDuckGo
/// has been observed both bot-walling requests and, on some networks, refusing the TCP
/// connection entirely — indistinguishable from an IP-level block from this side). Engines
/// known NOT to work from a bare HTTP client and excluded: Mojeek (CAPTCHAs immediately),
/// Bing (its no-JS HTML response carries no real results, just a locale-redirect shell).
/// Startpage requires a session-establishing redirect a plain client can't follow.
///
/// All paths scrape with plain regexes and fail quietly (null) rather than ever throwing.
///
/// Fetching goes through the OS's own curl.exe (bundled with Windows 10 1803+) rather than
/// HttpClient directly. KVR sits behind Cloudflare bot management that keys off the TLS/HTTP
/// client fingerprint rather than headers: confirmed live that identical requests (same
/// machine, same User-Agent) get a hard 403 "Just a moment..." challenge from .NET's
/// HttpClient every time, while curl.exe gets a normal 200 every time. Shelling out to curl
/// sidesteps that fingerprint. HttpClient remains a fallback for the rare machine without
/// curl.exe on PATH (very old Windows, or a stripped-down image).
/// </summary>
public class KvrLookupService
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// Each entry builds a search URL for "site:kvraudio.com/product {query}" and extracts
    /// the first matching product link from that engine's HTML. Tried in order; first hit
    /// wins. Order reflects observed reliability from a bare HTTP client: Brave's static HTML
    /// response (no JS challenge) first, DuckDuckGo's HTML-only endpoint second since it still
    /// works on many networks even where it's flaky on others.
    /// </summary>
    private static readonly (Func<string, string> BuildUrl, Func<string, string?> ExtractLink)[] SearchEngines =
    {
        (query => "https://search.brave.com/search?q=" + Uri.EscapeDataString(query), ExtractFirstKvrLink),
        (query => "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query), ExtractFirstDuckDuckGoLink)
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>Minimum gap between consecutive search-engine hits, app-wide.</summary>
    private static readonly TimeSpan SearchThrottle = TimeSpan.FromMilliseconds(1200);

    /// <summary>How long to stop using an engine after it rate-limits us.</summary>
    private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(10);

    private static readonly SemaphoreSlim SearchGate = new(1, 1);
    private static readonly Dictionary<string, DateTime> EngineCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastSearchAt = DateTime.MinValue;

    /// <summary>
    /// Serialises and paces search-engine requests. Engines rate-limit aggressively (Brave
    /// returns HTTP 429 and a CAPTCHA page after a burst), and once tripped they stay hostile
    /// for a while — which would silently poison every later lookup. So requests are spaced
    /// out, and an engine that 429s is benched for a cooldown rather than retried.
    /// </summary>
    private static async Task<FetchResult?> ThrottledSearchFetchAsync(string engineKey, string url)
    {
        await SearchGate.WaitAsync();
        try
        {
            if (EngineCooldownUntil.TryGetValue(engineKey, out var until) && DateTime.UtcNow < until)
            {
                return null;
            }

            var sinceLast = DateTime.UtcNow - _lastSearchAt;
            if (sinceLast < SearchThrottle)
            {
                await Task.Delay(SearchThrottle - sinceLast);
            }

            _lastSearchAt = DateTime.UtcNow;
            var fetch = await FetchAsync(url);

            if (fetch is not null && fetch.Value.StatusCode is 429 or 503)
            {
                EngineCooldownUntil[engineKey] = DateTime.UtcNow + RateLimitCooldown;
                return null;
            }

            return fetch;
        }
        finally
        {
            SearchGate.Release();
        }
    }

    public virtual async Task<KvrLookupResult?> SearchAsync(string pluginName, string? vendor = null)
    {
        if (!string.IsNullOrWhiteSpace(vendor))
        {
            var direct = await TryDirectProductPageAsync(pluginName, vendor);
            if (direct is not null)
            {
                return direct;
            }
        }

        var query = "site:kvraudio.com/product " + pluginName;

        foreach (var (buildUrl, extractLink) in SearchEngines)
        {
            try
            {
                var engineUrl = buildUrl(query);
                var searchFetch = await ThrottledSearchFetchAsync(EngineKey(engineUrl), engineUrl);
                if (searchFetch is null)
                {
                    continue;
                }

                var productUrl = extractLink(searchFetch.Value.Body);
                if (productUrl is null)
                {
                    continue;
                }

                var productFetch = await FetchAsync(productUrl);
                var result = productFetch is null ? null : ParseProductPage(productFetch.Value.Body);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                // This engine failed outright (network-level block, timeout, etc.) — move on
                // to the next one rather than giving up the whole lookup.
            }
        }

        return null;
    }

    /// <summary>
    /// Gathers several plausible matches instead of committing to the first hit, so the caller
    /// can either apply a confident one automatically or ask the user to choose. Tries, in
    /// order: the direct URL guess (near-certain when it resolves), then search results across
    /// the engine chain. Results are de-duplicated by source URL, scored against the scanned
    /// name, filtered to plausible ones, and returned best-first.
    /// </summary>
    public virtual async Task<IReadOnlyList<PluginInfoCandidate>> SearchCandidatesAsync(
        string pluginName, string? vendor = null, int maxCandidates = 5)
    {
        var candidates = new List<PluginInfoCandidate>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(KvrLookupResult? info, double confidenceBonus = 0)
        {
            if (info is null || (info.SourceUrl is not null && !seenUrls.Add(info.SourceUrl)))
            {
                return;
            }

            var score = Math.Min(1.0, NameSimilarity.Score(pluginName, info.ProductName, info.Vendor) + confidenceBonus);
            if (score >= NameSimilarity.PlausibleThreshold)
            {
                candidates.Add(new PluginInfoCandidate(info, score));
            }
        }

        bool HaveConfidentHit() => candidates.Any(c => c.Confidence >= NameSimilarity.ConfidentThreshold);

        // A resolving direct URL guess is strong evidence in itself — KVR only serves that
        // path if the name+vendor slug really exists — so it gets a confidence bump.
        if (!string.IsNullOrWhiteSpace(vendor))
        {
            Add(await TryDirectProductPageAsync(pluginName, vendor), confidenceBonus: 0.15);
        }

        // Every extra candidate costs a page fetch, so stop as soon as the answer is already
        // unambiguous. Without this, a full sweep of query variants x results takes long
        // enough to be unusable when run across a whole library.
        foreach (var query in BuildSearchQueries(pluginName, vendor))
        {
            if (HaveConfidentHit() || candidates.Count >= maxCandidates)
            {
                break;
            }

            foreach (var url in await SearchProductUrlsAsync(query, maxCandidates))
            {
                if (seenUrls.Contains(url))
                {
                    continue;
                }

                var fetch = await FetchAsync(url);
                if (fetch is null || fetch.Value.StatusCode is < 200 or >= 300)
                {
                    continue;
                }

                Add(ParseAnyProductPage(fetch.Value.Body, fetch.Value.FinalUrl ?? url));

                if (HaveConfidentHit() || candidates.Count >= maxCandidates)
                {
                    break;
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Confidence)
            .Take(maxCandidates)
            .ToList();
    }

    /// <summary>
    /// Builds progressively looser search queries. Installed filenames carry noise the product
    /// name never has (vendor prefixes, "x64", trailing version numbers), so a single literal
    /// query misses often — trying the cleaned variants materially improves hit rate.
    /// </summary>
    public static IReadOnlyList<string> BuildSearchQueries(string pluginName, string? vendor)
    {
        var queries = new List<string>();

        void Add(string? candidate)
        {
            var trimmed = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)
                && !queries.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(trimmed);
            }
        }

        var cleaned = CleanNameForSearch(pluginName);

        if (!string.IsNullOrWhiteSpace(vendor))
        {
            Add($"{cleaned} {vendor}");
        }

        Add(cleaned);

        // Drop a trailing version number ("Kontakt 8" -> "Kontakt"): products are frequently
        // listed under the unversioned name on databases.
        var withoutTrailingNumber = Regex.Replace(cleaned, @"\s+v?\d+(\.\d+)*$", string.Empty).Trim();
        Add(withoutTrailingNumber);

        return queries;
    }

    /// <summary>Strips filename noise ("_x64", ".vst3", underscores) that never appears in a product listing.</summary>
    public static string CleanNameForSearch(string name)
    {
        var working = Regex.Replace(name ?? string.Empty, @"\.(vst3|dll|component)$", string.Empty, RegexOptions.IgnoreCase);
        working = working.Replace('_', ' ').Replace('-', ' ');
        working = Regex.Replace(working, @"\b(x64|x86|win64|win32|win|vst3|vst2|vst)\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(working, @"\s+", " ").Trim();
    }

    /// <summary>Cooldowns are tracked per engine host, so benching Brave doesn't bench DuckDuckGo.</summary>
    private static string EngineKey(string engineUrl) =>
        Uri.TryCreate(engineUrl, UriKind.Absolute, out var uri) ? uri.Host : engineUrl;

    /// <summary>Runs the engine chain for one query and returns every distinct KVR product URL found.</summary>
    private static async Task<IReadOnlyList<string>> SearchProductUrlsAsync(string query, int max)
    {
        var siteQuery = "site:kvraudio.com/product " + query;

        foreach (var (buildUrl, _) in SearchEngines)
        {
            try
            {
                var engineUrl = buildUrl(siteQuery);
                var fetch = await ThrottledSearchFetchAsync(EngineKey(engineUrl), engineUrl);
                if (fetch is null)
                {
                    continue;
                }

                var urls = ExtractAllProductLinks(fetch.Value.Body).Take(max).ToList();
                if (urls.Count > 0)
                {
                    return urls;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                // Try the next engine.
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Pulls every distinct KVR product URL out of a results page, handling both link shapes
    /// (Brave's direct hrefs and DuckDuckGo's uddg= redirects) in one pass, since the caller
    /// wants several candidates rather than only the first.
    /// </summary>
    public static IReadOnlyList<string> ExtractAllProductLinks(string searchHtml)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string url)
        {
            // Trim sub-paths like /reviews/123 down to the product page itself.
            var match = Regex.Match(url, "https://www\\.kvraudio\\.com/product/[a-z0-9-]+", RegexOptions.IgnoreCase);
            if (match.Success && seen.Add(match.Value))
            {
                found.Add(match.Value);
            }
        }

        foreach (Match match in Regex.Matches(searchHtml, "uddg=([^&\"']+)", RegexOptions.IgnoreCase))
        {
            Add(Uri.UnescapeDataString(match.Groups[1].Value));
        }

        foreach (Match match in Regex.Matches(searchHtml, "https://www\\.kvraudio\\.com/product/[a-z0-9-]+", RegexOptions.IgnoreCase))
        {
            Add(match.Value);
        }

        return found;
    }

    private async Task<KvrLookupResult?> TryDirectProductPageAsync(string pluginName, string vendor)
    {
        var nameSlug = Slugify(pluginName);
        var vendorSlug = Slugify(vendor);
        if (nameSlug.Length == 0 || vendorSlug.Length == 0)
        {
            return null;
        }

        try
        {
            var fetch = await FetchAsync($"https://www.kvraudio.com/product/{nameSlug}-by-{vendorSlug}");
            if (fetch is null || fetch.Value.StatusCode is < 200 or >= 300)
            {
                return null;
            }

            // A slug miss redirects to the generic /plugins/ listing rather than 404ing;
            // only a final URL still under /product/ is a real product page.
            var finalPath = fetch.Value.FinalUrl is { } finalUrl && Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath
                : null;

            if (finalPath is null || !finalPath.StartsWith("/product/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ParseAnyProductPage(fetch.Value.Body, fetch.Value.FinalUrl!);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private readonly record struct FetchResult(int StatusCode, string Body, string? FinalUrl);

    /// <summary>Unique enough to never collide with real page content, used to split curl's body output from its trailing metadata.</summary>
    private const string CurlMetaMarker = "<<<VSTMGR_CURL_META>>>";

    /// <summary>
    /// Fetches a URL preferring the OS's curl.exe (see class remarks for why), falling back
    /// to HttpClient if curl.exe can't be launched at all. Follows redirects either way and
    /// reports the final URL actually landed on, needed to detect KVR's miss-redirects.
    /// </summary>
    private static async Task<FetchResult?> FetchAsync(string url)
    {
        var viaCurl = await TryFetchViaCurlAsync(url);
        if (viaCurl is not null)
        {
            return viaCurl;
        }

        try
        {
            using var response = await Client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            return new FetchResult((int)response.StatusCode, body, response.RequestMessage?.RequestUri?.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<FetchResult?> TryFetchViaCurlAsync(string url)
    {
        var startInfo = new ProcessStartInfo("curl.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("-L");
        startInfo.ArgumentList.Add("--max-time");
        startInfo.ArgumentList.Add("8");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add(UserAgent);
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("\n" + CurlMetaMarker + "%{http_code}|%{url_effective}");
        startInfo.ArgumentList.Add(url);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                return null;
            }

            var markerIndex = stdout.LastIndexOf(CurlMetaMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            var body = stdout[..markerIndex];
            var meta = stdout[(markerIndex + CurlMetaMarker.Length)..].Trim().Split('|', 2);
            if (!int.TryParse(meta[0], out var statusCode))
            {
                return null;
            }

            var finalUrl = meta.Length > 1 && meta[1].Length > 0 ? meta[1] : null;
            return new FetchResult(statusCode, body, finalUrl);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        {
            // curl.exe missing from PATH, or timed out — fall back to HttpClient.
            return null;
        }
    }

    /// <summary>
    /// Builds a KVR-style URL slug: lowercase, alphanumerics kept, separators collapsed to
    /// single dashes, and trailing filename noise ("x64", "vst3", ...) dropped so scanned
    /// names like "Serum_x64" slug the same as the product name.
    /// </summary>
    public static string Slugify(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        var parts = Regex.Split(lower, "[^a-z0-9]+")
            .Where(p => p.Length > 0)
            .ToList();

        while (parts.Count > 1 && parts[^1] is "x64" or "x86" or "vst3" or "vst2" or "vst" or "win" or "win64")
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return string.Join("-", parts);
    }

    /// <summary>
    /// DuckDuckGo's HTML-only results wrap each link in a redirect, e.g.
    /// "//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2F...&amp;rut=...".
    /// Scans every result in order and returns the first one that's a real KVR product page
    /// (the site: filter usually guarantees this, but isn't airtight). Kept as a named,
    /// separately-testable method (rather than inlined) since the two search engines wrap
    /// their result links completely differently.
    /// </summary>
    public static string? ExtractFirstDuckDuckGoLink(string searchHtml)
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
    /// Brave's static (JS-free) search HTML links results directly — no redirect wrapper — as
    /// plain "https://www.kvraudio.com/product/..." hrefs. The site: filter keeps results
    /// scoped to product pages, but reviews/discussion sub-paths still slip through
    /// occasionally, so this only accepts a bare "/product/{slug}" path (no further segments).
    /// </summary>
    public static string? ExtractFirstKvrLink(string searchHtml)
    {
        foreach (Match match in Regex.Matches(searchHtml, "https://www\\.kvraudio\\.com/product/([a-z0-9-]+)", RegexOptions.IgnoreCase))
        {
            return match.Value;
        }

        return null;
    }

    /// <summary>
    /// Fetches a product page the user supplied by URL and extracts what it can. Accepts any
    /// site, not just KVR: KVR pages go through the richer parser (it can read the exact
    /// "Product Version" field), anything else falls back to OpenGraph/title scraping, which
    /// most plugin databases and vendor product pages expose.
    /// </summary>
    public virtual async Task<KvrLookupResult?> FetchFromUrlAsync(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            var fetch = await FetchAsync(uri.ToString());
            if (fetch is null || fetch.Value.StatusCode is < 200 or >= 300)
            {
                return null;
            }

            return ParseAnyProductPage(fetch.Value.Body, fetch.Value.FinalUrl ?? uri.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a product page from any site. KVR keeps its dedicated parser (richer: real
    /// version field, known title grammar); everything else is read via OpenGraph tags with a
    /// plain &lt;title&gt; fallback, which covers most plugin databases and vendor pages.
    /// </summary>
    public static KvrLookupResult? ParseAnyProductPage(string html, string sourceUrl)
    {
        if (sourceUrl.Contains("kvraudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var kvr = ParseProductPage(html);
            if (kvr is not null)
            {
                return kvr with { SourceUrl = sourceUrl };
            }
        }

        var title = ExtractMetaContent(html, "og:title");
        if (string.IsNullOrWhiteSpace(title))
        {
            var titleTag = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            title = titleTag.Success ? WebUtility.HtmlDecode(titleTag.Groups[1].Value).Trim() : null;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var (name, vendor) = SplitTitle(title, ExtractMetaContent(html, "og:site_name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var logo = ExtractMetaContent(html, "og:image");
        if (logo is not null && !logo.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            logo = Uri.TryCreate(new Uri(sourceUrl), logo, out var absolute) ? absolute.ToString() : null;
        }

        return new KvrLookupResult(name, vendor ?? string.Empty, logo, ExtractLooseVersion(html), sourceUrl);
    }

    private static string? ExtractMetaContent(string html, string property)
    {
        // Attribute order varies by site, so match content= either side of the property name.
        var patterns = new[]
        {
            $"<meta[^>]*(?:property|name)=[\"']{Regex.Escape(property)}[\"'][^>]*content=[\"']([^\"']*)[\"']",
            $"<meta[^>]*content=[\"']([^\"']*)[\"'][^>]*(?:property|name)=[\"']{Regex.Escape(property)}[\"']"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a page title into product and vendor. Handles the common shapes: "X by Y",
    /// "X - Y", "X | Y". The site name (from og:site_name) is stripped when it shows up as a
    /// trailing segment, so "Pro-Q 4 | Pluginboutique" doesn't report the shop as the vendor.
    /// </summary>
    public static (string Name, string? Vendor) SplitTitle(string title, string? siteName)
    {
        var working = title.Trim();

        foreach (var separator in new[] { " | ", " – ", " — " })
        {
            var index = working.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                var tail = working[(index + separator.Length)..].Trim();
                if (siteName is not null && tail.Contains(siteName, StringComparison.OrdinalIgnoreCase))
                {
                    working = working[..index].Trim();
                }
            }
        }

        var byIndex = working.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIndex > 0)
        {
            var name = working[..byIndex].Trim();
            var afterBy = working[(byIndex + 4)..].Trim();
            var dashIndex = afterBy.IndexOf(" - ", StringComparison.Ordinal);
            var vendor = (dashIndex >= 0 ? afterBy[..dashIndex] : afterBy).Trim();
            return (name, string.IsNullOrWhiteSpace(vendor) ? null : vendor);
        }

        var hyphen = working.IndexOf(" - ", StringComparison.Ordinal);
        if (hyphen > 0)
        {
            return (working[..hyphen].Trim(), null);
        }

        return (working, null);
    }

    /// <summary>
    /// Best-effort version scrape for non-KVR pages, which have no standard version field:
    /// looks for "version 1.2.3" / "v1.2.3" near the top of the document. Returns null rather
    /// than guessing from any loose number, since a wrong version drives a wrong OUTDATED badge.
    /// </summary>
    public static string? ExtractLooseVersion(string html)
    {
        var match = Regex.Match(html, @"\bversion\s*:?\s*v?(\d+\.\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
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

        return new KvrLookupResult(productName, vendor, logoUrl, ExtractLatestVersion(productHtml));
    }

    /// <summary>
    /// KVR product pages carry the current release as "Product Version X.Y" per platform,
    /// anchored in the HTML as &lt;div ... id="verwin"&gt;4.13&lt;/div&gt; (Windows) and
    /// id="verosx" (macOS). Windows is preferred since this app manages Windows plugins;
    /// falls back to the macOS value for Mac-only product pages.
    /// </summary>
    public static string? ExtractLatestVersion(string productHtml)
    {
        foreach (var anchor in new[] { "verwin", "verosx" })
        {
            var match = Regex.Match(productHtml, $"id=\"{anchor}\"[^>]*>\\s*([^<]+?)\\s*<", RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }
}
