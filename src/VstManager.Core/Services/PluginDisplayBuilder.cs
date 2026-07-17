using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginDisplayBuilder
{
    private readonly PluginNameMatcher _matcher;

    public PluginDisplayBuilder(PluginNameMatcher? matcher = null)
    {
        _matcher = matcher ?? new PluginNameMatcher();
    }

    public List<PluginDisplayItem> Build(IReadOnlyList<CatalogEntry> catalog, IReadOnlyList<PluginInfo> installed)
    {
        var items = new List<PluginDisplayItem>();
        var byCatalogEntry = new Dictionary<CatalogEntry, PluginDisplayItem>();
        var byNormalizedName = new Dictionary<string, PluginDisplayItem>();

        foreach (var plugin in installed)
        {
            var entry = _matcher.FindMatch(plugin.Name, catalog);

            if (entry is not null)
            {
                if (!byCatalogEntry.TryGetValue(entry, out var item))
                {
                    item = new PluginDisplayItem { Name = entry.Name, Vendor = entry.Vendor, Catalog = entry, BaseName = entry.Name };
                    byCatalogEntry[entry] = item;
                    items.Add(item);
                }

                item.Installs.Add(plugin);
                continue;
            }

            var key = PluginNameMatcher.Normalize(plugin.Name);
            if (!byNormalizedName.TryGetValue(key, out var unmatchedItem))
            {
                unmatchedItem = new PluginDisplayItem { Name = plugin.Name, Vendor = null, Catalog = null, BaseName = plugin.Name };
                byNormalizedName[key] = unmatchedItem;
                items.Add(unmatchedItem);
            }

            unmatchedItem.Installs.Add(plugin);
        }

        foreach (var entry in catalog)
        {
            if (!byCatalogEntry.ContainsKey(entry))
            {
                items.Add(new PluginDisplayItem { Name = entry.Name, Vendor = entry.Vendor, Catalog = entry, BaseName = entry.Name });
            }
        }

        return items;
    }

    /// <summary>
    /// Applies user-supplied Name/Vendor corrections on top of the freshly built items, keyed
    /// by BaseName. Note: if two different uncatalogued installs already collapsed onto the
    /// same PluginDisplayItem (identical normalized raw name), an override on one applies to
    /// both, since they already share the item — a pre-existing grouping characteristic, not
    /// something this introduces.
    /// </summary>
    public void ApplyManualOverrides(IEnumerable<PluginDisplayItem> items, ManualMetadataOverrideService overrides)
    {
        foreach (var item in items)
        {
            var overrideEntry = overrides.GetOverride(item.BaseName);
            if (overrideEntry is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(overrideEntry.Name))
            {
                item.Name = overrideEntry.Name;
            }

            if (!string.IsNullOrWhiteSpace(overrideEntry.Vendor))
            {
                item.Vendor = overrideEntry.Vendor;
            }
        }
    }
}
