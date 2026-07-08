using System.Reflection;
using System.Text.Json;
using VstManager.Core.Models;

namespace VstManager.Core.Services;

public class PluginCatalog
{
    private readonly List<CatalogEntry> _entries;

    public PluginCatalog(IEnumerable<CatalogEntry>? entries = null)
    {
        _entries = entries?.ToList() ?? LoadBundled();
    }

    public IReadOnlyList<CatalogEntry> Entries => _entries;

    private static List<CatalogEntry> LoadBundled()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return new List<CatalogEntry>();
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new List<CatalogEntry>();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream, options) ?? new List<CatalogEntry>();
    }
}
