namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Forwards to whichever provider Settings has selected, so <see cref="CloudSyncService"/> can be
/// built once and keep its event wiring when the user switches between MEGA and Google Drive.
/// </summary>
public sealed class SwitchableCloudProvider(ICloudSyncProvider initial) : ICloudSyncProvider
{
    public ICloudSyncProvider Current { get; set; } = initial;

    public string DisplayName => Current.DisplayName;

    public bool IsConfigured => Current.IsConfigured;

    public Task<DateTime?> GetRemoteTimestampAsync(CancellationToken cancellationToken = default) =>
        Current.GetRemoteTimestampAsync(cancellationToken);

    public Task<string?> DownloadAsync(CancellationToken cancellationToken = default) =>
        Current.DownloadAsync(cancellationToken);

    public Task UploadAsync(string json, CancellationToken cancellationToken = default) =>
        Current.UploadAsync(json, cancellationToken);
}
