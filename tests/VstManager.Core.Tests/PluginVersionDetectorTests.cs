using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class PluginVersionDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vstmgr-verdet-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Builds a VST3 bundle laid out the way real plugins are, returning the inner binary's path.</summary>
    private string CreateBundle(string bundleName, string? moduleInfoJson = null, string? manifestJson = null,
        string moduleInfoFolder = "Contents")
    {
        var bundleRoot = Path.Combine(_root, bundleName + ".vst3");
        var binaryDir = Path.Combine(bundleRoot, "Contents", "x86_64-win");
        Directory.CreateDirectory(binaryDir);

        var binary = Path.Combine(binaryDir, bundleName + ".vst3");
        File.WriteAllBytes(binary, new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

        if (moduleInfoJson is not null)
        {
            var dir = Path.Combine(bundleRoot, moduleInfoFolder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "moduleinfo.json"), moduleInfoJson);
        }

        if (manifestJson is not null)
        {
            var resources = Path.Combine(bundleRoot, "Contents", "Resources");
            Directory.CreateDirectory(resources);
            File.WriteAllText(Path.Combine(resources, "manifest.json"), manifestJson);
        }

        return binary;
    }

    [Fact]
    public void DetectFromFile_NoVersionResourceButModuleInfoPresent_ReadsModuleInfoVersion()
    {
        var binary = CreateBundle("Demo", moduleInfoJson: """{ "Name": "Demo", "Version": "2.1.2" }""");

        Assert.Equal("2.1.2", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_ModuleInfoUnderResources_IsAlsoFound()
    {
        // SDK version differences put moduleinfo.json in Contents/ or Contents/Resources/.
        var binary = CreateBundle("Demo", moduleInfoJson: """{ "Version": "3.0.1" }""",
            moduleInfoFolder: Path.Combine("Contents", "Resources"));

        Assert.Equal("3.0.1", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_ModuleInfoWithCommentsAndTrailingCommas_StillParses()
    {
        // Real moduleinfo.json files are hand-maintained and often not strict JSON.
        var binary = CreateBundle("Demo", moduleInfoJson: """
            {
                // the module version
                "Name": "Demo",
                "Version": "1.4.0",
            }
            """);

        Assert.Equal("1.4.0", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_VendorManifest_ReadsVersionForMatchingPluginId()
    {
        // Mirrors Universal Audio's UADx bundle layout, which has neither a version resource
        // nor a moduleinfo.json — its version lives only here.
        var binary = CreateBundle("uaudio_polymax", manifestJson: """
            {
              "plugin_id": "uaudio_polymax",
              "plugin_bundle_name": "UADx PolyMAX Synth",
              "build_number": 1264,
              "algo_manifest": {
                "uaudio_polymax": { "name": "PolyMAX Synth", "version": "1.0.16", "author": "Universal Audio" }
              }
            }
            """);

        Assert.Equal("1.0.16", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_VendorManifestWithoutMatchingPluginId_FallsBackToSingleEntry()
    {
        var binary = CreateBundle("thing", manifestJson: """
            { "algo_manifest": { "some_other_key": { "version": "9.9.9" } } }
            """);

        Assert.Equal("9.9.9", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_ModuleInfoTakesPrecedenceOverVendorManifest()
    {
        var binary = CreateBundle("Demo",
            moduleInfoJson: """{ "Version": "5.0.0" }""",
            manifestJson: """{ "algo_manifest": { "x": { "version": "1.0.0" } } }""");

        Assert.Equal("5.0.0", new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_BundleWithNoVersionAnywhere_ReturnsNull()
    {
        var binary = CreateBundle("Bare");

        Assert.Null(new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void DetectFromFile_MalformedManifest_ReturnsNullRatherThanThrowing()
    {
        var binary = CreateBundle("Broken", manifestJson: "{ this is not json");

        Assert.Null(new PluginVersionDetector().DetectFromFile(binary));
    }

    [Fact]
    public void FindBundleRoot_LocatesBundleFromNestedBinaryAndFromBundleItself()
    {
        var binary = CreateBundle("Demo", moduleInfoJson: """{ "Version": "1.0.0" }""");
        var bundleRoot = Path.Combine(_root, "Demo.vst3");

        Assert.Equal(bundleRoot, PluginVersionDetector.FindBundleRoot(binary));
        Assert.Equal(bundleRoot, PluginVersionDetector.FindBundleRoot(bundleRoot));
    }

    [Fact]
    public void FindBundleRoot_PlainDllOutsideAnyBundle_ReturnsNull()
    {
        Directory.CreateDirectory(_root);
        var dll = Path.Combine(_root, "Synth1.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });

        Assert.Null(PluginVersionDetector.FindBundleRoot(dll));
    }

    [Fact]
    public void DetectFromFile_NonExistentPath_ReturnsNull()
    {
        var detector = new PluginVersionDetector();

        var result = detector.DetectFromFile(@"C:\this\path\does\not\exist\Fake.dll");

        Assert.Null(result);
    }

    [Fact]
    public void DetectFromFile_FileWithNoVersionResource_ReturnsNull()
    {
        var detector = new PluginVersionDetector();
        var tempFile = Path.Combine(Path.GetTempPath(), "VstManagerTests_" + Guid.NewGuid() + ".dll");
        File.WriteAllText(tempFile, "not a real PE file");

        try
        {
            var result = detector.DetectFromFile(tempFile);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
