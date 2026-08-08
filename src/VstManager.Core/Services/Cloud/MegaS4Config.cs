namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Connection details for the app's own MEGA S4 bucket (MEGA's S3-compatible object storage).
///
/// Fill the four constants in from the MEGA console: create a private bucket, then create an
/// access key pair under Object Storage → Access keys, and copy the bucket's S3 endpoint. Until
/// they're filled in, <see cref="MegaS4SyncProvider.IsConfigured"/> is false and the cloud icon
/// stays grey instead of throwing at startup.
///
/// Each value can also be overridden at runtime by an environment variable (same name as the
/// constant, prefixed VSTMANAGER_S4_) so a build can be pointed at a test bucket without a
/// recompile.
///
/// Note that anything compiled into a desktop binary is extractable — a key placed here should
/// be scoped to this one bucket and nothing else. If this ships broadly, move to a small
/// server-side endpoint that hands out presigned URLs and leave these blank.
/// </summary>
public static class MegaS4Config
{
    // The bare service host, with no bucket in it — the bucket goes in the path (ForcePathStyle).
    // The bucket-in-hostname form fails TLS: MEGA's certificate doesn't cover that wildcard.
    public const string ServiceUrl = "https://s3.g.megas4.com";
    public const string Region = "eu-central-1";
    public const string BucketName = "kdapps"; // e.g. "vstmanager-sync"
    public const string AccessKeyId = "AKIA2XPLQRA4GE4ZDHWW4GZGCCX5CH2W2YAPDC23OXZ4";
    public const string SecretAccessKey = "wnz65ZTjOfJxnQOxtlAPqiU3t3iSVlyTW6bJ1lbN";

    public static string ResolvedServiceUrl => Resolve("SERVICE_URL", ServiceUrl);
    public static string ResolvedRegion => Resolve("REGION", Region);
    public static string ResolvedBucketName => Resolve("BUCKET", BucketName);
    public static string ResolvedAccessKeyId => Resolve("ACCESS_KEY", AccessKeyId);
    public static string ResolvedSecretAccessKey => Resolve("SECRET_KEY", SecretAccessKey);

    public static bool IsComplete =>
        !string.IsNullOrWhiteSpace(ResolvedServiceUrl)
        && !string.IsNullOrWhiteSpace(ResolvedBucketName)
        && !string.IsNullOrWhiteSpace(ResolvedAccessKeyId)
        && !string.IsNullOrWhiteSpace(ResolvedSecretAccessKey);

    private static string Resolve(string suffix, string compiledDefault)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable($"VSTMANAGER_S4_{suffix}");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? compiledDefault : fromEnvironment;
    }
}
