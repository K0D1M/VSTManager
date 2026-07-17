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
}
