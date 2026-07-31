using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class ManualMetadataOverrideServiceTests : IDisposable
{
    private readonly string _filePath;

    public ManualMetadataOverrideServiceTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        _filePath = Path.Combine(tempDir, "manual-metadata.json");
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetOverride_NoOverrideSet_ReturnsNull()
    {
        var service = new ManualMetadataOverrideService(_filePath);

        Assert.Null(service.GetOverride("VPS Avenger_x64"));
    }

    [Fact]
    public void SetOverride_ThenGetOverride_RoundTrips()
    {
        var service = new ManualMetadataOverrideService(_filePath);

        service.SetOverride("VPS Avenger_x64", "Avenger", "Vengeance Sound");
        var result = service.GetOverride("VPS Avenger_x64");

        Assert.NotNull(result);
        Assert.Equal("Avenger", result!.Name);
        Assert.Equal("Vengeance Sound", result.Vendor);
    }

    [Fact]
    public void SetOverride_KeyLookup_IsCaseAndWhitespaceInsensitive()
    {
        var service = new ManualMetadataOverrideService(_filePath);

        service.SetOverride("  VPS Avenger_x64  ", "Avenger", "Vengeance Sound");

        Assert.NotNull(service.GetOverride("vps avenger_x64"));
    }

    [Fact]
    public void SetOverride_NameOnly_LeavesVendorNull()
    {
        var service = new ManualMetadataOverrideService(_filePath);

        service.SetOverride("uaudio_1176", "1176 Classic Limiter Collection", null);
        var result = service.GetOverride("uaudio_1176");

        Assert.NotNull(result);
        Assert.Equal("1176 Classic Limiter Collection", result!.Name);
        Assert.Null(result.Vendor);
    }

    [Fact]
    public void SetOverride_BothFieldsBlank_RemovesExistingEntry()
    {
        var service = new ManualMetadataOverrideService(_filePath);
        service.SetOverride("Serum", "Serum 2", "Xfer Records");

        service.SetOverride("Serum", "  ", null);

        Assert.Null(service.GetOverride("Serum"));
    }

    [Fact]
    public void ClearOverride_RemovesEntry()
    {
        var service = new ManualMetadataOverrideService(_filePath);
        service.SetOverride("Serum", "Serum 2", "Xfer Records");

        service.ClearOverride("Serum");

        Assert.Null(service.GetOverride("Serum"));
    }

    [Fact]
    public void Reload_PicksUpExternallyWrittenChanges()
    {
        var service = new ManualMetadataOverrideService(_filePath);
        service.SetOverride("Serum", "Serum 2", "Xfer Records");

        File.WriteAllText(_filePath, """{"massive":{"Name":"Massive X","Vendor":"Native Instruments"}}""");
        service.Reload();

        Assert.Null(service.GetOverride("Serum"));
        Assert.NotNull(service.GetOverride("massive"));
    }
}
