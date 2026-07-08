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
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog)
        {
            var match = installed.FirstOrDefault(p => _matcher.FindMatch(p.Name, new[] { entry }) is not null);
            if (match is not null)
            {
                matchedPaths.Add(match.Path);
            }

            items.Add(new PluginDisplayItem
            {
                Name = entry.Name,
                Vendor = entry.Vendor,
                Catalog = entry,
                Installed = match
            });
        }

        foreach (var plugin in installed)
        {
            if (!matchedPaths.Contains(plugin.Path))
            {
                items.Add(new PluginDisplayItem
                {
                    Name = plugin.Name,
                    Vendor = null,
                    Catalog = null,
                    Installed = plugin
                });
            }
        }

        return items;
    }
}
