using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// Which tags each plugin carries, persisted to disk.
///
/// Keyed by base name rather than file path, matching <see cref="ManualMetadataOverrideService"/>:
/// tags belong to a plugin, not to each installed copy of it, so a plugin with a VST2 and a VST3
/// build carries one set of tags rather than two.
/// </summary>
public class PluginTagService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Dictionary<string, PluginTagAssignment> _assignments;

    public PluginTagService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
        _assignments = Load();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "plugin-tags.json");
    }

    public PluginTagAssignment? GetAssignment(string baseName) =>
        _assignments.TryGetValue(NormalizeKey(baseName), out var entry) ? entry : null;

    /// <summary>Every tag id this plugin effectively carries, manual first.</summary>
    public IReadOnlyList<string> GetTagIds(string baseName) =>
        GetAssignment(baseName)?.AllTagIds.ToList() ?? new List<string>();

    /// <summary>Adds a manual tag. No-op when the plugin already carries it manually.</summary>
    public void AddTag(string baseName, string tagId, bool save = true)
    {
        var entry = GetOrCreate(baseName);

        if (!entry.TagIds.Contains(tagId, StringComparer.OrdinalIgnoreCase))
        {
            entry.TagIds.Add(tagId);
        }

        // Applying by hand something that was auto-detected promotes it to a manual tag, and
        // clears any earlier suppression — the user has now explicitly asked for it.
        entry.AutoTagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));
        entry.SuppressedAutoTagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));

        if (save)
        {
            Save();
        }
    }

    /// <summary>
    /// Removes a tag however it was applied. Removing an auto-detected tag also records the
    /// suppression, so the next KVR detection doesn't put it straight back.
    /// </summary>
    public void RemoveTag(string baseName, string tagId, bool save = true)
    {
        var entry = GetAssignment(baseName);
        if (entry is null)
        {
            return;
        }

        entry.TagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));

        if (entry.AutoTagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase)) > 0
            && !entry.SuppressedAutoTagIds.Contains(tagId, StringComparer.OrdinalIgnoreCase))
        {
            entry.SuppressedAutoTagIds.Add(tagId);
        }

        if (entry.IsEmpty)
        {
            _assignments.Remove(NormalizeKey(baseName));
        }

        if (save)
        {
            Save();
        }
    }

    public bool HasTag(string baseName, string tagId) =>
        GetAssignment(baseName)?.AllTagIds.Contains(tagId, StringComparer.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// Replaces the auto-detected tags for a plugin, honouring anything the user has suppressed
    /// or already applied manually. Callers doing a bulk pass should pass save: false and call
    /// <see cref="Save"/> once at the end.
    /// </summary>
    public void SetAutoTags(string baseName, IEnumerable<string> tagIds, bool save = true)
    {
        var entry = GetOrCreate(baseName);

        entry.AutoTagIds = tagIds
            .Where(id => !entry.SuppressedAutoTagIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Where(id => !entry.TagIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entry.IsEmpty)
        {
            _assignments.Remove(NormalizeKey(baseName));
        }

        if (save)
        {
            Save();
        }
    }

    /// <summary>Drops a tag from every plugin — used when a custom tag is deleted.</summary>
    public void RemoveTagEverywhere(string tagId)
    {
        foreach (var key in _assignments.Keys.ToList())
        {
            var entry = _assignments[key];
            entry.TagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));
            entry.AutoTagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));
            entry.SuppressedAutoTagIds.RemoveAll(id => string.Equals(id, tagId, StringComparison.OrdinalIgnoreCase));

            if (entry.IsEmpty)
            {
                _assignments.Remove(key);
            }
        }

        Save();
    }

    /// <summary>How many plugins carry a given tag. Drives the counts in the tag manager.</summary>
    public int CountFor(string tagId) =>
        _assignments.Values.Count(a => a.AllTagIds.Contains(tagId, StringComparer.OrdinalIgnoreCase));

    public void Reload()
    {
        _assignments.Clear();
        foreach (var (key, value) in Load())
        {
            _assignments[key] = value;
        }
    }

    private PluginTagAssignment GetOrCreate(string baseName)
    {
        var key = NormalizeKey(baseName);
        if (!_assignments.TryGetValue(key, out var entry))
        {
            entry = new PluginTagAssignment();
            _assignments[key] = entry;
        }

        return entry;
    }

    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private Dictionary<string, PluginTagAssignment> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PluginTagAssignment>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, PluginTagAssignment>>(json)
                   ?? new Dictionary<string, PluginTagAssignment>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            JsonFileStore.Quarantine(_filePath);
            return new Dictionary<string, PluginTagAssignment>();
        }
    }

    public void Save() => JsonFileStore.Write(_filePath, _assignments, SerializerOptions);
}
