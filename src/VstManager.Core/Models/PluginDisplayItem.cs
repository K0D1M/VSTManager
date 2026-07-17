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

    public bool IsInstalled => Installs.Count > 0;

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

    public bool IsFavoriteSummary => Installs.Any(i => i.IsFavorite);

    public bool IsHiddenSummary => Installs.Any(i => i.IsHidden);
}
