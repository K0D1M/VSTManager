namespace VstManager.Core.Services.Cloud;

/// <summary>Which side wins when local and remote have both changed since the last sync.</summary>
public enum ConflictResolution
{
    /// <summary>Leave both sides alone until the user decides.</summary>
    Skip,
    KeepLocal,
    KeepRemote
}

/// <summary>
/// Drives the cloud indicator: decides on each run whether to upload, download, or ask, and
/// reports state transitions so the toolbar icon can follow along. Provider-agnostic — it only
/// ever sees the settings blob as a string.
/// </summary>
public sealed class CloudSyncService
{
    private readonly ICloudSyncProvider _provider;
    private readonly DataPortabilityService _dataPortability;
    private readonly string _localFilePath;
    private readonly Func<DateTime?> _readLastSyncedAt;
    private readonly Action<DateTime> _writeLastSyncedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _debounce;

    /// <summary>
    /// Asked only when both sides changed since the last successful sync. Left unset, such a run
    /// resolves to <see cref="ConflictResolution.Skip"/> — never a silent overwrite.
    /// </summary>
    public Func<DateTime, DateTime, Task<ConflictResolution>>? ConflictResolver { get; set; }

    public CloudSyncState State { get; private set; } = CloudSyncState.NotConfigured;

    /// <summary>Human-readable detail for the icon's tooltip — a failure reason, or the last sync time.</summary>
    public string StatusMessage { get; private set; } = "Cloud sync is off.";

    public event EventHandler? StateChanged;

    /// <summary>Raised after a download has replaced the local files, so the UI can reload.</summary>
    public event EventHandler? RemoteDataApplied;

    public CloudSyncService(
        ICloudSyncProvider provider,
        DataPortabilityService dataPortability,
        Func<DateTime?> readLastSyncedAt,
        Action<DateTime> writeLastSyncedAt,
        string? localFilePath = null)
    {
        _provider = provider;
        _dataPortability = dataPortability;
        _readLastSyncedAt = readLastSyncedAt;
        _writeLastSyncedAt = writeLastSyncedAt;
        _localFilePath = localFilePath ?? LibraryStore.GetDefaultPath();
    }

    /// <summary>
    /// Queues a sync a few seconds out, restarting the timer on each call. Settings toggles fire
    /// this so a burst of changes turns into one upload rather than one per click.
    /// </summary>
    public void RequestSyncDebounced(TimeSpan? delay = null)
    {
        if (!_provider.IsConfigured)
        {
            return;
        }

        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay ?? TimeSpan.FromSeconds(5), token);
                await SyncAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request, or shutting down.
            }
        }, CancellationToken.None);
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_provider.IsConfigured)
        {
            SetState(CloudSyncState.NotConfigured, "Cloud sync isn't set up yet.");
            return;
        }

        // One sync at a time: a debounced run and a manual click can otherwise overlap and
        // upload stale content on top of fresh.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            SetState(CloudSyncState.Syncing, $"Syncing with {_provider.DisplayName}...");

            var remoteAt = await _provider.GetRemoteTimestampAsync(cancellationToken);
            var localAt = File.Exists(_localFilePath)
                ? File.GetLastWriteTimeUtc(_localFilePath)
                : (DateTime?)null;
            var lastSyncedAt = _readLastSyncedAt();

            if (localAt is null && remoteAt is null)
            {
                MarkSynced("Nothing to sync yet.");
                return;
            }

            if (remoteAt is null)
            {
                await UploadAsync(cancellationToken);
                return;
            }

            if (localAt is null)
            {
                await DownloadAsync(cancellationToken);
                return;
            }

            // A second of slack absorbs filesystem/clock granularity, which would otherwise make
            // a just-synced pair of files look changed on the very next run.
            var slack = TimeSpan.FromSeconds(1);
            var localChanged = lastSyncedAt is null || localAt > lastSyncedAt + slack;
            var remoteChanged = lastSyncedAt is null || remoteAt > lastSyncedAt + slack;

            switch (localChanged, remoteChanged)
            {
                case (false, false):
                    MarkSynced($"Up to date with {_provider.DisplayName}.");
                    break;
                case (true, false):
                    await UploadAsync(cancellationToken);
                    break;
                case (false, true):
                    await DownloadAsync(cancellationToken);
                    break;
                case (true, true):
                    await ResolveConflictAsync(localAt.Value, remoteAt.Value, cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetState(CloudSyncState.Error, $"Sync failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the local copy up, ignoring whatever is already there. Used by "Upload now".</summary>
    public async Task ForceUploadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetState(CloudSyncState.Syncing, "Uploading settings...");
            await UploadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(CloudSyncState.Error, $"Upload failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pulls the remote copy down over the local one. Used by "Restore from cloud".</summary>
    public async Task ForceDownloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetState(CloudSyncState.Syncing, "Restoring settings...");
            await DownloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(CloudSyncState.Error, $"Restore failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UploadAsync(CancellationToken cancellationToken)
    {
        await _provider.UploadAsync(_dataPortability.ExportBundle(), cancellationToken);
        MarkSynced($"Settings uploaded to {_provider.DisplayName}.");
    }

    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var json = await _provider.DownloadAsync(cancellationToken);
        if (json is null)
        {
            MarkSynced("Nothing to restore.");
            return;
        }

        _dataPortability.ImportBundle(json);
        MarkSynced($"Settings restored from {_provider.DisplayName}.");
        RemoteDataApplied?.Invoke(this, EventArgs.Empty);
    }

    private async Task ResolveConflictAsync(DateTime localAt, DateTime remoteAt, CancellationToken cancellationToken)
    {
        var resolver = ConflictResolver;
        if (resolver is null)
        {
            SetState(CloudSyncState.Error, "Both copies changed — open Settings → Cloud to pick one.");
            return;
        }

        switch (await resolver(localAt, remoteAt))
        {
            case ConflictResolution.KeepLocal:
                await UploadAsync(cancellationToken);
                break;
            case ConflictResolution.KeepRemote:
                await DownloadAsync(cancellationToken);
                break;
            default:
                SetState(CloudSyncState.Error, "Both copies changed — sync is paused until you choose one.");
                break;
        }
    }

    private void MarkSynced(string message)
    {
        // Stamped from the local clock because that's what the next run's file-mtime comparison
        // is measured against; the remote's own timestamp would drift against it.
        _writeLastSyncedAt(DateTime.UtcNow);
        SetState(CloudSyncState.Synced, message);
    }

    private void SetState(CloudSyncState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
