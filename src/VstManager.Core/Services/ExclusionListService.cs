using System.Reflection;
using System.Text.Json;

namespace VstManager.Core.Services;

public class ExclusionListService
{
    private readonly string _localOverridePath;
    private readonly HashSet<string> _excludedFileNames;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public ExclusionListService(string? localOverridePath = null)
    {
        _localOverridePath = localOverridePath ?? GetDefaultLocalOverridePath();
        _excludedFileNames = new HashSet<string>(LoadBundled(), StringComparer.OrdinalIgnoreCase);
        _excludedFileNames.UnionWith(LoadLocalOverride());
    }

    public static string GetDefaultLocalOverridePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "excluded-files.local.json");
    }

    public bool IsExcluded(string filePath) =>
        _excludedFileNames.Contains(Path.GetFileName(filePath));

    /// <summary>Re-reads the local override file from disk (e.g. after a data import replaced it).</summary>
    public void Reload()
    {
        _excludedFileNames.Clear();
        _excludedFileNames.UnionWith(LoadBundled());
        _excludedFileNames.UnionWith(LoadLocalOverride());
    }

    public void Exclude(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (!_excludedFileNames.Add(fileName))
        {
            return;
        }

        SaveLocalOverride();
    }

    private static List<string> LoadBundled()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("excluded-files.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return new List<string>();
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new List<string>();
        }

        return JsonSerializer.Deserialize<List<string>>(stream) ?? new List<string>();
    }

    private List<string> LoadLocalOverride()
    {
        if (!File.Exists(_localOverridePath))
        {
            return new List<string>();
        }

        var json = File.ReadAllText(_localOverridePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    private void SaveLocalOverride()
    {
        var directory = Path.GetDirectoryName(_localOverridePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_excludedFileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), SerializerOptions);
        File.WriteAllText(_localOverridePath, json);
    }
}
