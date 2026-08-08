namespace VstManager.Core.Models;

/// <summary>
/// One remembered KVR lookup. Stored so that relaunching doesn't re-query the web for a library
/// whose answers haven't changed — the lookup is by far the slowest thing the app does on start.
/// </summary>
public class CachedLookup
{
    public string? ProductName { get; set; }
    public string? Vendor { get; set; }
    public string? LogoUrl { get; set; }
    public string? LatestVersion { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>Category words scraped from the product page title, e.g. "EQ", "Synth".</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// True when the lookup completed but found nothing. Remembered deliberately: a plugin
    /// that isn't on KVR would otherwise be re-searched — the most expensive path there is —
    /// on every single launch, forever.
    /// </summary>
    public bool NotFound { get; set; }

    public DateTime FetchedAt { get; set; }
}
