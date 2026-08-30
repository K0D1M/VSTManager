using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class ManualMetadataOverrideService
{
    private readonly string _filePath;
    private readonly Dictionary<string, ManualMetadataOverride> _overrides;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public ManualMetadataOverrideService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
        _overrides = Load();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "manual-metadata.json");
    }

    public ManualMetadataOverride? GetOverride(string baseName) =>
        _overrides.TryGetValue(NormalizeKey(baseName), out var entry) ? entry : null;

    /// <summary>
    /// Records a name/vendor correction, leaving any version correction in place — the two are
    /// edited from different places in the UI and must not clear each other.
    /// </summary>
    public void SetOverride(string baseName, string? name, string? vendor)
    {
        var key = NormalizeKey(baseName);
        _overrides.TryGetValue(key, out var existing);

        Store(key, new ManualMetadataOverride
        {
            Name = Normalize(name),
            Vendor = Normalize(vendor),
            CurrentVersion = existing?.CurrentVersion,
            LatestVersion = existing?.LatestVersion
        });
    }

    /// <summary>
    /// Records a version correction. Passing null for a version clears that one correction,
    /// letting the detected value take over again.
    /// </summary>
    public void SetVersionOverride(string baseName, string? currentVersion, string? latestVersion)
    {
        var key = NormalizeKey(baseName);
        _overrides.TryGetValue(key, out var existing);

        Store(key, new ManualMetadataOverride
        {
            Name = existing?.Name,
            Vendor = existing?.Vendor,
            CurrentVersion = Normalize(currentVersion),
            LatestVersion = Normalize(latestVersion)
        });
    }

    /// <summary>Writes the entry, or drops it entirely once nothing is overridden any more.</summary>
    private void Store(string key, ManualMetadataOverride entry)
    {
        var isEmpty = entry.Name is null
                      && entry.Vendor is null
                      && entry.CurrentVersion is null
                      && entry.LatestVersion is null;

        if (isEmpty)
        {
            _overrides.Remove(key);
        }
        else
        {
            _overrides[key] = entry;
        }

        Save();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void ClearOverride(string baseName)
    {
        _overrides.Remove(NormalizeKey(baseName));
        Save();
    }

    /// <summary>Re-reads the override file from disk (e.g. after a data import replaced it).</summary>
    public void Reload()
    {
        _overrides.Clear();
        foreach (var (key, value) in Load())
        {
            _overrides[key] = value;
        }
    }

    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private Dictionary<string, ManualMetadataOverride> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, ManualMetadataOverride>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ManualMetadataOverride>>(json)
                   ?? new Dictionary<string, ManualMetadataOverride>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged overrides file used to take the whole app down with it: this is read
            // from the view model's constructor, so the exception surfaced as "the constructor
            // on MainWindow threw" and the window never opened — unrecoverable without editing
            // JSON by hand. Losing manual corrections is bad, but being unable to start is worse,
            // so the file is set aside for recovery and the app carries on.
            JsonFileStore.Quarantine(_filePath);
            return new Dictionary<string, ManualMetadataOverride>();
        }
    }

    private void Save() => JsonFileStore.Write(_filePath, _overrides, SerializerOptions);
}
