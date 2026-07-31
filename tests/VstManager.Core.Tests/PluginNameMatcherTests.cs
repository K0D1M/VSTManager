using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class PluginNameMatcherTests
{
    private static readonly List<CatalogEntry> Catalog = new()
    {
        new CatalogEntry { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" },
        new CatalogEntry { Name = "Pro-Q 3", Vendor = "FabFilter", LogoUrl = "https://example.com/proq3.png" },
        new CatalogEntry { Name = "Saturn 2", Vendor = "FabFilter", LogoUrl = "https://example.com/saturn2.png" },
        new CatalogEntry { Name = "Massive", Vendor = "Native Instruments", LogoUrl = "https://example.com/massive.png" },
        new CatalogEntry { Name = "Absynth", Vendor = "Native Instruments", LogoUrl = "https://example.com/absynth.png" },
        new CatalogEntry { Name = "Avenger", Vendor = "Vengeance Sound", LogoUrl = "https://example.com/avenger.png" },
        new CatalogEntry
        {
            Name = "1176 Classic Limiter Collection",
            Vendor = "Universal Audio",
            LogoUrl = "https://example.com/1176.png",
            Aliases = new List<string> { "uaudio_ua_1176" }
        },
        new CatalogEntry { Name = "Galaxy Tape Echo", Vendor = "Universal Audio", LogoUrl = "https://example.com/galaxy.png" },
        new CatalogEntry
        {
            Name = "PolyMAX Synth",
            Vendor = "Universal Audio",
            LogoUrl = "https://example.com/polymax.png",
            Aliases = new List<string> { "uaudio_polymax" }
        },
        new CatalogEntry
        {
            Name = "Teletronix LA-2A Leveler Collection",
            Vendor = "Universal Audio",
            LogoUrl = "https://example.com/la2a.png",
            Aliases = new List<string> { "uaudio_teletronix_la-2a" }
        }
    };

    [Fact]
    public void FindMatch_GalaxyTapeEchoFileName_MatchesViaVendorPrefixSuffix()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("uaudio_galaxy_tape_echo", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Galaxy Tape Echo", result!.Name);
    }

    [Fact]
    public void FindMatch_PolyMaxFileName_MatchesCatalogEntryByAlias()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("uaudio_polymax", Catalog);

        Assert.NotNull(result);
        Assert.Equal("PolyMAX Synth", result!.Name);
    }

    [Fact]
    public void FindMatch_TeletronixLa2aVariantFileName_MatchesCatalogEntryByAliasPrefix()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("uaudio_teletronix_la-2a_tc", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Teletronix LA-2A Leveler Collection", result!.Name);
    }

    [Fact]
    public void FindMatch_AliasedFileName_MatchesCatalogEntryByAlias()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("uaudio_ua_1176", Catalog);

        Assert.NotNull(result);
        Assert.Equal("1176 Classic Limiter Collection", result!.Name);
    }

    [Fact]
    public void FindMatch_FileNameStartingWithAlias_MatchesCatalogEntryByAliasPrefix()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("uaudio_ua_1176se", Catalog);

        Assert.NotNull(result);
        Assert.Equal("1176 Classic Limiter Collection", result!.Name);
    }

    [Fact]
    public void FindMatch_VpsAvengerFileName_MatchesCatalogVendorProductName()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("VPS Avenger", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Avenger", result!.Name);
        Assert.Equal("Vengeance Sound", result.Vendor);
    }

    [Fact]
    public void FindMatch_UadxPrefixedCollectionName_MatchesCatalogProductName()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("UADx 1176 Classic Limiter Collection", Catalog);

        Assert.NotNull(result);
        Assert.Equal("1176 Classic Limiter Collection", result!.Name);
        Assert.Equal("Universal Audio", result.Vendor);
    }

    [Fact]
    public void FindMatch_VersionedName_MatchesUnversionedEntry()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Absynth 6", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Absynth", result!.Name);
    }

    [Fact]
    public void FindMatch_VersionedNameNoSpace_MatchesUnversionedEntry()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Absynth5", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Absynth", result!.Name);
    }

    [Fact]
    public void FindMatch_NameWithNonNumericSuffix_DoesNotVersionMatch()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("MassiveX", Catalog);

        Assert.Null(result);
    }

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

    [Fact]
    public void FindMatch_VendorPrefixedName_MatchesViaSuffix()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("FabFilter Saturn 2", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Saturn 2", result!.Name);
    }

    [Fact]
    public void FindMatch_UnrelatedNameWithSimilarSuffix_DoesNotFalsePositiveMatch()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("MassiveX", Catalog);

        Assert.Null(result);
    }

    [Fact]
    public void FindMatch_ExactNameTakesPriorityOverSuffixMatch()
    {
        var matcher = new PluginNameMatcher();
        var result = matcher.FindMatch("Massive", Catalog);

        Assert.NotNull(result);
        Assert.Equal("Massive", result!.Name);
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
