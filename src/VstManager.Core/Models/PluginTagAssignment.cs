namespace VstManager.Core.Models;

/// <summary>
/// Which tags one plugin carries. Manual and auto-detected ids are kept apart on purpose: a KVR
/// re-detection rewrites <see cref="AutoTagIds"/> wholesale, and keeping the user's own choices
/// in a separate list is what stops that from ever throwing their work away.
/// </summary>
public class PluginTagAssignment
{
    /// <summary>Tags the user applied. Never touched by auto-detection.</summary>
    public List<string> TagIds { get; set; } = new();

    /// <summary>Tags inferred from the KVR category line. Replaced wholesale on re-detection.</summary>
    public List<string> AutoTagIds { get; set; } = new();

    /// <summary>
    /// Auto-detected ids the user explicitly removed. Remembered so a later re-detection doesn't
    /// helpfully put back the tag they just deleted.
    /// </summary>
    public List<string> SuppressedAutoTagIds { get; set; } = new();

    public bool IsEmpty =>
        TagIds.Count == 0 && AutoTagIds.Count == 0 && SuppressedAutoTagIds.Count == 0;

    /// <summary>Everything the plugin effectively carries, manual first, without duplicates.</summary>
    public IEnumerable<string> AllTagIds =>
        TagIds.Concat(AutoTagIds.Where(id => !TagIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
              .Distinct(StringComparer.OrdinalIgnoreCase);
}
