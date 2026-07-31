using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class DataPortabilityServiceTests : IDisposable
{
    private readonly string _libraryPath;
    private readonly string _excludedFilesPath;
    private readonly string _manualLogoOverridesPath;
    private readonly string _manualMetadataOverridesPath;
    private readonly string _exportPath;

    public DataPortabilityServiceTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        _libraryPath = Path.Combine(tempDir, "library.json");
        _excludedFilesPath = Path.Combine(tempDir, "excluded-files.local.json");
        _manualLogoOverridesPath = Path.Combine(tempDir, "manual-logos.json");
        _manualMetadataOverridesPath = Path.Combine(tempDir, "manual-metadata.json");
        _exportPath = Path.Combine(tempDir, "export.json");
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_libraryPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private DataPortabilityService CreateService() =>
        new(_libraryPath, _excludedFilesPath, _manualLogoOverridesPath, _manualMetadataOverridesPath);

    [Fact]
    public void ExportBundle_NoFilesExist_ProducesBundleWithNullSections()
    {
        var service = CreateService();

        var json = service.ExportBundle();

        Assert.Contains("\"library\": null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportThenImport_RoundTripsLibraryContent()
    {
        File.WriteAllText(_libraryPath, """{"CustomScanFolders":["D:\\MyVsts"],"Plugins":[]}""");
        var service = CreateService();

        var exported = service.ExportBundle();
        File.Delete(_libraryPath);

        service.ImportBundle(exported);

        Assert.True(File.Exists(_libraryPath));
        Assert.Contains("MyVsts", File.ReadAllText(_libraryPath));
    }

    [Fact]
    public void ExportThenImport_RoundTripsExcludedFilesAndManualLogoOverrides()
    {
        File.WriteAllText(_excludedFilesPath, """["not-a-plugin.dll"]""");
        File.WriteAllText(_manualLogoOverridesPath, """{"serum":"https://example.com/serum.png"}""");
        var service = CreateService();

        var exported = service.ExportBundle();
        File.Delete(_excludedFilesPath);
        File.Delete(_manualLogoOverridesPath);

        service.ImportBundle(exported);

        Assert.Contains("not-a-plugin.dll", File.ReadAllText(_excludedFilesPath));
        Assert.Contains("serum", File.ReadAllText(_manualLogoOverridesPath));
    }

    [Fact]
    public void ExportThenImport_RoundTripsManualMetadataOverrides()
    {
        File.WriteAllText(_manualMetadataOverridesPath, """{"vps avenger_x64":{"Name":"Avenger","Vendor":"Vengeance Sound"}}""");
        var service = CreateService();

        var exported = service.ExportBundle();
        File.Delete(_manualMetadataOverridesPath);

        service.ImportBundle(exported);

        Assert.True(File.Exists(_manualMetadataOverridesPath));
        Assert.Contains("Vengeance Sound", File.ReadAllText(_manualMetadataOverridesPath));
    }

    [Fact]
    public void ImportBundle_InvalidJson_ThrowsInvalidDataException()
    {
        var service = CreateService();

        Assert.Throws<InvalidDataException>(() => service.ImportBundle("not valid json"));
    }

    [Fact]
    public void ImportBundle_MissingSection_LeavesExistingFileUntouched()
    {
        File.WriteAllText(_libraryPath, """{"CustomScanFolders":["D:\\Keep"],"Plugins":[]}""");
        var service = CreateService();

        // A bundle with no library section at all (e.g. exported from a machine with no
        // library.json yet) must not wipe out an existing file on the importing machine.
        service.ImportBundle("""{"FormatVersion":1,"ExportedAt":"2026-01-01T00:00:00Z"}""");

        Assert.Contains("Keep", File.ReadAllText(_libraryPath));
    }
}
