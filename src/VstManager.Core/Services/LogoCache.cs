using System.Security.Cryptography;
using System.Text;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class LogoCache
{
    /// <summary>How old a cached logo may get before "Refresh All Metadata" re-downloads it.</summary>
    private static readonly TimeSpan LogoFreshWindow = TimeSpan.FromDays(30);

    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _failedThisSession = new();

    /// <summary>
    /// slug → cached file path, so a cache hit is a dictionary lookup rather than a directory
    /// scan. Built once on first use: the old per-call Directory.EnumerateFiles meant one
    /// filesystem enumeration per plugin per load, which on a large library dominated the cost
    /// of showing logos that were already downloaded.
    /// </summary>
    private Dictionary<string, string>? _index;
    private readonly object _indexLock = new();

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

    /// <summary>
    /// Re-fetches a catalog logo, but only when the cached copy is missing or has gone stale.
    /// This runs across the whole library on "Refresh All Metadata"; unconditionally deleting
    /// and re-downloading every logo there meant a full re-download of artwork that hadn't
    /// changed. Pass force: true for a single plugin the user explicitly asked to refresh.
    /// </summary>
    public async Task<string?> RefreshLogoAsync(CatalogEntry entry, bool force = false, CancellationToken cancellationToken = default)
    {
        var slug = GetSlug(entry);
        _failedThisSession.Remove(slug);

        Directory.CreateDirectory(_cacheDirectory);

        if (!force)
        {
            var existing = FindCachedFile(slug);
            if (existing is not null && !IsStale(existing))
            {
                return existing;
            }
        }

        DeleteCachedFiles(slug);

        var cachedPath = Path.Combine(_cacheDirectory, slug + GetExtensionFromUrl(entry.LogoUrl));
        return await DownloadFromUrlAsync(entry.LogoUrl, cachedPath, cancellationToken, slug);
    }

    private static bool IsStale(string path)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > LogoFreshWindow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
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

    /// <summary>
    /// Saves a locally-picked image file (e.g. from a file-browse dialog) as a plugin's manual
    /// logo override. Unlike <see cref="GetManualLogoPathAsync"/> this never touches the network
    /// — it exists for the case where the plugin's normal artwork URL fails to decode (or there
    /// isn't one), so the user can supply a working image straight from disk.
    /// </summary>
    public async Task<string?> SaveLocalLogoAsync(string name, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var slug = GetSlugForName(name);
        DeleteCachedFiles(slug);

        var bytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        var extension = DetectImageExtension(bytes, contentType: null)
            ?? (IsSupportedImageExtension(Path.GetExtension(sourceFilePath).ToLowerInvariant())
                ? Path.GetExtension(sourceFilePath).ToLowerInvariant()
                : ".png");

        var cachedPath = Path.Combine(_cacheDirectory, slug + extension);
        await File.WriteAllBytesAsync(cachedPath, bytes, cancellationToken);
        RememberCachedFile(slug, cachedPath);
        return cachedPath;
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

            var slug = Path.GetFileNameWithoutExtension(correctedPath);
            if (!string.IsNullOrEmpty(slug))
            {
                RememberCachedFile(slug, correctedPath);
            }

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

    /// <summary>Finds the cached manual-override logo for a plugin name, whatever extension it
    /// was saved with, without touching the network.</summary>
    public string? FindManualCachedFile(string name) => FindCachedFile(GetSlugForName(name));

    /// <summary>
    /// Builds (once) and returns the slug → path index. The cache directory is flat and only
    /// this process writes to it, so a single enumeration at startup stays accurate as long as
    /// every write path below keeps the index in step.
    /// </summary>
    private Dictionary<string, string> GetIndex()
    {
        lock (_indexLock)
        {
            if (_index is not null)
            {
                return _index;
            }

            _index = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                {
                    var slug = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(slug))
                    {
                        _index[slug] = file;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable cache directory just means every logo re-downloads.
            }

            return _index;
        }
    }

    /// <summary>Finds an existing cached logo for a slug, whatever extension it was saved with.</summary>
    private string? FindCachedFile(string slug)
    {
        var index = GetIndex();

        lock (_indexLock)
        {
            if (!index.TryGetValue(slug, out var path))
            {
                return null;
            }

            // The index can outlive the file if something outside the app cleared the folder.
            if (File.Exists(path))
            {
                return path;
            }

            index.Remove(slug);
            return null;
        }
    }

    private void RememberCachedFile(string slug, string path)
    {
        var index = GetIndex();

        lock (_indexLock)
        {
            index[slug] = path;
        }
    }

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

        var index = GetIndex();
        lock (_indexLock)
        {
            index.Remove(slug);
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
