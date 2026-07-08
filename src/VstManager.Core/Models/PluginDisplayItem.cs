namespace VstManager.Core.Models;

public class PluginDisplayItem
{
    public required string Name { get; set; }
    public string? Vendor { get; set; }
    public CatalogEntry? Catalog { get; set; }
    public PluginInfo? Installed { get; set; }

    public bool IsInstalled => Installed is not null;
    public PluginFormat? Format => Installed?.Format;
    public PluginTag Tag => Installed?.Tag ?? PluginTag.Unclassified;
}
