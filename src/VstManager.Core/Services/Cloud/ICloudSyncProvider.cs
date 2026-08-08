namespace VstManager.Core.Services.Cloud;

/// <summary>
/// A remote store that holds one settings blob per device/user. Deliberately tiny: the sync
/// payload is the same JSON string <see cref="DataPortabilityService.ExportBundle"/> produces,
/// so a provider only has to move a string and report when it last changed. Google Drive and a
/// user's own MEGA account slot in behind this same interface later.
/// </summary>
public interface ICloudSyncProvider
{
    /// <summary>Name shown in Settings and in the cloud icon's tooltip.</summary>
    string DisplayName { get; }

    /// <summary>Whether the provider has everything it needs to talk to the remote.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// When the remote copy was last written, or null if nothing has been uploaded yet.
    /// Used to decide upload vs download vs conflict without transferring the whole blob.
    /// </summary>
    Task<DateTime?> GetRemoteTimestampAsync(CancellationToken cancellationToken = default);

    /// <summary>The remote settings JSON, or null if the remote has no copy yet.</summary>
    Task<string?> DownloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the remote copy with <paramref name="json"/>.</summary>
    Task UploadAsync(string json, CancellationToken cancellationToken = default);
}
