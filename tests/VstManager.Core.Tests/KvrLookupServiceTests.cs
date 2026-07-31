using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class KvrLookupServiceTests
{
    [Fact]
    public void ExtractFirstDuckDuckGoLink_DecodesFirstDuckDuckGoRedirectToKvrProduct()
    {
        var html = """
            <div class="results">
                <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fforum%2Fviewtopic%2Ephp%253Ft%253D1234&amp;rut=abc">Some forum thread</a>
                <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2Fpro%2Dq%2D4%2Dby%2Dfabfilter&amp;rut=def">Pro-Q 4 by FabFilter</a>
                <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2Fpro%2Dq%2D3%2Dby%2Dfabfilter&amp;rut=ghi">Pro-Q 3 by FabFilter</a>
            </div>
            """;

        var result = KvrLookupService.ExtractFirstDuckDuckGoLink(html);

        Assert.Equal("https://www.kvraudio.com/product/pro-q-4-by-fabfilter", result);
    }

    [Fact]
    public void ExtractFirstDuckDuckGoLink_SkipsNonProductResultsToFindLaterProductLink()
    {
        var html = """
            <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fforum%2Fviewtopic%2Ephp&amp;rut=abc">Forum</a>
            <a href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fproduct%2Fserum%2D2%2Dby%2Dxfer%2Drecords&amp;rut=def">Serum 2</a>
            """;

        var result = KvrLookupService.ExtractFirstDuckDuckGoLink(html);

        Assert.Equal("https://www.kvraudio.com/product/serum-2-by-xfer-records", result);
    }

    [Fact]
    public void ExtractFirstDuckDuckGoLink_NoProductLinkPresent_ReturnsNull()
    {
        var html = "<a href=\"//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kvraudio.com%2Fforum%2Fviewtopic%2Ephp&amp;rut=abc\">Some forum thread</a>";

        var result = KvrLookupService.ExtractFirstDuckDuckGoLink(html);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractFirstKvrLink_PlainHrefsNoRedirectWrapper_ReturnsFirstProductLink()
    {
        // Brave's static (JS-free) search HTML links results directly, unlike DuckDuckGo's
        // redirect-wrapped uddg= links.
        var html = """
            <a href="https://www.kvraudio.com/product/serum-2-by-xfer-records">Serum 2 by Xfer Records</a>
            <a href="https://www.kvraudio.com/product/serum-2-by-xfer-records/reviews/4235">Reviews</a>
            """;

        var result = KvrLookupService.ExtractFirstKvrLink(html);

        Assert.Equal("https://www.kvraudio.com/product/serum-2-by-xfer-records", result);
    }

    [Fact]
    public void ExtractFirstKvrLink_NoKvrProductLinkPresent_ReturnsNull()
    {
        var html = "<a href=\"https://www.somesite.com/other\">Not KVR</a>";

        var result = KvrLookupService.ExtractFirstKvrLink(html);

        Assert.Null(result);
    }

    [Fact]
    public void ParseProductPage_StandardTitleFormat_ExtractsNameAndVendor()
    {
        var html = "<html><head><title>Pro-Q 4 by FabFilter - EQ Plugin VST VST3 Audio Unit AAX CLAP</title></head></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Equal("Pro-Q 4", result!.ProductName);
        Assert.Equal("FabFilter", result.Vendor);
    }

    [Fact]
    public void ParseProductPage_TitleWithoutTrailingCategory_ExtractsVendor()
    {
        var html = "<html><head><title>SRX Keyboards by Roland</title></head></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Equal("SRX Keyboards", result!.ProductName);
        Assert.Equal("Roland", result.Vendor);
    }

    [Fact]
    public void ParseProductPage_HtmlEntitiesInTitle_AreDecoded()
    {
        var html = "<html><head><title>Rock &amp; Roll Synth by Some Vendor - Instrument Plugin</title></head></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Equal("Rock & Roll Synth", result!.ProductName);
    }

    [Fact]
    public void ParseProductPage_NoTitleTag_ReturnsNull()
    {
        var html = "<html><body>No title here</body></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.Null(result);
    }

    [Fact]
    public void ParseProductPage_TitleWithoutByVendorPattern_ReturnsNull()
    {
        var html = "<html><head><title>KVR Audio - Plugins for Digital Audio Workstations</title></head></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.Null(result);
    }

    [Fact]
    public void ParseProductPage_LogoImagePresent_ExtractsFirstKvrStaticImageUrl()
    {
        var html = """
            <html>
            <head><title>Pro-Q 4 by FabFilter - EQ Plugin</title></head>
            <body>
                <img src="https://static.kvraudio.com/i/s/pro-q-3-screenshot.jpg" />
                <img src="https://static.kvraudio.com/i/b/pro-q-3-screenshot.jpg" />
            </body>
            </html>
            """;

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Equal("https://static.kvraudio.com/i/s/pro-q-3-screenshot.jpg", result!.LogoUrl);
    }

    [Fact]
    public void ParseProductPage_NoLogoImagePresent_LogoUrlIsNull()
    {
        var html = "<html><head><title>Pro-Q 4 by FabFilter - EQ Plugin</title></head></html>";

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Null(result!.LogoUrl);
    }

    [Fact]
    public void ExtractLatestVersion_WindowsVersionPresent_ExtractsIt()
    {
        // Real structure observed on kvraudio.com product pages.
        var html = """<div style="padding: 16px;" id="verwin">4.13</div><div style="padding: 16px;" id="verosx">4.12</div>""";

        Assert.Equal("4.13", KvrLookupService.ExtractLatestVersion(html));
    }

    [Fact]
    public void ExtractLatestVersion_MacOnlyPage_FallsBackToOsxVersion()
    {
        var html = """<div id="verosx">2.1.5</div>""";

        Assert.Equal("2.1.5", KvrLookupService.ExtractLatestVersion(html));
    }

    [Fact]
    public void ExtractLatestVersion_NoVersionAnchors_ReturnsNull()
    {
        var html = "<html><head><title>Pro-Q 4 by FabFilter - EQ Plugin</title></head></html>";

        Assert.Null(KvrLookupService.ExtractLatestVersion(html));
    }

    [Theory]
    [InlineData("Pro-Q 4", "pro-q-4")]
    [InlineData("FabFilter", "fabfilter")]
    [InlineData("Serum_x64", "serum")]
    [InlineData("VPS Avenger_x64", "vps-avenger")]
    [InlineData("Xfer Records", "xfer-records")]
    [InlineData("Serum 2", "serum-2")]
    [InlineData("u-he", "u-he")]
    [InlineData("Kontakt 8 VST3", "kontakt-8")]
    public void Slugify_ProducesKvrStyleSlugs(string input, string expected)
    {
        Assert.Equal(expected, KvrLookupService.Slugify(input));
    }

    [Fact]
    public void Slugify_SuffixOnlyName_KeepsLastToken()
    {
        // Never strip the final token down to nothing, even if it looks like noise.
        Assert.Equal("vst3", KvrLookupService.Slugify("VST3"));
    }

    [Fact]
    public void ParseProductPage_VersionAnchorPresent_PopulatesLatestVersion()
    {
        var html = """
            <html>
            <head><title>Pro-Q 4 by FabFilter - EQ Plugin</title></head>
            <body><div style="padding: 16px;" id="verwin">4.13</div></body>
            </html>
            """;

        var result = KvrLookupService.ParseProductPage(html);

        Assert.NotNull(result);
        Assert.Equal("4.13", result!.LatestVersion);
    }
}
