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

    public List<PluginInfo> MergeOnRescan(List<PluginInfo> existing, List<PluginInfo> scanned)
    {
        var existingByPath = existing.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
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
            merged.Add(found);
        }

        return merged;
    }
}
