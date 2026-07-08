using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class PluginDisplayBuilderTests
{
    [Fact]
    public void Build_CatalogEntryWithMatchingInstall_IsMarkedInstalled()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2 }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.True(result[0].IsInstalled);
    }

    [Fact]
    public void Build_CatalogEntryWithoutInstall_IsMarkedNotInstalled()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, new List<PluginInfo>());

        Assert.Single(result);
        Assert.False(result[0].IsInstalled);
    }

    [Fact]
    public void Build_InstalledPluginNotInCatalog_AppearsAsExtraEntry()
    {
        var catalog = new List<CatalogEntry>();
        var installed = new List<PluginInfo>
        {
            new() { Name = "SomeUnknownPlugin", Path = @"C:\vst2\Unknown.dll", Format = PluginFormat.Vst2 }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.True(result[0].IsInstalled);
        Assert.Null(result[0].Catalog);
    }
}
