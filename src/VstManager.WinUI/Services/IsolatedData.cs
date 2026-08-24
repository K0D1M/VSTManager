namespace VstManager.WinUI.Services;

/// <summary>
/// Keeps this experimental head's data completely separate from the shipping WPF app's.
///
/// Every Core service defaults to %APPDATA%\VstManager\, so without this the two apps would read and
/// write the same files. Two processes writing them is not theoretical: it corrupted
/// manual-metadata.json with a torn write during development. So this head works in
/// %APPDATA%\VstManagerWinUI\, seeded once from the real data so the prototype opens on a genuine
/// library rather than an empty one.
///
/// Core needs no changes for this — every service already takes a path in its constructor.
/// </summary>
public static class IsolatedData
{
    /// <summary>The real app's folder. Read at most once, to seed. Never written.</summary>
    private static string SourceFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VstManager");

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VstManagerWinUI");

    public static string Library => Path.Combine(Folder, "library.json");
    public static string PluginTags => Path.Combine(Folder, "plugin-tags.json");
    public static string ManualMetadata => Path.Combine(Folder, "manual-metadata.json");
    public static string ManualLogos => Path.Combine(Folder, "manual-logos.json");
    public static string ExcludedFiles => Path.Combine(Folder, "excluded-files.local.json");
    public static string LookupCache => Path.Combine(Folder, "lookup-cache.json");

    /// <summary>
    /// Logo cache, pointed at the WPF app's existing folder. Shared deliberately and read-only in
    /// practice: artwork is large, already downloaded, and content-addressed by slug — so reusing it
    /// means the prototype has images immediately instead of re-fetching the whole library.
    /// </summary>
    public static string LogoCacheFolder => Path.Combine(SourceFolder, "logos");

    /// <summary>
    /// Creates the isolated folder and, the first time only, copies the real data in. Existing files
    /// are never overwritten, so changes made in this app survive relaunch.
    /// </summary>
    public static void EnsureSeeded()
    {
        Directory.CreateDirectory(Folder);

        foreach (var fileName in new[]
                 {
                     "library.json", "plugin-tags.json", "manual-metadata.json",
                     "manual-logos.json", "excluded-files.local.json", "lookup-cache.json"
                 })
        {
            var destination = Path.Combine(Folder, fileName);
            var source = Path.Combine(SourceFolder, fileName);

            if (File.Exists(destination) || !File.Exists(source))
            {
                continue;
            }

            try
            {
                File.Copy(source, destination);
            }
            catch (IOException)
            {
                // Seeding is a convenience — starting empty is survivable, corrupting the real app's
                // data is not, so a failure here is swallowed rather than retried against the source.
            }
        }
    }
}
