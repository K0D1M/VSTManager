using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class NameSimilarityTests
{
    [Fact]
    public void Score_ExactMatch_IsOne()
    {
        Assert.Equal(1.0, NameSimilarity.Score("Serum", "Serum"));
    }

    [Fact]
    public void Score_IgnoresCaseAndPunctuation()
    {
        Assert.Equal(1.0, NameSimilarity.Score("pro-q 4", "Pro-Q 4"));
    }

    [Fact]
    public void Score_FilenameWithVendorPrefix_ScoresConfident()
    {
        // The very common real case: "FabFilter Pro-Q 4.vst3" on disk vs "Pro-Q 4" listed.
        var score = NameSimilarity.Score("FabFilter Pro-Q 4", "Pro-Q 4", "FabFilter");

        Assert.True(score >= NameSimilarity.ConfidentThreshold, $"expected confident, got {score:F2}");
    }

    [Fact]
    public void Score_UnrelatedPresetPack_ScoresBelowConfident()
    {
        // Search results are full of preset packs that merely mention the synth's name; those
        // must never be auto-applied over the real product.
        var score = NameSimilarity.Score("Serum", "Neuro Bass Patches 1 for Xfer Serum", "ArtFX Studios");

        Assert.True(score < NameSimilarity.ConfidentThreshold, $"expected not confident, got {score:F2}");
    }

    [Fact]
    public void Score_RealProductBeatsPresetPack()
    {
        var real = NameSimilarity.Score("Serum", "Serum 2", "Xfer Records");
        var pack = NameSimilarity.Score("Serum", "Fume Serum Ambient Presets", "ModeAudio");

        Assert.True(real > pack, $"real={real:F2} should beat pack={pack:F2}");
    }

    [Fact]
    public void Score_CompletelyDifferentNames_ScoreLow()
    {
        Assert.True(NameSimilarity.Score("Serum", "Kontakt") < NameSimilarity.PlausibleThreshold);
    }

    [Theory]
    [InlineData(null, "Serum")]
    [InlineData("Serum", null)]
    [InlineData("", "Serum")]
    public void Score_MissingInput_IsZero(string? a, string? b)
    {
        Assert.Equal(0, NameSimilarity.Score(a!, b!));
    }
}
