using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class PluginNameMatcherTests
{
    private static readonly List<CatalogEntry> Catalog = new()
    {
        new CatalogEntry { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" },
        new CatalogEntry { Name = "Pro-Q 3", Vendor = "FabFilter", LogoUrl = "https://example.com/proq3.png" }
    };

    [Fact]
    public void FindMatch_ExactName_Matches()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Serum", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Serum", result!.Name);
    }

    [Fact]
    public void FindMatch_CaseInsensitive_Matches()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("SERUM", Catalog);

        Assert.NotNull(result);
    }

    [Fact]
    public void FindMatch_WithX64Suffix_Matches()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Serum_x64", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Serum", result!.Name);
    }

    [Fact]
    public void FindMatch_WithPunctuationVariance_Matches()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Pro Q3", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Pro-Q 3", result!.Name);
    }

    [Fact]
    public void FindMatch_NoMatch_ReturnsNull()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("SomeUnknownPlugin", Catalog);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Serum.dll", "serum")]
    [InlineData("Serum_x64", "serum")]
    [InlineData("Serum-VST3", "serum")]
    public void Normalize_StripsExtensionsAndSuffixes(string input, string expected)
    {
        var normalized = PluginNameMatcher.Normalize(Path.GetFileNameWithoutExtension(input));
        Assert.Equal(expected, normalized);
    }
}
