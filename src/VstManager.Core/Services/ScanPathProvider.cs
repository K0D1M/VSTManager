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

    public static readonly IReadOnlyList<string> DefaultAaxPaths = new[]
    {
        @"C:\Program Files\Common Files\Avid\Audio\Plug-Ins"
    };

    /// <summary>The two locations the CLAP spec defines for Windows: system-wide and per-user.</summary>
    public static readonly IReadOnlyList<string> DefaultClapPaths = new[]
    {
        @"C:\Program Files\Common Files\CLAP",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "CLAP")
    };

    public IReadOnlyList<string> GetAaxPaths(IEnumerable<string> customFolders) =>
        DefaultAaxPaths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> GetClapPaths(IEnumerable<string> customFolders) =>
        DefaultClapPaths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> GetVst3Paths(IEnumerable<string> customFolders) =>
        DefaultVst3Paths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> GetVst2Paths(IEnumerable<string> customFolders) =>
        DefaultVst2Paths.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
