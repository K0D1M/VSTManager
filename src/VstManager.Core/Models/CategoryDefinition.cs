namespace VstManager.Core.Models;

/// <summary>
/// One category a plugin can belong to — modeled directly on TagDefinition, since a category is
/// the same shape (a stable id, a display name, a "can't be deleted" flag for built-ins) plus one
/// addition: an icon, since categories drive the main grid's grouped sections and each section
/// needs a glyph the way Instruments/Effects already do.
/// </summary>
public class CategoryDefinition
{
    /// <summary>Stable slug. Plugin assignments (PluginInfo.CategoryId) reference this, so
    /// renaming a category keeps them intact.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>A single emoji, matching this project's existing section-header icon convention
    /// (🎹 Instruments, 🎛 Effects) rather than a vector icon or color swatch.</summary>
    public string Icon { get; set; } = "❔";

    /// <summary>
    /// True for Instrument and Effect. Presets can't be deleted — they're the two categories the
    /// rest of the app (Kind-based filtering, the mandatory new-plugin window) already assumes
    /// exist, matching how TagDefinition protects its own built-ins.
    /// </summary>
    public bool IsPreset { get; set; }

    /// <summary>Position among the user's own categories; presets ignore it and always lead.</summary>
    public int SortOrder { get; set; }
}
