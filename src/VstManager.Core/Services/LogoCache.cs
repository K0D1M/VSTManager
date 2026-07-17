using System.Security.Cryptography;
using System.Text;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class LogoCache
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _failedThisSession = new();

    public LogoCache(HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDirectory = cacheDirectory ?? GetDefaultCacheDirectory();
    }

    public static string GetDefaultCacheDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "logos");
    }

    public static string GetSlug(CatalogEntry entry)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(entry.Name.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public async Task<string?> GetLogoPathAsync(CatalogEntry entry, CancellationToken cancellationToken = default)
    {
        var slug = GetSlug(entry);

        if (_failedThisSession.Contains(slug))
        {
            return null;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var extension = GetExtensionFromUrl(entry.LogoUrl);
        var cachedPath = Path.Combine(_cacheDirectory, slug + extension);

        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        return await DownloadAsync(entry, cachedPath, cancellationToken);
    }

    public async Task<string?> RefreshLogoAsync(CatalogEntry entry, CancellationToken cancellationToken = default)
    {
        var slug = GetSlug(entry);
        _failedThisSession.Remove(slug);

        Directory.CreateDirectory(_cacheDirectory);
        var extension = GetExtensionFromUrl(entry.LogoUrl);
        var cachedPath = Path.Combine(_cacheDirectory, slug + extension);

        if (File.Exists(cachedPath))
        {
            File.Delete(cachedPath);
        }

        return await DownloadFromUrlAsync(entry.LogoUrl, cachedPath, cancellationToken, slug);
    }

    public static string GetSlugForName(string name)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant() + "-manual";
    }

    public async Task<string?> GetManualLogoPathAsync(string name, string sourceUrl, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var slug = GetSlugForName(name);
        var extension = GetExtensionFromUrl(sourceUrl);
        var cachedPath = Path.Combine(_cacheDirectory, slug + extension);

        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        return await DownloadFromUrlAsync(sourceUrl, cachedPath, cancellationToken);
    }

    public async Task<string?> DownloadPreviewAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var tempPath = Path.Combine(_cacheDirectory, "preview-" + Guid.NewGuid().ToString("N") + GetExtensionFromUrl(sourceUrl));
        return await DownloadFromUrlAsync(sourceUrl, tempPath, cancellationToken);
    }

    private async Task<string?> DownloadAsync(CatalogEntry entry, string cachedPath, CancellationToken cancellationToken) =>
        await DownloadFromUrlAsync(entry.LogoUrl, cachedPath, cancellationToken, GetSlug(entry));

    private async Task<string?> DownloadFromUrlAsync(string url, string cachedPath, CancellationToken cancellationToken, string? failureTrackingSlug = null)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(cachedPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
            return cachedPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (failureTrackingSlug is not null)
            {
                _failedThisSession.Add(failureTrackingSlug);
            }

            return null;
        }
    }

    private static string GetExtensionFromUrl(string url)
    {
        var uri = new Uri(url);
        var extension = Path.GetExtension(uri.LocalPath);
        return string.IsNullOrEmpty(extension) ? ".png" : extension;
    }
}
