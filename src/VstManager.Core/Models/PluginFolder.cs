namespace VstManager.Core.Models;

/// <summary>
/// A user-made folder for organising plugins, nestable to any depth.
///
/// Deliberately different from <see cref="TagDefinition"/>, which the app already has: tags are
/// flat and a plugin carries as many as apply ("Synth", "Analog"). A folder is a *place* — a
/// plugin lives in exactly one, and folders contain other folders — so the two answer different
/// questions ("what is this?" versus "where did I put it?") rather than duplicating each other.
///
/// The tree is stored flat, as a list where each entry names its parent, rather than as nested
/// child lists. That keeps moving a folder to a single field write, makes a cycle detectable
/// (see PluginFolderService.WouldCreateCycle) instead of structurally impossible-to-express, and
/// means a corrupted parent id orphans one folder rather than losing an entire subtree.
/// </summary>
public class PluginFolder
{
    /// <summary>Stable id. Assignments and child folders reference this, so renaming is safe.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Null for a top-level folder; otherwise the <see cref="Id"/> of the containing folder.</summary>
    public string? ParentId { get; set; }

    public string ColorHex { get; set; } = "#FF8A5CF6";

    /// <summary>
    /// An emoji or single glyph shown beside the name. Empty means "no icon" — the UI falls back
    /// to a generic folder glyph rather than showing a blank gap.
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Order among siblings. Ties fall back to name, so the order is always defined.</summary>
    public int SortOrder { get; set; }
}
