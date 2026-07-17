using System.Reflection;
using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginCatalog
{
    private const string RemoteCatalogUrl = "https://raw.githubusercontent.com/K0D1M/VSTManager/main/catalog.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private List<CatalogEntry> _entries;

    public PluginCatalog(IEnumerable<CatalogEntry>? entries = null)
    {
        _entries = entries?.ToList() ?? LoadCached() ?? LoadBundled();
    }

    public IReadOnlyList<CatalogEntry> Entries => _entries;

    public static string GetCachePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "catalog.cache.json");
    }

    /// <summary>
    /// Downloads the latest catalog from the GitHub repo, caches it locally, and swaps
    /// it in. Returns true when the catalog changed. Offline/parse failures leave the
    /// current (cached or bundled) catalog untouched.
    /// </summary>
    public async Task<bool> TryRefreshFromRemoteAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default)
    {
        var client = httpClient ?? new HttpClient();
        try
        {
            var json = await client.GetStringAsync(RemoteCatalogUrl, cancellationToken);
            var remote = JsonSerializer.Deserialize<List<CatalogEntry>>(json, SerializerOptions);
            if (remote is null || remote.Count == 0)
            {
                return false;
            }

            var changed = remote.Count != _entries.Count
                          || remote.Zip(_entries).Any(pair =>
                              pair.First.Name != pair.Second.Name
                              || pair.First.Vendor != pair.Second.Vendor
                              || pair.First.LogoUrl != pair.Second.LogoUrl);

            var cachePath = GetCachePath();
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(cachePath, json);

            _entries = remote;
            return changed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return false;
        }
        finally
        {
            if (httpClient is null)
            {
                client.Dispose();
            }
        }
    }

    private static List<CatalogEntry>? LoadCached()
    {
        var cachePath = GetCachePath();
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(cachePath);
            var cached = JsonSerializer.Deserialize<List<CatalogEntry>>(json, SerializerOptions);
            return cached is { Count: > 0 } ? cached : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static List<CatalogEntry> LoadBundled()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return new List<CatalogEntry>();
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new List<CatalogEntry>();
        }

        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream, SerializerOptions) ?? new List<CatalogEntry>();
    }
}
