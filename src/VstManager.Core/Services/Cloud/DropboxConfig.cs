namespace VstManager.Core.Services.Cloud;

/// <summary>
/// The Dropbox app VST Manager signs in through ("App folder" access, so it only ever sees
/// <c>Apps/&lt;app name&gt;</c> in the user's Dropbox). Sign-in uses OAuth PKCE, which needs no app
/// secret, so the app key is not sensitive and can live in source. An environment variable
/// overrides it for testing against a different app.
/// </summary>
public static class DropboxConfig
{
    private const string BuiltInAppKey = "3kdsis0d5y4zuwp";

    /// <summary>Must match a Redirect URI registered in the Dropbox app console exactly.</summary>
    public const int RedirectPort = 53682;

    public static string RedirectUri => $"http://127.0.0.1:{RedirectPort}/";

    public static string AppKey =>
        Environment.GetEnvironmentVariable("VSTMANAGER_DROPBOX_APP_KEY") is { Length: > 0 } key
            ? key.Trim()
            : BuiltInAppKey;

    public static string TokenFile => Path.Combine(GoogleDriveConfig.AppDataFolder, "dropbox-token.json");
}
