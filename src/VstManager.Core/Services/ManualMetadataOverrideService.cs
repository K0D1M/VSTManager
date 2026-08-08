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

    public void SetOverride(string baseName, string? name, string? vendor)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var normalizedVendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim();
        var key = NormalizeKey(baseName);

        if (normalizedName is null && normalizedVendor is null)
        {
            _overrides.Remove(key);
        }
        else
        {
            _overrides[key] = new ManualMetadataOverride { Name = normalizedName, Vendor = normalizedVendor };
        }

        Save();
    }

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
