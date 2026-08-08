namespace VstManager.Core.Models;

public class PluginDisplayItem
{
    public required string Name { get; set; }
    public string? Vendor { get; set; }
    public CatalogEntry? Catalog { get; set; }
    public List<PluginInfo> Installs { get; set; } = new();

    /// <summary>
    /// The Name as originally derived by catalog matching / raw filename, before any manual
    /// metadata or logo override was applied. Overrides are keyed by this, not by the live
    /// (possibly user-edited) Name, so they stay stable across rescans and repeated edits.
    /// </summary>
    public string BaseName { get; set; } = string.Empty;

    /// <summary>Copies whose file is actually present on disk right now.</summary>
    public IEnumerable<PluginInfo> ActiveInstalls => Installs.Where(i => !i.IsUninstalled);

    /// <summary>Copies kept only to remember their metadata after the files were removed.</summary>
    public IEnumerable<PluginInfo> RememberedInstalls => Installs.Where(i => i.IsUninstalled);

    /// <summary>
    /// True when at least one copy is present on disk. Deliberately not "Installs.Count > 0":
    /// uninstalled copies stay in Installs so the summaries below keep reporting the user's
    /// tag/kind/favourite/hidden choices, but they must not make the plugin read as installed.
    /// </summary>
    public bool IsInstalled => Installs.Any(i => !i.IsUninstalled);

    /// <summary>
    /// True for a plugin that isn't installed but that the library still has a record of —
    /// distinguishing "you uninstalled this" from "a catalog entry you never installed".
    /// </summary>
    public bool IsRemembered => !IsInstalled && Installs.Count > 0;

    public PluginTagSummary TagSummary
    {
        get
        {
            var hasLegit = Installs.Any(i => i.Tag == PluginTag.Legit);
            var hasCracked = Installs.Any(i => i.Tag == PluginTag.Cracked);

            return (hasLegit, hasCracked) switch
            {
                (true, true) => PluginTagSummary.Both,
                (true, false) => PluginTagSummary.Legit,
                (false, true) => PluginTagSummary.Cracked,
                _ => PluginTagSummary.Unclassified
            };
        }
    }

    /// <summary>
    /// Instrument/Effect is strictly exclusive per plugin, so all installed copies are
    /// always set together (see MainViewModel.ApplyKindToAllCopies). If a library.json was
    /// hand-edited into disagreement, treat that as Unclassified rather than guessing.
    /// </summary>
    public PluginKind KindSummary
    {
        get
        {
            var distinctKinds = Installs.Select(i => i.Kind).Distinct().ToList();
            return distinctKinds.Count == 1 ? distinctKinds[0] : PluginKind.Unclassified;
        }
    }

    /// <summary>
    /// The tags this plugin carries, resolved to definitions and ordered manual-first. Filled in
    /// by PluginDisplayBuilder.ApplyTags after the items are built, since tag assignments live
    /// outside the scan data.
    /// </summary>
    public List<TagDefinition> Tags { get; set; } = new();

    /// <summary>Ids among <see cref="Tags"/> that were auto-detected rather than user-applied.</summary>
    public HashSet<string> AutoTagIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsFavoriteSummary => Installs.Any(i => i.IsFavorite);

    public bool IsHiddenSummary => Installs.Any(i => i.IsHidden);
}
