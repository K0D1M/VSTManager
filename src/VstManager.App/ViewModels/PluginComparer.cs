using System.Collections;

namespace VstManager.App.ViewModels;

/// <summary>
/// Orders plugins for the section views.
///
/// The rule that shapes this: a plugin missing the thing being sorted on — no vendor, no tags,
/// no version info — always sinks to the bottom, in both ascending and descending order.
/// Sorting by vendor should open on real vendors, not on a block of "unknown"; and reversing the
/// direction shouldn't suddenly promote the least informative rows to the top.
///
/// Name is the tiebreaker throughout, so equal keys stay in a stable, readable order.
/// </summary>
public sealed class PluginComparer : IComparer
{
    private readonly SortOption _option;
    private readonly bool _descending;

    public PluginComparer(SortOption option, bool descending)
    {
        _option = option;
        _descending = descending;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not PluginDisplayViewModel left || y is not PluginDisplayViewModel right)
        {
            return 0;
        }

        // Compared before the direction flip so that "missing" stays last either way.
        var emptiness = MissingRank(left).CompareTo(MissingRank(right));
        if (emptiness != 0)
        {
            return emptiness;
        }

        var result = CompareKeys(left, right);
        if (_descending)
        {
            result = -result;
        }

        return result != 0 ? result : CompareNames(left, right);
    }

    private int CompareKeys(PluginDisplayViewModel left, PluginDisplayViewModel right) => _option switch
    {
        SortOption.Vendor => string.Compare(left.Vendor, right.Vendor, StringComparison.CurrentCultureIgnoreCase),
        SortOption.Type => string.Compare(left.PrimaryTagName, right.PrimaryTagName, StringComparison.CurrentCultureIgnoreCase),
        SortOption.RecentlyAdded => Nullable.Compare(FirstSeen(right), FirstSeen(left)),
        SortOption.UpdateStatus => UpdateRank(left).CompareTo(UpdateRank(right)),
        _ => CompareNames(left, right)
    };

    private static int CompareNames(PluginDisplayViewModel left, PluginDisplayViewModel right) =>
        string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>1 for plugins with nothing to sort on, so they land after everything else.</summary>
    private int MissingRank(PluginDisplayViewModel plugin) => _option switch
    {
        SortOption.Vendor => string.IsNullOrWhiteSpace(plugin.Vendor) ? 1 : 0,
        SortOption.Type => plugin.PrimaryTagName is null ? 1 : 0,
        SortOption.RecentlyAdded => FirstSeen(plugin) is null ? 1 : 0,
        _ => 0
    };

    private static DateTime? FirstSeen(PluginDisplayViewModel plugin) =>
        plugin.Installs.Select(i => i.FirstSeenAt).Where(d => d.HasValue).DefaultIfEmpty(null).Max();

    /// <summary>
    /// Outdated first — that's the reason to sort by update status at all — then up-to-date,
    /// then plugins whose latest version was never established.
    /// </summary>
    private static int UpdateRank(PluginDisplayViewModel plugin)
    {
        if (plugin.IsOutdated)
        {
            return 0;
        }

        return string.IsNullOrWhiteSpace(plugin.LatestVersion) ? 2 : 1;
    }
}
