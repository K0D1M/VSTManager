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

        var existing = FindCachedFile(slug);
        if (existing is not null)
        {
            return existing;
        }

        var cachedPath = Path.Combine(_cacheDirectory, slug + GetExtensionFromUrl(entry.LogoUrl));
        return await DownloadAsync(entry, cachedPath, cancellationToken);
    }

    public async Task<string?> RefreshLogoAsync(CatalogEntry entry, CancellationToken cancellationToken = default)
    {
        var slug = GetSlug(entry);
        _failedThisSession.Remove(slug);

        Directory.CreateDirectory(_cacheDirectory);
        DeleteCachedFiles(slug);

        var cachedPath = Path.Combine(_cacheDirectory, slug + GetExtensionFromUrl(entry.LogoUrl));
        return await DownloadFromUrlAsync(entry.LogoUrl, cachedPath, cancellationToken, slug);
    }

    public static string GetSlugForName(string name)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant() + "-manual";
    }

    public async Task<string?> GetManualLogoPathAsync(string name, string sourceUrl, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var slug = GetSlugForName(name);

        if (forceRefresh)
        {
            // The user is saving a (possibly new) URL — discard any stale cache file so the
            // corrected image, and its correct extension, replace it.
            DeleteCachedFiles(slug);
        }
        else
        {
            // Cache hit regardless of extension: this is called on every load with the stored
            // override URL, so it must not re-download each time. The real format (e.g. .webp)
            // may differ from what the URL's path suggested, so match by slug, not extension.
            var existing = FindCachedFile(slug);
            if (existing is not null)
            {
                return existing;
            }
        }

        var cachedPath = Path.Combine(_cacheDirectory, slug + GetExtensionFromUrl(sourceUrl));
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
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // The extension guessed from the URL can be wrong or missing (query-string image
            // APIs, redirects, content negotiation), and WPF's image decoder keys off the
            // file extension — so WebP bytes saved as ".png" silently fail to display.
            // Correct the extension from the actual bytes (and the response Content-Type as a
            // fallback) so the cached file's name always matches its real format.
            var actualExtension = DetectImageExtension(bytes, response.Content.Headers.ContentType?.MediaType);
            var correctedPath = actualExtension is null
                ? cachedPath
                : Path.ChangeExtension(cachedPath, actualExtension);

            await File.WriteAllBytesAsync(correctedPath, bytes, cancellationToken);
            return correctedPath;
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

    /// <summary>Finds an existing cached logo for a slug, whatever extension it was saved with.</summary>
    private string? FindCachedFile(string slug) =>
        Directory.EnumerateFiles(_cacheDirectory, slug + ".*")
            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), slug, StringComparison.Ordinal));

    /// <summary>Removes every cached file for a slug (any extension) before a fresh download.</summary>
    private void DeleteCachedFiles(string slug)
    {
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, slug + ".*").ToList())
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(file), slug, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best-effort; a stale file will simply be overwritten on the next matching download.
            }
        }
    }

    private static string GetExtensionFromUrl(string url)
    {
        var uri = new Uri(url);
        var extension = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
        return IsSupportedImageExtension(extension) ? extension : ".png";
    }

    private static bool IsSupportedImageExtension(string extension) => extension is
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    /// <summary>
    /// Identifies an image format from its leading "magic number" bytes, so the cached file
    /// gets the right extension no matter what the URL looked like. Falls back to the HTTP
    /// Content-Type when the signature isn't recognized, and to null (keep the URL-guessed
    /// extension) when neither is conclusive.
    /// </summary>
    private static string? DetectImageExtension(byte[] bytes, string? contentType)
    {
        if (bytes.Length >= 12)
        {
            // WebP: "RIFF" .... "WEBP"
            if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
                bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            {
                return ".webp";
            }
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
        {
            return ".gif";
        }

        if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
        {
            return ".bmp";
        }

        return contentType switch
        {
            "image/webp" => ".webp",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => null
        };
    }
}
