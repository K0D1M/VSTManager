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
    public void MergeOnRescan_DroppedPluginNoLongerPresent()
    {
        var store = new LibraryStore(_tempFile);
        var existing = new List<PluginInfo>
        {
            new() { Name = "Old", Path = @"C:\vst2\Old.dll", Format = PluginFormat.Vst2, Tag = PluginTag.Legit }
        };
        var scanned = new List<PluginInfo>();

        var merged = store.MergeOnRescan(existing, scanned);

        Assert.Empty(merged);
    }
}
