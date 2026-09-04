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

    /// <summary>Whether closing the main window hides it to the system tray instead of exiting
    /// the app — background scanning/notifications keep running until Exit is chosen from the
    /// tray icon.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>Whether settings are mirrored to the cloud. Off until the user opts in.</summary>
    public bool CloudSyncEnabled { get; set; }

    /// <summary>
    /// Identifies this user's settings in the remote bucket. Generated once on first use;
    /// carrying the same id to another machine is what makes the two share a settings copy.
    /// </summary>
    public string? CloudDeviceId { get; set; }

    /// <summary>
    /// When the last successful sync completed (UTC). Compared against the library file's
    /// modification time to tell "changed since last sync" from "untouched".
    /// </summary>
    public DateTime? LastCloudSyncAt { get; set; }

    /// <summary>
    /// Every tag that exists — the built-in set plus anything the user created. Lives here
    /// rather than in its own file so it exports and cloud-syncs with the rest of the library
    /// for free. The per-plugin assignments are separate (see PluginTagService).
    /// </summary>
    public List<TagDefinition> Tags { get; set; } = new();

    /// <summary>
    /// Every folder the user has made, stored flat with each entry naming its parent (see
    /// <see cref="PluginFolder"/>). Lives here for the same reason the tags do: it exports and
    /// cloud-syncs with the rest of the library without extra plumbing. The per-plugin filing is
    /// separate (see PluginFolderService).
    /// </summary>
    public List<PluginFolder> Folders { get; set; } = new();

    /// <summary>How the plugin lists are ordered. Stored as the enum name.</summary>
    public string SortOption { get; set; } = "Name";

    public bool SortDescending { get; set; }

    /// <summary>
    /// User-chosen zoom for all window content, 1.0 = 100%. Scales text, cards and icons together
    /// (on top of the OS's own DPI scaling) so the app can be made denser or roomier to taste and
    /// stay comfortable from FHD to 4K. Clamped to UiScale.Min..UiScale.Max on load.
    /// </summary>
    public double UiScale { get; set; } = 1.0;
}
