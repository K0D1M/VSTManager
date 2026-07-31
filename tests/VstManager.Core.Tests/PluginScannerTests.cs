using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class PluginScannerTests : IDisposable
{
    private readonly string _tempRoot;

    public PluginScannerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Scan_FindsVst2DllsAndVst3Bundles()
    {
        var vst2Folder = Path.Combine(_tempRoot, "vst2");
        var vst3Folder = Path.Combine(_tempRoot, "vst3");
        Directory.CreateDirectory(vst2Folder);
        Directory.CreateDirectory(vst3Folder);

        File.WriteAllText(Path.Combine(vst2Folder, "Serum.dll"), "dummy");
        File.WriteAllText(Path.Combine(vst3Folder, "Diva.vst3"), "dummy");

        var scanner = new PluginScanner();
        var results = scanner.Scan(new[] { vst3Folder }, new[] { vst2Folder });

        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Name == "Serum" && p.Format == PluginFormat.Vst2);
        Assert.Contains(results, p => p.Name == "Diva" && p.Format == PluginFormat.Vst3);
    }

    [Fact]
    public void Scan_NonExistentFolder_ReturnsEmpty()
    {
        var scanner = new PluginScanner();
        var results = scanner.Scan(new[] { Path.Combine(_tempRoot, "missing") }, Array.Empty<string>());

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_IgnoresUnrelatedFiles()
    {
        var vst2Folder = Path.Combine(_tempRoot, "vst2");
        Directory.CreateDirectory(vst2Folder);
        File.WriteAllText(Path.Combine(vst2Folder, "readme.txt"), "dummy");

        var scanner = new PluginScanner();
        var results = scanner.Scan(Array.Empty<string>(), new[] { vst2Folder });

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_Vst3Bundle_CountsOnlyTheInnerBinaryNotTheFolder()
    {
        var vst3Folder = Path.Combine(_tempRoot, "vst3");
        var bundleFolder = Path.Combine(vst3Folder, "Serum.vst3");
        var innerArchFolder = Path.Combine(bundleFolder, "Contents", "x86_64-win");
        Directory.CreateDirectory(innerArchFolder);

        var innerBinaryPath = Path.Combine(innerArchFolder, "Serum.vst3");
        File.WriteAllText(innerBinaryPath, "dummy-binary");

        var scanner = new PluginScanner();
        var results = scanner.Scan(new[] { vst3Folder }, Array.Empty<string>());

        Assert.Single(results);
        Assert.Equal(innerBinaryPath, results[0].Path);
        Assert.Equal("Serum", results[0].Name);
    }

    [Fact]
    public void Scan_LooseVst3File_NotTreatedAsBundle()
    {
        var vst3Folder = Path.Combine(_tempRoot, "vst3");
        Directory.CreateDirectory(vst3Folder);
        var filePath = Path.Combine(vst3Folder, "Standalone.vst3");
        File.WriteAllText(filePath, "dummy");

        var scanner = new PluginScanner();
        var results = scanner.Scan(new[] { vst3Folder }, Array.Empty<string>());

        Assert.Single(results);
        Assert.Equal(filePath, results[0].Path);
    }
}
