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
                // No catalog entry to take a vendor from, so use whatever the scan read off
                // disk. This is the difference between the online lookup being able to build a
                // direct product URL and having to fall back to a web search.
                unmatchedItem = new PluginDisplayItem { Name = plugin.Name, Vendor = plugin.Vendor, Catalog = null, BaseName = plugin.Name };
                byNormalizedName[key] = unmatchedItem;
                items.Add(unmatchedItem);
            }

            // Copies are merged one at a time, and only some carry a readable vendor (a VST2 DLL
            // often does where its VST3 sibling doesn't, or the reverse) — so take the first one
            // that has an answer rather than only whichever copy happened to arrive first.
            unmatchedItem.Vendor ??= plugin.Vendor;
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

    /// <summary>
    /// Resolves each item's tag assignments into definitions, manual tags first so the chips a
    /// user chose lead the ones KVR guessed. Ids with no matching definition are skipped — that
    /// happens when a custom tag was deleted, and dropping them keeps the display honest without
    /// needing a migration pass over the assignment file.
    /// </summary>
    public void ApplyTags(
        IEnumerable<PluginDisplayItem> items,
        PluginTagService tagService,
        IReadOnlyList<TagDefinition> definitions)
    {
        var byId = definitions
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var assignment = tagService.GetAssignment(item.BaseName);
            if (assignment is null)
            {
                item.Tags = new List<TagDefinition>();
                item.AutoTagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            item.Tags = assignment.AllTagIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();

            item.AutoTagIds = new HashSet<string>(assignment.AutoTagIds, StringComparer.OrdinalIgnoreCase);
        }
    }
}
