namespace VstManager.Core.Services.Cloud;

/// <summary>
/// What the cloud indicator in the toolbar is currently showing. Drives the icon colour:
/// grey / green / blue / red, in declaration order.
/// </summary>
public enum CloudSyncState
{
    /// <summary>No provider configured yet, or sync switched off — grey.</summary>
    NotConfigured,

    /// <summary>Local and remote agree as of the last completed sync — green.</summary>
    Synced,

    /// <summary>An upload or download is in flight — blue.</summary>
    Syncing,

    /// <summary>The last attempt failed; <see cref="CloudSyncService.StatusMessage"/> says why — red.</summary>
    Error
}
