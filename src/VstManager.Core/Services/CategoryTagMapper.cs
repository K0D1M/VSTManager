namespace VstManager.Core.Services;

/// <summary>
/// Turns KVR's category words into preset tag ids. KVR product titles carry a category line
/// ("Pro-Q 4 by FabFilter - EQ Plugin VST VST3 ..."), which is the only free source of "what is
/// this plugin" the app has.
///
/// Deliberately conservative: a word that isn't recognised is ignored rather than guessed at.
/// A wrong tag is worse than a missing one, because the user has to notice it to undo it.
/// </summary>
public static class CategoryTagMapper
{
    private static readonly Dictionary<string, string> WordToTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["synth"] = PresetTags.Synth,
        ["synthesizer"] = PresetTags.Synth,
        ["synthesiser"] = PresetTags.Synth,
        ["subtractive"] = PresetTags.Synth,
        ["additive"] = PresetTags.Synth,
        ["granular"] = PresetTags.Synth,

        ["wavetable"] = PresetTags.Wavetable,
        ["fm"] = PresetTags.Fm,
        ["analog"] = PresetTags.Analog,
        ["analogue"] = PresetTags.Analog,
        ["virtual"] = PresetTags.Analog,

        ["sampler"] = PresetTags.Sampler,
        ["sample"] = PresetTags.Sampler,
        ["sampled"] = PresetTags.Sampler,
        ["rompler"] = PresetTags.Rompler,
        ["workstation"] = PresetTags.Rompler,

        ["drum"] = PresetTags.DrumMachine,
        ["drums"] = PresetTags.DrumMachine,
        ["percussion"] = PresetTags.DrumMachine,
        ["beat"] = PresetTags.DrumMachine,

        ["piano"] = PresetTags.Keys,
        ["keyboard"] = PresetTags.Keys,
        ["keys"] = PresetTags.Keys,
        ["organ"] = PresetTags.Keys,
        ["rhodes"] = PresetTags.Keys,

        ["orchestral"] = PresetTags.Orchestral,
        ["orchestra"] = PresetTags.Orchestral,
        ["strings"] = PresetTags.Orchestral,
        ["brass"] = PresetTags.Orchestral,
        ["cinematic"] = PresetTags.Orchestral,

        ["vocal"] = PresetTags.Vocal,
        ["vocals"] = PresetTags.Vocal,
        ["voice"] = PresetTags.Vocal,
        ["choir"] = PresetTags.Vocal,

        ["eq"] = PresetTags.Eq,
        ["equaliser"] = PresetTags.Eq,
        ["equalizer"] = PresetTags.Eq,

        ["compressor"] = PresetTags.Compressor,
        ["compression"] = PresetTags.Compressor,
        ["dynamics"] = PresetTags.Compressor,
        ["gate"] = PresetTags.Compressor,
        ["expander"] = PresetTags.Compressor,

        ["limiter"] = PresetTags.Limiter,
        ["maximizer"] = PresetTags.Limiter,
        ["maximiser"] = PresetTags.Limiter,

        ["reverb"] = PresetTags.Reverb,
        ["convolution"] = PresetTags.Reverb,
        ["room"] = PresetTags.Reverb,

        ["delay"] = PresetTags.Delay,
        ["echo"] = PresetTags.Delay,

        ["chorus"] = PresetTags.Modulation,
        ["flanger"] = PresetTags.Modulation,
        ["phaser"] = PresetTags.Modulation,
        ["modulation"] = PresetTags.Modulation,
        ["tremolo"] = PresetTags.Modulation,

        ["distortion"] = PresetTags.Distortion,
        ["saturation"] = PresetTags.Distortion,
        ["overdrive"] = PresetTags.Distortion,
        ["amp"] = PresetTags.Distortion,
        ["bitcrusher"] = PresetTags.Distortion,
        ["clipper"] = PresetTags.Distortion,

        ["filter"] = PresetTags.Filter,
        ["autofilter"] = PresetTags.Filter,

        ["pitch"] = PresetTags.Pitch,
        ["autotune"] = PresetTags.Pitch,
        ["harmonizer"] = PresetTags.Pitch,
        ["harmoniser"] = PresetTags.Pitch,
        ["vocoder"] = PresetTags.Pitch,

        // Observed live on KVR product pages and initially unmapped: "Mastering Suite",
        // "Multiband Compressor", "Stereo Imaging", "Transient Shaper", "Exciter".
        ["mastering"] = PresetTags.Utility,
        ["multiband"] = PresetTags.Compressor,
        ["transient"] = PresetTags.Compressor,
        ["stereo"] = PresetTags.Utility,
        ["imaging"] = PresetTags.Utility,
        ["exciter"] = PresetTags.Distortion,
        ["enhancer"] = PresetTags.Distortion,
        ["tape"] = PresetTags.Distortion,
        ["player"] = PresetTags.Sampler,
        ["library"] = PresetTags.Sampler,
        ["utility"] = PresetTags.Utility,
        ["analyser"] = PresetTags.Utility,
        ["analyzer"] = PresetTags.Utility,
        ["metering"] = PresetTags.Utility,
        ["meter"] = PresetTags.Utility,
        ["tuner"] = PresetTags.Utility
    };

    /// <summary>
    /// Maps category words to preset tag ids, in the order first recognised and without
    /// duplicates. Returns an empty list when nothing is recognised.
    /// </summary>
    public static IReadOnlyList<string> Map(IEnumerable<string> categoryWords)
    {
        var tagIds = new List<string>();

        foreach (var word in categoryWords)
        {
            if (WordToTag.TryGetValue(word.Trim(), out var tagId) && !tagIds.Contains(tagId))
            {
                tagIds.Add(tagId);
            }
        }

        return tagIds;
    }
}
