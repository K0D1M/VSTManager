namespace VstManager.Core.Models;

/// <summary>
/// One tag a plugin can carry. Covers both what a plugin *is* (Synth, EQ, Sampler — seeded as
/// presets and auto-detected from KVR) and whatever the user wants to track themselves, since
/// there's no useful line between the two: both are just labels a plugin can hold several of.
/// </summary>
public class TagDefinition
{
    /// <summary>Stable slug. Assignments reference this, so renaming a tag keeps them intact.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#FF8B949E";

    /// <summary>
    /// True for the built-in set. Presets can be renamed and recoloured but not deleted — the
    /// KVR category mapping targets their ids, so deleting one would silently break auto-tagging.
    /// </summary>
    public bool IsPreset { get; set; }
}
