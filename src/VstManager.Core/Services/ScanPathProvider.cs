namespace VstManager.Core.Services;

/// <summary>
/// The standard plugin folders per format. Windows lists are unchanged; the macOS lists follow
/// Apple's and each format's documented locations (system-wide and per-user) and are
/// UNVERIFIED on a real Mac.
/// </summary>
public class ScanPathProvider
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static IReadOnlyList<string> MacBoth(string relative) =>
    [
        Path.Combine("/Library", relative),
        Path.Combine(Home, "Library", relative)
    ];

    public static readonly IReadOnlyList<string> DefaultVst3Paths = OperatingSystem.IsMacOS()
        ? MacBoth("Audio/Plug-Ins/VST3")
        : [@"C:\Program Files\Common Files\VST3"];

    public static readonly IReadOnlyList<string> DefaultVst2Paths = OperatingSystem.IsMacOS()
        ? MacBoth("Audio/Plug-Ins/VST")
        :
        [
            @"C:\Program Files\Steinberg\VstPlugins",
            @"C:\Program Files\VstPlugins",
            @"C:\Program Files\Common Files\VST2"
        ];

    public static readonly IReadOnlyList<string> DefaultAaxPaths = OperatingSystem.IsMacOS()
        ? ["/Library/Application Support/Avid/Audio/Plug-Ins"]
        : [@"C:\Program Files\Common Files\Avid\Audio\Plug-Ins"];

    /// <summary>The system-wide and per-user locations the CLAP spec defines.</summary>
    public static readonly IReadOnlyList<string> DefaultClapPaths = OperatingSystem.IsMacOS()
        ? MacBoth("Audio/Plug-Ins/CLAP")
        :
        [
            @"C:\Program Files\Common Files\CLAP",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "CLAP")
        ];

    /// <summary>Audio Units exist only on macOS.</summary>
    public static readonly IReadOnlyList<string> DefaultAuPaths = OperatingSystem.IsMacOS()
        ? MacBoth("Audio/Plug-Ins/Components")
        : [];

    public IReadOnlyList<string> GetVst3Paths(IEnumerable<string> customFolders) => With(DefaultVst3Paths, customFolders);

    public IReadOnlyList<string> GetVst2Paths(IEnumerable<string> customFolders) => With(DefaultVst2Paths, customFolders);

    public IReadOnlyList<string> GetAaxPaths(IEnumerable<string> customFolders) => With(DefaultAaxPaths, customFolders);

    public IReadOnlyList<string> GetClapPaths(IEnumerable<string> customFolders) => With(DefaultClapPaths, customFolders);

    public IReadOnlyList<string> GetAuPaths(IEnumerable<string> customFolders) =>
        OperatingSystem.IsMacOS() ? With(DefaultAuPaths, customFolders) : [];

    private static IReadOnlyList<string> With(IReadOnlyList<string> defaults, IEnumerable<string> customFolders) =>
        defaults.Concat(customFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
