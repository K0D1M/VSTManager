namespace VstManager.Core.Models;

public class LibraryData
{
    public List<string> CustomScanFolders { get; set; } = new();
    public List<PluginInfo> Plugins { get; set; } = new();
    public bool IsDarkTheme { get; set; } = true;
    public string AccentColor { get; set; } = "#FF8A5CF6";
    public bool AutostartEnabled { get; set; }
    public DateTime? LastUpdateCheck { get; set; }
    public string LayoutMode { get; set; } = "Grid";
    public bool HasSeenLogoInstructions { get; set; }

    /// <summary>
    /// Whether to look plugins up online (KVR) automatically on launch to find newer versions.
    /// On by default to preserve existing behaviour; users on metered/offline connections — or
    /// anyone who finds the startup lookup slow across a large library — can turn it off and
    /// still run it on demand via "Refresh All Metadata".
    /// </summary>
    public bool CheckForPluginUpdatesOnStartup { get; set; } = true;

    /// <summary>Whether the main window opens minimized (to the taskbar) instead of normal-sized.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Whether Windows notifications fire for new plugins, outdated versions, and
    /// completed scans/refreshes.</summary>
    public bool ShowNotifications { get; set; } = true;
}
