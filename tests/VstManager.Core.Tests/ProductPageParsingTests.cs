using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class ProductPageParsingTests
{
    [Theory]
    [InlineData("Serum_x64", "Serum")]
    [InlineData("FabFilter Pro-Q 4.vst3", "FabFilter Pro Q 4")]
    [InlineData("Kontakt 8 VST3", "Kontakt 8")]
    [InlineData("some_plugin_x64", "some plugin")]
    public void CleanNameForSearch_StripsFilenameNoise(string input, string expected)
    {
        Assert.Equal(expected, KvrLookupService.CleanNameForSearch(input));
    }

    [Fact]
    public void BuildSearchQueries_WithVendor_TriesVendorQualifiedFirst()
    {
        var queries = KvrLookupService.BuildSearchQueries("Kontakt 8", "Native Instruments");

        Assert.Equal("Kontakt 8 Native Instruments", queries[0]);
        Assert.Contains("Kontakt 8", queries);
        // Versioned installs are often listed under the unversioned product name.
        Assert.Contains("Kontakt", queries);
    }

    [Fact]
    public void BuildSearchQueries_NoDuplicates()
    {
        var queries = KvrLookupService.BuildSearchQueries("Diva", null);

        Assert.Equal(queries.Distinct(StringComparer.OrdinalIgnoreCase).Count(), queries.Count);
    }

    [Fact]
    public void ExtractAllProductLinks_HandlesBothBraveAndDuckDuckGoShapes()
    {
        var html = """
            <a href="https://www.kvraudio.com/product/serum-2-by-xfer-records">Serum 2</a>
            <a href="https://www.kvraudio.com/product/serum-2-by-xfer-records/reviews/42">Reviews</a>
            <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2Fdiva%2Dby%2Du%2Dhe&amp;rut=x">Diva</a>
            """;

        var links = KvrLookupService.ExtractAllProductLinks(html);

        // The /reviews/ sub-path collapses onto the product page and de-duplicates.
        Assert.Equal(2, links.Count);
        Assert.Contains("https://www.kvraudio.com/product/serum-2-by-xfer-records", links);
        Assert.Contains("https://www.kvraudio.com/product/diva-by-u-he", links);
    }

    [Fact]
    public void ParseAnyProductPage_KvrUrl_UsesRichKvrParserIncludingVersion()
    {
        var html = """
            <html><head><title>Pro-Q 4 by FabFilter - EQ Plugin</title></head>
            <body><div id="verwin">4.13</div></body></html>
            """;

        var result = KvrLookupService.ParseAnyProductPage(html, "https://www.kvraudio.com/product/pro-q-4-by-fabfilter");

        Assert.NotNull(result);
        Assert.Equal("Pro-Q 4", result!.ProductName);
        Assert.Equal("FabFilter", result.Vendor);
        Assert.Equal("4.13", result.LatestVersion);
        Assert.Equal("https://www.kvraudio.com/product/pro-q-4-by-fabfilter", result.SourceUrl);
    }

    [Fact]
    public void ParseAnyProductPage_NonKvrSite_FallsBackToOpenGraph()
    {
        var html = """
            <html><head>
            <meta property="og:title" content="Phase Plant by Kilohearts">
            <meta property="og:image" content="https://cdn.example.com/phaseplant.jpg">
            <meta property="og:site_name" content="Example Plugins">
            </head><body>Version 2.4.6 released</body></html>
            """;

        var result = KvrLookupService.ParseAnyProductPage(html, "https://example.com/products/phase-plant");

        Assert.NotNull(result);
        Assert.Equal("Phase Plant", result!.ProductName);
        Assert.Equal("Kilohearts", result.Vendor);
        Assert.Equal("https://cdn.example.com/phaseplant.jpg", result.LogoUrl);
        Assert.Equal("2.4.6", result.LatestVersion);
    }

    [Fact]
    public void ParseAnyProductPage_NoOpenGraph_UsesTitleTag()
    {
        var html = "<html><head><title>FabFilter Pro-Q 4 - Equalizer Plug-In</title></head></html>";

        var result = KvrLookupService.ParseAnyProductPage(html, "https://www.fabfilter.com/products/pro-q-4");

        Assert.NotNull(result);
        Assert.Equal("FabFilter Pro-Q 4", result!.ProductName);
    }

    [Fact]
    public void ParseAnyProductPage_RelativeOgImage_IsResolvedToAbsolute()
    {
        var html = """
            <html><head>
            <meta property="og:title" content="Thing by Vendor">
            <meta property="og:image" content="/img/thing.png">
            </head></html>
            """;

        var result = KvrLookupService.ParseAnyProductPage(html, "https://example.com/products/thing");

        Assert.Equal("https://example.com/img/thing.png", result!.LogoUrl);
    }

    [Fact]
    public void ParseAnyProductPage_NoUsableTitle_ReturnsNull()
    {
        Assert.Null(KvrLookupService.ParseAnyProductPage("<html><body>nothing</body></html>", "https://example.com/x"));
    }

    [Fact]
    public void SplitTitle_StripsTrailingSiteName()
    {
        var (name, _) = KvrLookupService.SplitTitle("Pro-Q 4 | Pluginboutique", "Pluginboutique");

        Assert.Equal("Pro-Q 4", name);
    }

    [Theory]
    [InlineData("<p>Version 1.2.3</p>", "1.2.3")]
    [InlineData("<p>version: v2.0</p>", "2.0")]
    [InlineData("<p>no version here</p>", null)]
    [InlineData("<p>Buy 2 for 10 dollars</p>", null)]
    public void ExtractLooseVersion_OnlyMatchesExplicitVersionLabels(string html, string? expected)
    {
        Assert.Equal(expected, KvrLookupService.ExtractLooseVersion(html));
    }
}
