using Google.Apis.Auth.OAuth2;

namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Finds the Google OAuth desktop client. Deliberately never in source: the repo is public.
/// Order: environment variables, then <c>google-client.json</c> in the app data folder, then a
/// copy embedded at build time (present only when a gitignored <c>google-client.json</c> sat
/// next to the Core project).
/// </summary>
public static class GoogleDriveConfig
{
    private const string FileName = "google-client.json";

    public static string AppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataLocation.FolderName);

    private static readonly Lazy<ClientSecrets?> Cached = new(Load);

    public static ClientSecrets? Secrets => Cached.Value;

    private static ClientSecrets? Load()
    {
        var id = Environment.GetEnvironmentVariable("VSTMANAGER_GDRIVE_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("VSTMANAGER_GDRIVE_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret))
        {
            return new ClientSecrets { ClientId = id.Trim(), ClientSecret = secret.Trim() };
        }

        try
        {
            var path = Path.Combine(AppDataFolder, FileName);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                return GoogleClientSecrets.FromStream(file).Secrets;
            }

            using var embedded = typeof(GoogleDriveConfig).Assembly
                .GetManifestResourceStream("VstManager.Core." + FileName);
            return embedded is null ? null : GoogleClientSecrets.FromStream(embedded).Secrets;
        }
        catch (Exception)
        {
            // A malformed file must leave Drive "not set up", not crash startup.
            return null;
        }
    }
}
