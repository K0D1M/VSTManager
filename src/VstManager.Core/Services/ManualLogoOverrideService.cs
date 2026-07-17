using System.Text.Json;

namespace VstManager.Core.Services;

public class ManualLogoOverrideService
{
    private readonly string _filePath;
    private readonly Dictionary<string, string> _overrides;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public ManualLogoOverrideService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
        _overrides = Load();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "manual-logos.json");
    }

    public string? GetOverrideUrl(string name) =>
        _overrides.TryGetValue(NormalizeKey(name), out var url) ? url : null;

    public void SetOverride(string name, string url)
    {
        _overrides[NormalizeKey(name)] = url;
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

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_overrides, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
