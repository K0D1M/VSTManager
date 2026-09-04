using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// Which folder each plugin is filed in, persisted to disk.
///
/// Keyed by base name rather than file path, matching <see cref="PluginTagService"/> and
/// <see cref="ManualMetadataOverrideService"/>: a plugin with both a VST2 and a VST3 build is one
/// plugin to the user, so it is filed once rather than twice.
///
/// The folder *definitions* live in library.json (see <see cref="LibraryData.Folders"/>) so they
/// export and cloud-sync with the rest of the library for free — the same split the tag system
/// uses, where definitions travel with the library and assignments sit in their own file.
/// </summary>
public class PluginFolderService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    /// <summary>Plugin base name (normalized) to folder id. One folder per plugin.</summary>
    private readonly Dictionary<string, string> _assignments;

    public PluginFolderService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
        _assignments = Load();
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VstManager", "plugin-folders.json");
    }

    /// <summary>The folder this plugin is filed in, or null when it is unfiled.</summary>
    public string? GetFolderId(string baseName) =>
        _assignments.TryGetValue(NormalizeKey(baseName), out var id) ? id : null;

    /// <summary>
    /// Files a plugin into a folder, replacing whatever it was in. Passing null unfiles it —
    /// the entry is removed rather than stored as null, so the file only ever holds real
    /// assignments.
    /// </summary>
    public void SetFolder(string baseName, string? folderId, bool save = true)
    {
        var key = NormalizeKey(baseName);

        if (string.IsNullOrWhiteSpace(folderId))
        {
            _assignments.Remove(key);
        }
        else
        {
            _assignments[key] = folderId;
        }

        if (save)
        {
            Save();
        }
    }

    /// <summary>Base names filed directly in this folder — not counting sub-folders.</summary>
    public IReadOnlyList<string> GetMembers(string folderId) =>
        _assignments.Where(kv => string.Equals(kv.Value, folderId, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .ToList();

    /// <summary>How many plugins are filed directly in this folder. Drives the header counts.</summary>
    public int CountFor(string folderId) =>
        _assignments.Count(kv => string.Equals(kv.Value, folderId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Unfiles everything in a folder — used when it is deleted. Callers deleting a whole subtree
    /// should pass each descendant id; this does not recurse on its own, since it has no view of
    /// the folder definitions.
    /// </summary>
    public void ClearFolder(string folderId, bool save = true)
    {
        foreach (var key in _assignments.Where(kv => string.Equals(kv.Value, folderId, StringComparison.OrdinalIgnoreCase))
                                        .Select(kv => kv.Key)
                                        .ToList())
        {
            _assignments.Remove(key);
        }

        if (save)
        {
            Save();
        }
    }

    /// <summary>
    /// Drops assignments pointing at folders that no longer exist. A stale id would otherwise
    /// leave a plugin invisible — filed into nothing the UI can render — so this runs on load.
    /// </summary>
    public void PruneMissingFolders(IEnumerable<PluginFolder> folders, bool save = true)
    {
        var known = new HashSet<string>(folders.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var key in _assignments.Where(kv => !known.Contains(kv.Value))
                                        .Select(kv => kv.Key)
                                        .ToList())
        {
            _assignments.Remove(key);
            removed++;
        }

        if (removed > 0 && save)
        {
            Save();
        }
    }

    public void Reload()
    {
        _assignments.Clear();
        foreach (var (key, value) in Load())
        {
            _assignments[key] = value;
        }
    }

    // ---- tree helpers -------------------------------------------------------
    // These operate on the folder definitions (which live in LibraryData), so they are static:
    // this service owns assignments, not the tree itself.

    /// <summary>Direct children of a folder, or of the root when parentId is null, in display order.</summary>
    public static IReadOnlyList<PluginFolder> GetChildren(IEnumerable<PluginFolder> folders, string? parentId) =>
        folders.Where(f => string.Equals(f.ParentId, parentId, StringComparison.OrdinalIgnoreCase)
                           || (parentId is null && string.IsNullOrEmpty(f.ParentId)))
               .OrderBy(f => f.SortOrder)
               .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>
    /// A folder and every folder beneath it. Used for recursive counts and for deleting a subtree.
    /// Iterative rather than recursive, and guarded by a visited set, so a malformed file that
    /// somehow contains a cycle cannot hang the app.
    /// </summary>
    public static IReadOnlyList<PluginFolder> GetSubtree(IEnumerable<PluginFolder> folders, string folderId)
    {
        var all = folders.ToList();
        var result = new List<PluginFolder>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(folderId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id))
            {
                continue;
            }

            var folder = all.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
            if (folder is null)
            {
                continue;
            }

            result.Add(folder);

            foreach (var child in all.Where(f => string.Equals(f.ParentId, id, StringComparison.OrdinalIgnoreCase)))
            {
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether re-parenting <paramref name="folderId"/> under <paramref name="newParentId"/> would
    /// make it its own ancestor. Dropping a folder onto its own descendant is an easy thing to do
    /// by accident in a drag-and-drop tree, and the resulting cycle would detach the whole branch
    /// from the root and hang any naive walk — so the move is refused instead.
    /// </summary>
    public static bool WouldCreateCycle(IEnumerable<PluginFolder> folders, string folderId, string? newParentId)
    {
        if (string.IsNullOrWhiteSpace(newParentId))
        {
            return false;
        }

        if (string.Equals(folderId, newParentId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var all = folders.ToList();
        var current = newParentId;
        var guard = 0;

        // Walk up from the proposed parent: if we reach the folder being moved, it is an
        // ancestor of its own new parent. The guard bounds the walk in case the stored data is
        // already cyclic.
        while (!string.IsNullOrWhiteSpace(current) && guard++ <= all.Count + 1)
        {
            if (string.Equals(current, folderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = all.FirstOrDefault(f => string.Equals(f.Id, current, StringComparison.OrdinalIgnoreCase))?.ParentId;
        }

        return false;
    }

    /// <summary>
    /// Breadcrumb path from the root down to this folder ("Production / Mixing / EQ"). Bounded
    /// the same way as <see cref="WouldCreateCycle"/> so bad data degrades to a short path
    /// instead of looping.
    /// </summary>
    public static string GetPath(IEnumerable<PluginFolder> folders, string folderId, string separator = " / ")
    {
        var all = folders.ToList();
        var parts = new List<string>();
        var current = all.FirstOrDefault(f => string.Equals(f.Id, folderId, StringComparison.OrdinalIgnoreCase));
        var guard = 0;

        while (current is not null && guard++ <= all.Count + 1)
        {
            parts.Insert(0, current.Name);
            current = string.IsNullOrEmpty(current.ParentId)
                ? null
                : all.FirstOrDefault(f => string.Equals(f.Id, current.ParentId, StringComparison.OrdinalIgnoreCase));
        }

        return string.Join(separator, parts);
    }

    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return loaded is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Same trade-off the other stores make: losing the filing is bad, but this is read
            // during start-up, so throwing would stop the app opening at all. The damaged file is
            // set aside for recovery instead of destroyed.
            JsonFileStore.Quarantine(_filePath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save() => JsonFileStore.Write(_filePath, _assignments, SerializerOptions);
}
