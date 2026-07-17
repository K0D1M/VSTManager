namespace VstManager.Core.Models;

public class CatalogEntry
{
    public required string Name { get; set; }
    public required string Vendor { get; set; }
    public required string LogoUrl { get; set; }

    /// <summary>
    /// Known alternate installed filenames that don't resemble the product name closely
    /// enough for the normal fuzzy matching to connect (e.g. abbreviated internal names).
    /// Matched by exact normalized equality only, so no false-positive risk.
    /// </summary>
    public List<string> Aliases { get; set; } = new();
}
