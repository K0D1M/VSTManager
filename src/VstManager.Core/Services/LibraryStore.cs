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

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<LibraryData>(json) ?? new LibraryData();
    }

    public void Save(LibraryData data)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }

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
            }

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
}
