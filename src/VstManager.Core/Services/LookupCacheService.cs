using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// Remembers KVR lookup results across launches, keyed by plugin base name.
///
/// Without this, every launch re-queries the web for every plugin — on a large library that's
/// minutes of waiting for answers that almost never change between one launch and the next.
/// Entries stay usable for a week; misses expire sooner, since a plugin absent from KVR today
/// may be listed next month, but re-searching for it every launch is exactly what makes a big
/// library painful.
///
/// Writes are batched: callers mutate through <see cref="Set"/> and call <see cref="Save"/> once
/// at the end of a pass, rather than paying a full file rewrite per plugin.
/// </summary>
public class LookupCacheService
{
    /// <summary>How long a successful lookup is served without going back to the network.</summary>
    public static readonly TimeSpan FreshWindow = TimeSpan.FromDays(7);

    /// <summary>Shorter window for "not on KVR", so newly listed plugins are picked up sooner.</summary>
    public static readonly TimeSpan NotFoundWindow = TimeSpan.FromDays(2);

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Dictionary<string, CachedLookup> _entries;

    public LookupCacheService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
        _entries = Load();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "lookup-cache.json");
    }

    /// <summary>The stored entry regardless of age, or null if this plugin was never looked up.</summary>
    public CachedLookup? Get(string baseName) =>
        _entries.TryGetValue(NormalizeKey(baseName), out var entry) ? entry : null;

    /// <summary>
    /// True when the entry is recent enough to use without going back to the network. A missing
    /// entry is never fresh.
    /// </summary>
    public bool IsFresh(CachedLookup? entry, DateTime? now = null)
    {
        if (entry is null)
        {
            return false;
        }

        var window = entry.NotFound ? NotFoundWindow : FreshWindow;
        return (now ?? DateTime.UtcNow) - entry.FetchedAt < window;
    }

    public bool IsFresh(string baseName, DateTime? now = null) => IsFresh(Get(baseName), now);

    /// <summary>Records a successful lookup. Does not write to disk — call <see cref="Save"/>.</summary>
    public void Set(string baseName, KvrLookupResult result)
    {
        _entries[NormalizeKey(baseName)] = new CachedLookup
        {
            ProductName = result.ProductName,
            Vendor = result.Vendor,
            LogoUrl = result.LogoUrl,
            LatestVersion = result.LatestVersion,
            SourceUrl = result.SourceUrl,
            Categories = result.Categories.ToList(),
            NotFound = false,
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>Records that a lookup completed and found nothing.</summary>
    public void SetNotFound(string baseName)
    {
        _entries[NormalizeKey(baseName)] = new CachedLookup
        {
            NotFound = true,
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>Rebuilds a lookup result from a cached entry, or null if the entry was a miss.</summary>
    public static KvrLookupResult? ToResult(CachedLookup? entry)
    {
        if (entry is null || entry.NotFound || string.IsNullOrWhiteSpace(entry.ProductName))
        {
            return null;
        }

        return new KvrLookupResult(
            entry.ProductName,
            entry.Vendor ?? string.Empty,
            entry.LogoUrl,
            entry.LatestVersion,
            entry.SourceUrl,
            entry.Categories);
    }

    public void Remove(string baseName) => _entries.Remove(NormalizeKey(baseName));

    /// <summary>Drops everything, so the next pass re-queries from scratch.</summary>
    public void Clear() => _entries.Clear();

    public void Reload()
    {
        _entries.Clear();
        foreach (var (key, value) in Load())
        {
            _entries[key] = value;
        }
    }

    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private Dictionary<string, CachedLookup> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, CachedLookup>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, CachedLookup>>(json)
                   ?? new Dictionary<string, CachedLookup>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A damaged cache is only ever a speed loss — start over rather than block launch.
            return new Dictionary<string, CachedLookup>();
        }
    }

    public void Save() => JsonFileStore.Write(_filePath, _entries, SerializerOptions);
}
