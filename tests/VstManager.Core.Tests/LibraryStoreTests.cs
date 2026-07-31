using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class LibraryStoreTests : IDisposable
{
    private readonly string _tempFile;

    public LibraryStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid() + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void CheckForPluginUpdatesOnStartup_DefaultsToEnabled()
    {
        var data = new LibraryStore(_tempFile).Load();

        Assert.True(data.CheckForPluginUpdatesOnStartup);
    }

    [Fact]
    public void CheckForPluginUpdatesOnStartup_SurvivesSaveAndReload()
    {
        var store = new LibraryStore(_tempFile);
        var data = store.Load();
        data.CheckForPluginUpdatesOnStartup = false;
        store.Save(data);

        Assert.False(new LibraryStore(_tempFile).Load().CheckForPluginUpdatesOnStartup);
    }

    [Fact]
    public void CheckForPluginUpdatesOnStartup_MissingFromOlderLibraryFile_DefaultsToEnabled()
    {
        // A library.json written before this setting existed must not silently disable the
        // startup check on upgrade.
        File.WriteAllText(_tempFile, """{"Plugins":[],"IsDarkTheme":true}""");

        Assert.True(new LibraryStore(_tempFile).Load().CheckForPluginUpdatesOnStartup);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyLibrary()
    {
        var store = new LibraryStore(_tempFile);
        var data = store.Load();

        Assert.Empty(data.Plugins);
        Assert.Empty(data.CustomScanFolders);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsData()
    {
        var store = new LibraryStore(_tempFile);
        var data = new LibraryData
        {
            CustomScanFolders = new List<string> { @"D:\MyVstFolder" },
            Plugins = new List<PluginInfo>
            {
                new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Legit }
            }
        };

        store.Save(data);
        var loaded = store.Load();

        Assert.Single(loaded.CustomScanFolders);
        Assert.Equal(@"D:\MyVstFolder", loaded.CustomScanFolders[0]);
        Assert.Single(loaded.Plugins);
        Assert.Equal("Serum", loaded.Plugins[0].Name);
        Assert.Equal(PluginTag.Legit, loaded.Plugins[0].Tag);
    }

    [Fact]
    public void MergeOnRescan_PreservesTagForExistingPath()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Cracked }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Unclassified }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Single(merged);
        Assert.Equal(PluginTag.Cracked, merged[0].Tag);
    }

    [Fact]
    public void MergeOnRescan_NewPluginIsUnclassified()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>();
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Diva", Path = @"C:\vst3\Diva.vst3", Format = PluginFormat.Vst3, Tag = PluginTag.Unclassified }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Single(merged);
        Assert.Equal(PluginTag.Unclassified, merged[0].Tag);
    }

    [Fact]
    public void MergeOnRescan_PreservesKindForExistingPath()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Kind = PluginKind.Instrument }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, Kind = PluginKind.Unclassified }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Single(merged);
        Assert.Equal(PluginKind.Instrument, merged[0].Kind);
    }

    [Fact]
    public void MergeOnRescan_PreservesFavoriteForExistingPath()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsFavorite = true }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsFavorite = false }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Single(merged);
        Assert.True(merged[0].IsFavorite);
    }

    [Fact]
    public void MergeOnRescan_PreservesHiddenForExistingPath()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsHidden = true }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2, IsHidden = false }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Single(merged);
        Assert.True(merged[0].IsHidden);
    }

    // Replaces an earlier test that asserted a vanished plugin was dropped entirely. That was
    // the data-loss bug: uninstalling wiped the user's tag, kind, versions and favourite.
    [Fact]
    public void MergeOnRescan_PluginMissingFromScan_IsRetainedAndFlaggedUninstalled()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new()
            {
                Name = "Old", Path = @"C:\vst2\Old.dll", Format = PluginFormat.Vst2,
                Tag = PluginTag.Cracked, Kind = PluginKind.Instrument,
                CurrentVersion = "1.2", LatestVersion = "1.5", IsFavorite = true, IsHidden = true
            }
        };

        var merged = store.MergeOnRescan(existing, new List<PluginInfo>());

        var entry = Assert.Single(merged);
        Assert.True(entry.IsUninstalled);
        Assert.NotNull(entry.UninstalledAt);

        // The whole point: everything the user set must survive the uninstall.
        Assert.Equal(PluginTag.Cracked, entry.Tag);
        Assert.Equal(PluginKind.Instrument, entry.Kind);
        Assert.Equal("1.2", entry.CurrentVersion);
        Assert.Equal("1.5", entry.LatestVersion);
        Assert.True(entry.IsFavorite);
        Assert.True(entry.IsHidden);
    }

    [Fact]
    public void MergeOnRescan_PluginStillOnDisk_IsNotFlaggedUninstalled()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2 }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2 }
        };

        var entry = Assert.Single(store.MergeOnRescan(existing, scanned));

        Assert.False(entry.IsUninstalled);
        Assert.Null(entry.UninstalledAt);
    }

    [Fact]
    public void MergeOnRescan_RememberedPluginReappears_BecomesInstalledKeepingItsMetadata()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new()
            {
                Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2,
                Tag = PluginTag.Legit, IsFavorite = true,
                IsUninstalled = true, UninstalledAt = new DateTime(2024, 1, 1)
            }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Serum", Path = @"C:\vst2\Serum.dll", Format = PluginFormat.Vst2 }
        };

        var entry = Assert.Single(store.MergeOnRescan(existing, scanned));

        Assert.False(entry.IsUninstalled);
        Assert.Null(entry.UninstalledAt);
        Assert.Equal(PluginTag.Legit, entry.Tag);
        Assert.True(entry.IsFavorite);
    }

    [Fact]
    public void MergeOnRescan_AlreadyRememberedAndStillMissing_KeepsOriginalUninstalledAt()
    {
        var store = new LibraryStore(_tempFile);
        var firstNoticed = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var existing = new List<PluginInfo>
        {
            new()
            {
                Name = "Old", Path = @"C:\vst2\Old.dll", Format = PluginFormat.Vst2,
                IsUninstalled = true, UninstalledAt = firstNoticed
            }
        };

        var entry = Assert.Single(store.MergeOnRescan(existing, new List<PluginInfo>()));

        // Repeated rescans must not keep bumping the timestamp forward.
        Assert.Equal(firstNoticed, entry.UninstalledAt);
    }

    [Fact]
    public void MergeOnRescan_MixOfFoundAndMissing_FlagsOnlyTheMissingOne()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Kept", Path = @"C:\vst2\Kept.dll", Format = PluginFormat.Vst2 },
            new() { Name = "Gone", Path = @"C:\vst2\Gone.dll", Format = PluginFormat.Vst2 }
        };
        var scanned = new List<PluginInfo>
        {
            new() { Name = "Kept", Path = @"C:\vst2\Kept.dll", Format = PluginFormat.Vst2 }
        };

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Equal(2, merged.Count);
        Assert.False(merged.Single(p => p.Name == "Kept").IsUninstalled);
        Assert.True(merged.Single(p => p.Name == "Gone").IsUninstalled);
    }

    [Fact]
    public void IsUninstalled_MissingFromOlderLibraryFile_LoadsAsInstalled()
    {
        // A library.json written before this field existed must not read as uninstalled.
        File.WriteAllText(_tempFile,
            """{"Plugins":[{"Name":"Serum","Path":"C:\\vst2\\Serum.dll","Format":0}]}""");

        var plugin = Assert.Single(new LibraryStore(_tempFile).Load().Plugins);

        Assert.False(plugin.IsUninstalled);
        Assert.Null(plugin.UninstalledAt);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsUninstalledState()
    {
        var store = new LibraryStore(_tempFile);
        var noticed = new DateTime(2025, 6, 1, 8, 30, 0, DateTimeKind.Utc);
        store.Save(new LibraryData
        {
            Plugins =
            {
                new PluginInfo
                {
                    Name = "Old", Path = @"C:\vst2\Old.dll", Format = PluginFormat.Vst2,
                    IsUninstalled = true, UninstalledAt = noticed
                }
            }
        });

        var plugin = Assert.Single(new LibraryStore(_tempFile).Load().Plugins);

        Assert.True(plugin.IsUninstalled);
        Assert.Equal(noticed, plugin.UninstalledAt);
    }
}
