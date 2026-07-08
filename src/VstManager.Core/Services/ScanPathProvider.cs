namespace VstManager.Core.Services;

public class ScanPathProvider
{
    public static readonly IReadOnlyList<string> DefaultVst3Paths = new[]
    {
        @"C:\Program Files\Common Files\VST3"
    };

    public static readonly IReadOnlyList<string> DefaultVst2Paths = new[]
    {
        @"C:\Program Files\Steinberg\VstPlugins",
        @"C:\Program Files\VstPlugins",
        @"C:\Program Files\Common Files\VST2"
    };

    public IReadOnlyList<string> GetVst3Paths(IEnumerable<string> customFolders) =>
        DefaultVst3Paths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> GetVst2Paths(IEnumerable<string> customFolders) =>
        DefaultVst2Paths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
