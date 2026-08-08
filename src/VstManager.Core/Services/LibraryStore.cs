using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class LibraryStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public LibraryStore(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "library.json");
    }

    public LibraryData Load()
    {
        if (!File.Exists(_filePath))
        {
            return new LibraryData();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<LibraryData>(json) ?? new LibraryData();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Starting empty loses tags, types and favourites, which is genuinely painful — but
            // the alternative is an app that cannot open at all, since this is read during
            // start-up. The damaged file is kept as "<name>.corrupt" so nothing is destroyed and
            // it can be repaired by hand; a rescan repopulates the plugin list itself.
            JsonFileStore.Quarantine(_filePath);
            return new LibraryData();
        }
    }

    public void Save(LibraryData data) => JsonFileStore.Write(_filePath, data, SerializerOptions);

    /// <summary>
    /// Reconciles the stored library against a fresh scan. Entries whose files are still on
    /// disk keep their user-set metadata; entries whose files have gone are *retained* and
    /// flagged uninstalled rather than dropped, so uninstalling a plugin no longer destroys
    /// its tag, kind, versions, favourite and hidden state.
    /// </summary>
    public List<PluginInfo> MergeOnRescan(List<PluginInfo> existing, List<PluginInfo> scanned)
    {
        var existingByPath = existing.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var scannedPaths = new HashSet<string>(scanned.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
        var merged = new List<PluginInfo>();

        foreach (var found in scanned)
        {
            if (existingByPath.TryGetValue(found.Path, out var existingEntry))
            {
                found.Tag = existingEntry.Tag;
                found.Kind = existingEntry.Kind;
                found.CurrentVersion = existingEntry.CurrentVersion;
                found.LatestVersion = existingEntry.LatestVersion;
                found.IsFavorite = existingEntry.IsFavorite;
                found.IsHidden = existingEntry.IsHidden;
                found.FirstSeenAt = existingEntry.FirstSeenAt;

                // A fresh scan re-reads the vendor from disk; keep the stored one only when this
                // scan came back empty, so an improved detector can correct an earlier answer
                // without a blank ever overwriting a good value.
                found.Vendor ??= existingEntry.Vendor;
            }

            // Genuinely new to the library, or an entry from before FirstSeenAt existed. New
            // ones are stamped now; older ones are backfilled from the file's own timestamp,
            // which approximates when it was installed rather than pretending it just appeared.
            found.FirstSeenAt ??= existingByPath.ContainsKey(found.Path)
                ? GetFileTimestamp(found.Path)
                : DateTime.UtcNow;

            // Anything the scan found is on disk by definition. This is also the reinstall
            // path: a previously remembered plugin whose file reappeared becomes installed
            // again, keeping the classification it had before it was removed.
            found.IsUninstalled = false;
            found.UninstalledAt = null;
            merged.Add(found);
        }

        // Retain stored entries the scan didn't find, flagged as gone from disk. Also covers
        // a scan folder being temporarily unavailable (e.g. an external drive): those plugins
        // are now hidden but recoverable, and restored automatically on the next good scan.
        foreach (var orphan in existing)
        {
            if (scannedPaths.Contains(orphan.Path))
            {
                continue;
            }

            // Keep the first-noticed timestamp so repeated rescans stay idempotent.
            orphan.UninstalledAt ??= DateTime.UtcNow;
            orphan.IsUninstalled = true;
            merged.Add(orphan);
        }

        return merged;
    }

    /// <summary>
    /// The plugin file's last-write time, used to backfill FirstSeenAt for entries stored before
    /// that field existed. Returns null rather than guessing when the file can't be read.
    /// </summary>
    private static DateTime? GetFileTimestamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
