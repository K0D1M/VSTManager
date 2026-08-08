using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// The built-in tag set, seeded into a library the first time it's opened after this feature
/// lands. Ids are stable slugs — <see cref="CategoryTagMapper"/> maps KVR's category words onto
/// them, so they must not be renamed even though the display names can be.
/// </summary>
public static class PresetTags
{
    public const string Synth = "synth";
    public const string Wavetable = "wavetable";
    public const string Fm = "fm";
    public const string Analog = "analog";
    public const string Sampler = "sampler";
    public const string Rompler = "rompler";
    public const string DrumMachine = "drum-machine";
    public const string Keys = "keys";
    public const string Orchestral = "orchestral";
    public const string Vocal = "vocal";

    public const string Eq = "eq";
    public const string Compressor = "compressor";
    public const string Limiter = "limiter";
    public const string Reverb = "reverb";
    public const string Delay = "delay";
    public const string Modulation = "modulation";
    public const string Distortion = "distortion";
    public const string Filter = "filter";
    public const string Pitch = "pitch";
    public const string Utility = "utility";

    /// <summary>
    /// Instruments run warm (violet through amber), effects cool (blue through teal), so the two
    /// families stay distinguishable at chip size without reading the label.
    /// </summary>
    public static IReadOnlyList<TagDefinition> All { get; } = new List<TagDefinition>
    {
        new() { Id = Synth,       Name = "Synth",        ColorHex = "#FF8A5CF6", IsPreset = true },
        new() { Id = Wavetable,   Name = "Wavetable",    ColorHex = "#FF9D6BF8", IsPreset = true },
        new() { Id = Fm,          Name = "FM",           ColorHex = "#FFB07AF9", IsPreset = true },
        new() { Id = Analog,      Name = "Analog",       ColorHex = "#FFC77DDB", IsPreset = true },
        new() { Id = Sampler,     Name = "Sampler",      ColorHex = "#FFE0699B", IsPreset = true },
        new() { Id = Rompler,     Name = "Rompler",      ColorHex = "#FFE8697A", IsPreset = true },
        new() { Id = DrumMachine, Name = "Drum Machine", ColorHex = "#FFE87C5A", IsPreset = true },
        new() { Id = Keys,        Name = "Piano / Keys", ColorHex = "#FFE0913F", IsPreset = true },
        new() { Id = Orchestral,  Name = "Orchestral",   ColorHex = "#FFD4A62F", IsPreset = true },
        new() { Id = Vocal,       Name = "Vocal",        ColorHex = "#FFC2B037", IsPreset = true },

        new() { Id = Eq,          Name = "EQ",           ColorHex = "#FF3B9EFF", IsPreset = true },
        new() { Id = Compressor,  Name = "Compressor",   ColorHex = "#FF4FA8F0", IsPreset = true },
        new() { Id = Limiter,     Name = "Limiter",      ColorHex = "#FF3FB0D9", IsPreset = true },
        new() { Id = Reverb,      Name = "Reverb",       ColorHex = "#FF35B8C4", IsPreset = true },
        new() { Id = Delay,       Name = "Delay",        ColorHex = "#FF2FBDAC", IsPreset = true },
        new() { Id = Modulation,  Name = "Modulation",   ColorHex = "#FF35BF93", IsPreset = true },
        new() { Id = Distortion,  Name = "Distortion",   ColorHex = "#FF52BE72", IsPreset = true },
        new() { Id = Filter,      Name = "Filter",       ColorHex = "#FF6FBC5C", IsPreset = true },
        new() { Id = Pitch,       Name = "Pitch",        ColorHex = "#FF8ABB4C", IsPreset = true },
        new() { Id = Utility,     Name = "Utility",      ColorHex = "#FF8B949E", IsPreset = true }
    };

    /// <summary>
    /// Adds any presets the library doesn't have yet, leaving existing ones (and any renames or
    /// recolours applied to them) untouched. Returns true when something was added, so the
    /// caller knows to save.
    /// </summary>
    public static bool EnsureSeeded(List<TagDefinition> tags)
    {
        var added = false;

        foreach (var preset in All)
        {
            if (tags.Any(t => string.Equals(t.Id, preset.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            tags.Add(new TagDefinition
            {
                Id = preset.Id,
                Name = preset.Name,
                ColorHex = preset.ColorHex,
                IsPreset = true
            });
            added = true;
        }

        return added;
    }
}
