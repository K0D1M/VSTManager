namespace VstManager.Core.Models;

public enum PluginFormat
{
    Vst2,
    Vst3
}

#if !NO_LICENSE_STATUS
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
#endif

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
#if !NO_LICENSE_STATUS
    public PluginTag Tag { get; set; } = PluginTag.Unclassified;
#endif
    public PluginKind Kind { get; set; } = PluginKind.Unclassified;

    /// <summary>
    /// Which CategoryDefinition (LibraryData.Categories) this copy belongs to, or null if not yet
    /// assigned. Separate from Kind rather than replacing it — Kind stays exactly as every
    /// existing call site across both UI heads already reads it; this is purely additive, and for
    /// the two built-in categories (Instrument/Effect) is kept in sync with Kind so old data and
    /// new data agree. A custom, user-created category has no Kind equivalent and leaves Kind at
    /// Unclassified.
    /// </summary>
    public string? CategoryId { get; set; }

    /// <summary>
    /// Set the moment this copy's path is first noticed as new (same scan that sets FirstSeenAt),
    /// cleared only once a category is actually assigned — deliberately NOT the same thing as
    /// "IsNew" in the UI layer, which resets every scan and can't alone drive "keep asking until
    /// answered": a plugin dismissed via the mandatory info window's "Later" would otherwise stop
    /// being IsNew on the very next scan and never be re-prompted. This flag is what the prompt
    /// actually keys off, and is what "repeat until entered" really means at the data level.
    /// Absent (defaults false) for every plugin that existed before this feature shipped, so
    /// nothing already in a library.json suddenly demands a category on first launch.
    /// </summary>
    public bool NeedsCategoryReview { get; set; }
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }

    /// <summary>When true, the OUTDATED badge is suppressed for this copy even if LatestVersion is newer than CurrentVersion — a user-set exception (e.g. a cracked copy that can't be updated the normal way), not tag-based.</summary>
    public bool IgnoreVersionCheck { get; set; }

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
