using VstManager.Core.Services.Cloud;

namespace VstManager.Core.Abstractions;

/// <summary>
/// Application-level operations that need the host UI framework's notion of "the app" — shutting
/// down, and the one bespoke dialog the view model owns.
/// </summary>
public interface IAppShell
{
    /// <summary>Closes the application.</summary>
    void Shutdown();

    /// <summary>
    /// Asks the user how to resolve a local/remote cloud conflict. Sync runs on a background
    /// thread, so implementations must marshal to the UI thread themselves and parent the dialog
    /// to whichever window is currently active.
    /// </summary>
    Task<ConflictResolution> AskAboutCloudConflictAsync(DateTime localChangedAt, DateTime remoteChangedAt);
}
