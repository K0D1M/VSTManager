using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Sync against the app's own MEGA S4 bucket. S4 speaks the S3 protocol, so this is the stock
/// AWS SDK pointed at MEGA's endpoint — no MEGA-specific library involved. Path-style addressing
/// is required; S4 doesn't serve virtual-host style bucket URLs.
/// </summary>
public sealed class MegaS4SyncProvider : ICloudSyncProvider, IDisposable
{
    private readonly string _bucket;
    private readonly string _objectKey;
    private readonly Lazy<AmazonS3Client>? _client;

    public string DisplayName => "MEGA (VST Manager cloud)";

    public bool IsConfigured => _client is not null;

    /// <param name="deviceId">
    /// Identifies whose settings these are. Objects live at <c>users/{deviceId}/settings.json</c>,
    /// so a second machine syncs with the first only when it carries the same id.
    /// </param>
    public MegaS4SyncProvider(string deviceId)
    {
        _bucket = MegaS4Config.ResolvedBucketName;
        _objectKey = $"users/{deviceId}/settings.json";

        if (!MegaS4Config.IsComplete)
        {
            return;
        }

        _client = new Lazy<AmazonS3Client>(() => new AmazonS3Client(
            new BasicAWSCredentials(MegaS4Config.ResolvedAccessKeyId, MegaS4Config.ResolvedSecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = MegaS4Config.ResolvedServiceUrl,
                AuthenticationRegion = MegaS4Config.ResolvedRegion,
                ForcePathStyle = true,

                // Recent AWS SDKs default to sending a CRC32 checksum in a chunked trailer.
                // S4 rejects that with "Trailer signature verification failed", so ask for
                // checksums only where the operation genuinely requires them.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            }));
    }

    public async Task<DateTime?> GetRemoteTimestampAsync(CancellationToken cancellationToken = default)
    {
        var client = RequireClient();

        try
        {
            var metadata = await client.GetObjectMetadataAsync(_bucket, _objectKey, cancellationToken);
            return metadata.LastModified.ToUniversalTime();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Nothing uploaded yet — a first sync from this machine, not a failure.
            return null;
        }
    }

    public async Task<string?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var client = RequireClient();

        try
        {
            using var response = await client.GetObjectAsync(_bucket, _objectKey, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UploadAsync(string json, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = _objectKey,
            ContentBody = json,
            ContentType = "application/json"
        }, cancellationToken);
    }

    private AmazonS3Client RequireClient() =>
        _client?.Value ?? throw new InvalidOperationException(
            "The MEGA cloud isn't configured — fill in MegaS4Config or set the VSTMANAGER_S4_* environment variables.");

    public void Dispose()
    {
        if (_client is { IsValueCreated: true })
        {
            _client.Value.Dispose();
        }
    }
}
