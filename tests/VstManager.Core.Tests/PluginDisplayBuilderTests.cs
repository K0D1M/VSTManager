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

    [Fact]
    public void Build_Vst2AndVst3OfSamePlugin_GroupedIntoSingleItemWithBothInstalls()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Omnisphere", Vendor = "Spectrasonics", LogoUrl = "https://example.com/omnisphere.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Omnisphere", Path = @"C:\vst2\Omnisphere.dll", Format = PluginFormat.Vst2 },
            new() { Name = "Omnisphere", Path = @"C:\vst3\Omnisphere.vst3", Format = PluginFormat.Vst3 }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.Equal(2, result[0].Installs.Count);
        Assert.NotNull(result[0].Catalog);
        Assert.Equal("Omnisphere", result[0].Catalog!.Name);
    }

    [Fact]
    public void Build_VersionedInstall_GroupsUnderUnversionedCatalogEntry()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Absynth", Vendor = "Native Instruments", LogoUrl = "https://example.com/absynth.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Absynth 6", Path = @"C:\vst3\Absynth 6.vst3", Format = PluginFormat.Vst3 }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.True(result[0].IsInstalled);
        Assert.Equal("Absynth", result[0].Name);
        Assert.Single(result[0].Installs);
    }

    [Fact]
    public void Build_BothLegitAndCrackedCopies_TagSummaryIsBoth()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Legit },
            new() { Name = "Serum", Path = @"C:\vst3\Serum.vst3", Format = PluginFormat.Vst3, Tag = PluginTag.Cracked }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.Equal(PluginTagSummary.Both, result[0].TagSummary);
    }

    [Fact]
    public void Build_UnclassifiedInstall_KindSummaryIsUnclassified()
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
        Assert.Equal(PluginKind.Unclassified, result[0].KindSummary);
    }

    [Fact]
    public void Build_AllCopiesMarkedInstrument_KindSummaryIsInstrument()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Kind = PluginKind.Instrument },
            new() { Name = "Serum", Path = @"C:\vst3\Serum.vst3", Format = PluginFormat.Vst3, Kind = PluginKind.Instrument }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.Equal(PluginKind.Instrument, result[0].KindSummary);
    }

    [Fact]
    public void Build_CopiesWithDisagreeingKind_KindSummaryFallsBackToUnclassified()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Kind = PluginKind.Instrument },
            new() { Name = "Serum", Path = @"C:\vst3\Serum.vst3", Format = PluginFormat.Vst3, Kind = PluginKind.Effect }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.Equal(PluginKind.Unclassified, result[0].KindSummary);
    }

    [Fact]
    public void Build_OneCopyFavorited_IsFavoriteSummaryIsTrue()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsFavorite = false },
            new() { Name = "Serum", Path = @"C:\vst3\Serum.vst3", Format = PluginFormat.Vst3, IsFavorite = true }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.True(result[0].IsFavoriteSummary);
    }

    [Fact]
    public void Build_NoCopyFavorited_IsFavoriteSummaryIsFalse()
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
        Assert.False(result[0].IsFavoriteSummary);
    }

    [Fact]
    public void Build_OneCopyHidden_IsHiddenSummaryIsTrue()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsHidden = false },
            new() { Name = "Serum", Path = @"C:\vst3\Serum.vst3", Format = PluginFormat.Vst3, IsHidden = true }
        };

        var builder = new PluginDisplayBuilder();
        var result = builder.Build(catalog, installed);

        Assert.Single(result);
        Assert.True(result[0].IsHiddenSummary);
    }

    [Fact]
    public void Build_NoCopyHidden_IsHiddenSummaryIsFalse()
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
        Assert.False(result[0].IsHiddenSummary);
    }

    [Fact]
    public void Build_CatalogMatchedPlugin_BaseNameIsCatalogEntryName()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/serum.png" }
        };
        var installed = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2 }
        };

        var result = new PluginDisplayBuilder().Build(catalog, installed);

        Assert.Equal("Serum", result[0].BaseName);
    }

    [Fact]
    public void Build_UncataloguedPlugin_BaseNameIsRawScannedName()
    {
        var installed = new List<PluginInfo>
        {
            new() { Name = "SomeUnknownPlugin", Path = @"C:\vst2\Unknown.dll", Format = PluginFormat.Vst2 }
        };

        var result = new PluginDisplayBuilder().Build(new List<CatalogEntry>(), installed);

        Assert.Equal("SomeUnknownPlugin", result[0].BaseName);
    }

    [Fact]
    public void ApplyManualOverrides_NameOverridePresent_ReplacesName()
    {
        var items = new List<PluginDisplayItem>
        {
            new() { Name = "VPS Avenger_x64", BaseName = "VPS Avenger_x64" }
        };
        var overrides = new ManualMetadataOverrideService(TempOverridePath());
        overrides.SetOverride("VPS Avenger_x64", "Avenger", null);

        new PluginDisplayBuilder().ApplyManualOverrides(items, overrides);

        Assert.Equal("Avenger", items[0].Name);
    }

    [Fact]
    public void ApplyManualOverrides_VendorOverridePresent_ReplacesVendor()
    {
        var items = new List<PluginDisplayItem>
        {
            new() { Name = "Avenger", Vendor = null, BaseName = "Avenger" }
        };
        var overrides = new ManualMetadataOverrideService(TempOverridePath());
        overrides.SetOverride("Avenger", null, "Vengeance Sound");

        new PluginDisplayBuilder().ApplyManualOverrides(items, overrides);

        Assert.Equal("Vengeance Sound", items[0].Vendor);
    }

    [Fact]
    public void ApplyManualOverrides_BothFieldsOverridden_ReplacesBoth()
    {
        var items = new List<PluginDisplayItem>
        {
            new() { Name = "uaudio_1176", BaseName = "uaudio_1176" }
        };
        var overrides = new ManualMetadataOverrideService(TempOverridePath());
        overrides.SetOverride("uaudio_1176", "1176 Classic Limiter Collection", "Universal Audio");

        new PluginDisplayBuilder().ApplyManualOverrides(items, overrides);

        Assert.Equal("1176 Classic Limiter Collection", items[0].Name);
        Assert.Equal("Universal Audio", items[0].Vendor);
    }

    [Fact]
    public void ApplyManualOverrides_NoMatchingBaseName_IsNoOp()
    {
        var items = new List<PluginDisplayItem>
        {
            new() { Name = "Serum", BaseName = "Serum" }
        };
        var overrides = new ManualMetadataOverrideService(TempOverridePath());
        overrides.SetOverride("SomethingElse", "Renamed", null);

        new PluginDisplayBuilder().ApplyManualOverrides(items, overrides);

        Assert.Equal("Serum", items[0].Name);
    }

    [Fact]
    public void ApplyManualOverrides_TwoUncataloguedInstallsSharingBaseName_BothReceiveTheOverride()
    {
        // Documents a known, pre-existing characteristic: installs that already collapsed onto
        // one PluginDisplayItem (identical normalized raw name) share a single override target.
        var sharedItem = new PluginDisplayItem { Name = "SomeUnknownPlugin", BaseName = "SomeUnknownPlugin" };
        sharedItem.Installs.Add(new PluginInfo { Name = "SomeUnknownPlugin", Path = @"C:\a.dll", Format = PluginFormat.Vst2 });
        sharedItem.Installs.Add(new PluginInfo { Name = "SomeUnknownPlugin", Path = @"C:\b.dll", Format = PluginFormat.Vst2 });
        var items = new List<PluginDisplayItem> { sharedItem };

        var overrides = new ManualMetadataOverrideService(TempOverridePath());
        overrides.SetOverride("SomeUnknownPlugin", "Renamed Plugin", null);

        new PluginDisplayBuilder().ApplyManualOverrides(items, overrides);

        Assert.Equal("Renamed Plugin", items[0].Name);
        Assert.Equal(2, items[0].Installs.Count);
    }

    /// <summary>
    /// The core guarantee behind "remember the plugin after it's uninstalled": the item reads
    /// as not installed, yet every classification the user made is still reported.
    /// </summary>
    [Fact]
    public void Build_OnlyUninstalledCopy_IsNotInstalledButKeepsAllMetadata()
    {
        var plugins = new List<PluginInfo>
        {
            new()
            {
                Name = "GhostSynth", Path = @"C:\vst2\GhostSynth.dll", Format = PluginFormat.Vst2,
                Tag = PluginTag.Cracked, Kind = PluginKind.Instrument,
                CurrentVersion = "2.0", IsFavorite = true, IsUninstalled = true
            }
        };

        var item = Assert.Single(new PluginDisplayBuilder().Build(new List<CatalogEntry>(), plugins));

        Assert.False(item.IsInstalled);
        Assert.True(item.IsRemembered);
        Assert.Equal(PluginTagSummary.Cracked, item.TagSummary);
        Assert.Equal(PluginKind.Instrument, item.KindSummary);
        Assert.True(item.IsFavoriteSummary);
        Assert.Empty(item.ActiveInstalls);
        Assert.Single(item.RememberedInstalls);
    }

    [Fact]
    public void Build_UninstalledCopyOfCatalogPlugin_GroupsOntoEntryInsteadOfDuplicating()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Serum", Vendor = "Xfer Records", LogoUrl = "https://example.com/s.png" }
        };
        var plugins = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsUninstalled = true }
        };

        // The catalog pass must not also append a bare "never installed" Serum alongside it.
        var item = Assert.Single(new PluginDisplayBuilder().Build(catalog, plugins));

        Assert.True(item.IsRemembered);
        Assert.Equal("Serum", item.Name);
    }

    [Fact]
    public void Build_OneLiveAndOneUninstalledCopy_CountsAsInstalled()
    {
        var plugins = new List<PluginInfo>
        {
            new() { Name = "Thing", Path = @"C:\vst2\Thing.dll", Format = PluginFormat.Vst2, IsUninstalled = true },
            new() { Name = "Thing", Path = @"C:\vst3\Thing.vst3", Format = PluginFormat.Vst3 }
        };

        var item = Assert.Single(new PluginDisplayBuilder().Build(new List<CatalogEntry>(), plugins));

        Assert.True(item.IsInstalled);
        Assert.False(item.IsRemembered);
        Assert.Equal(2, item.Installs.Count);
        Assert.Equal(PluginFormat.Vst3, Assert.Single(item.ActiveInstalls).Format);
    }

    [Fact]
    public void Build_MixedCopies_RememberedCopyStillContributesToTagSummary()
    {
        // Deliberate: a remembered copy keeps influencing the summary, which is what makes its
        // classification survive at all.
        var plugins = new List<PluginInfo>
        {
            new() { Name = "Thing", Path = @"C:\vst2\Thing.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Cracked, IsUninstalled = true },
            new() { Name = "Thing", Path = @"C:\vst3\Thing.vst3", Format = PluginFormat.Vst3, Tag = PluginTag.Legit }
        };

        var item = Assert.Single(new PluginDisplayBuilder().Build(new List<CatalogEntry>(), plugins));

        Assert.Equal(PluginTagSummary.Both, item.TagSummary);
    }

    [Fact]
    public void Build_CatalogEntryNeverInstalled_IsNeitherInstalledNorRemembered()
    {
        var catalog = new List<CatalogEntry>
        {
            new() { Name = "Massive", Vendor = "Native Instruments", LogoUrl = "https://example.com/m.png" }
        };

        var item = Assert.Single(new PluginDisplayBuilder().Build(catalog, new List<PluginInfo>()));

        Assert.False(item.IsInstalled);
        Assert.False(item.IsRemembered);
    }

    private static string TempOverridePath() =>
        Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid(), "manual-metadata.json");
}
