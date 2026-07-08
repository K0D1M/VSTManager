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

        return await DownloadAsync(entry, cachedPath, cancellationToken);
    }

    private async Task<string?> DownloadAsync(CatalogEntry entry, string cachedPath, CancellationToken cancellationToken)
    {
        var slug = GetSlug(entry);

        try
        {
            using var response = await _httpClient.GetAsync(entry.LogoUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(cachedPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
            return cachedPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _failedThisSession.Add(slug);
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
