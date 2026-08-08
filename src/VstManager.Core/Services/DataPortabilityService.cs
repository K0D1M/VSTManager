using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// Bundles the library (plugin tags/versions/scan folders/preferences), exclusion list
/// override, and manual logo overrides into a single exportable file, and restores them on
/// import. Used so a user can carry their data across a reinstall or a new machine.
/// </summary>
public class DataPortabilityService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _libraryPath;
    private readonly string _excludedFilesPath;
    private readonly string _manualLogoOverridesPath;
    private readonly string _manualMetadataOverridesPath;
    private readonly string _pluginTagsPath;

    public DataPortabilityService(
        string? libraryPath = null,
        string? excludedFilesPath = null,
        string? manualLogoOverridesPath = null,
        string? manualMetadataOverridesPath = null,
        string? pluginTagsPath = null)
    {
        _libraryPath = libraryPath ?? LibraryStore.GetDefaultPath();
        _excludedFilesPath = excludedFilesPath ?? ExclusionListService.GetDefaultLocalOverridePath();
        _manualLogoOverridesPath = manualLogoOverridesPath ?? ManualLogoOverrideService.GetDefaultPath();
        _manualMetadataOverridesPath = manualMetadataOverridesPath ?? ManualMetadataOverrideService.GetDefaultPath();
        _pluginTagsPath = pluginTagsPath ?? PluginTagService.GetDefaultPath();
    }

    public string ExportBundle()
    {
        var bundle = new DataExportBundle
        {
            ExportedAt = DateTime.UtcNow,
            Library = ReadRawJson(_libraryPath),
            ExcludedFiles = ReadRawJson(_excludedFilesPath),
            ManualLogoOverrides = ReadRawJson(_manualLogoOverridesPath),
            ManualMetadataOverrides = ReadRawJson(_manualMetadataOverridesPath),
            PluginTags = ReadRawJson(_pluginTagsPath)
        };

        return JsonSerializer.Serialize(bundle, SerializerOptions);
    }

    public void ImportBundle(string json)
    {
        DataExportBundle bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<DataExportBundle>(json, SerializerOptions)
                      ?? throw new InvalidDataException("The file doesn't contain any recognizable data.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("That file isn't a valid VST Manager export.", ex);
        }

        WriteRawJsonIfPresent(_libraryPath, bundle.Library);
        WriteRawJsonIfPresent(_excludedFilesPath, bundle.ExcludedFiles);
        WriteRawJsonIfPresent(_manualLogoOverridesPath, bundle.ManualLogoOverrides);
        WriteRawJsonIfPresent(_manualMetadataOverridesPath, bundle.ManualMetadataOverrides);
        WriteRawJsonIfPresent(_pluginTagsPath, bundle.PluginTags);
    }

    private static JsonElement? ReadRawJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static void WriteRawJsonIfPresent(string path, JsonElement? element)
    {
        if (element is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, element.Value.GetRawText());
    }
}
