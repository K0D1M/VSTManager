namespace VstManager.Core.Models;

public enum PluginFormat
{
    Vst2,
    Vst3
}

public enum PluginTag
{
    Unclassified,
    Legit,
    Cracked
}

public enum PluginTagSummary
{
    Unclassified,
    Legit,
    Cracked,
    Both
}

public enum PluginKind
{
    Unclassified,
    Instrument,
    Effect
}

public class PluginInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required PluginFormat Format { get; set; }
    public PluginTag Tag { get; set; } = PluginTag.Unclassified;
    public PluginKind Kind { get; set; } = PluginKind.Unclassified;
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }

    /// <summary>
    /// True when this entry's file was present in an earlier scan but is no longer on disk.
    /// The entry is deliberately retained so the user's classification (tag, kind, versions,
    /// favourite, hidden) survives an uninstall instead of being destroyed by the next
    /// rescan. Absent from older library.json files, so it loads as false — i.e. everything
    /// already stored is treated as installed, which is what it was.
    /// </summary>
    public bool IsUninstalled { get; set; }

    /// <summary>
    /// When a rescan first noticed the file was gone. Null while installed. Preserved across
    /// later rescans so it means "when it disappeared", not "the last time we checked".
    /// </summary>
    public DateTime? UninstalledAt { get; set; }

    /// <summary>
    /// When this copy first turned up in a scan, for the "Recently added" sort. Null for entries
    /// that predate the field; LibraryStore backfills those from the file's own timestamp on the
    /// next rescan, which is an approximation but a far better one than "unknown".
    /// </summary>
    public DateTime? FirstSeenAt { get; set; }

    /// <summary>
    /// Who makes this plugin, read off disk at scan time (bundle manifest, version resource, or
    /// the containing folder). Stored so the disk reads happen once rather than every launch.
    /// Only a fallback for display — a catalog entry or a manual correction always wins — but
    /// it's what lets the online lookup build a direct product URL instead of web-searching.
    /// </summary>
    public string? Vendor { get; set; }
}
