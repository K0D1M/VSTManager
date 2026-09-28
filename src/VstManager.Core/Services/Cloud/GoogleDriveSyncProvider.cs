using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Sync through the user's own Google Drive, in the hidden per-app <c>appDataFolder</c>: the user
/// can't see or accidentally delete it, and the narrow <c>drive.appdata</c> scope needs no Google
/// verification review. Pairing is implicit — machines signed into the same Google account share
/// the same folder, so the sync id isn't used here.
/// </summary>
public sealed class GoogleDriveSyncProvider : ICloudSyncProvider, IDisposable
{
#if NO_LICENSE_STATUS
    private const string RemoteFileName = "vstmanager-nostatus-settings.json";
#else
    private const string RemoteFileName = "vstmanager-settings.json";
#endif
    private const string UserKey = "user";
    private static readonly string[] Scopes = [DriveService.Scope.DriveAppdata];

    private readonly FileDataStore _tokenStore =
        new(Path.Combine(GoogleDriveConfig.AppDataFolder, "gdrive-token"), fullPath: true);

    private UserCredential? _credential;
    private DriveService? _service;

    public string DisplayName => "Google Drive";

    public bool HasClient => GoogleDriveConfig.Secrets is not null;

    public bool IsConfigured => HasClient && IsSignedIn;

    public bool IsSignedIn { get; private set; }

    public string? SignedInEmail { get; private set; }

    /// <summary>Restores a saved sign-in without opening a browser. Silent if there is none.</summary>
    public async Task RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (GoogleDriveConfig.Secrets is not { } secrets)
        {
            return;
        }

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = _tokenStore
        });

        var token = await flow.LoadTokenAsync(UserKey, cancellationToken);
        if (token is null)
        {
            return;
        }

        await AttachAsync(new UserCredential(flow, UserKey, token), cancellationToken);
    }

    /// <summary>Opens the system browser for Google's consent page and waits for the answer.</summary>
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var secrets = GoogleDriveConfig.Secrets
            ?? throw new InvalidOperationException("Google Drive isn't set up in this build.");

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, UserKey, cancellationToken, _tokenStore);
        await AttachAsync(credential, cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is not null)
        {
            try
            {
                await _credential.RevokeTokenAsync(cancellationToken);
            }
            catch (Exception)
            {
                // Offline or already revoked: forgetting the local token is what matters.
            }
        }

        await _tokenStore.ClearAsync();
        _service?.Dispose();
        _service = null;
        _credential = null;
        IsSignedIn = false;
        SignedInEmail = null;
    }

    private async Task AttachAsync(UserCredential credential, CancellationToken cancellationToken)
    {
        _credential = credential;
        _service?.Dispose();
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "VST Manager"
        });
        IsSignedIn = true;

        try
        {
            // The appdata scope can't read the profile, but about.get's user field is allowed.
            var about = _service.About.Get();
            about.Fields = "user(emailAddress)";
            SignedInEmail = (await about.ExecuteAsync(cancellationToken)).User?.EmailAddress;
        }
        catch (Exception)
        {
            SignedInEmail = null;
        }
    }

    public async Task<DateTime?> GetRemoteTimestampAsync(CancellationToken cancellationToken = default) =>
        (await FindRemoteAsync(cancellationToken))?.ModifiedTimeDateTimeOffset?.UtcDateTime;

    public async Task<string?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var remote = await FindRemoteAsync(cancellationToken);
        if (remote is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        await RequireService().Files.Get(remote.Id).DownloadAsync(stream, cancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task UploadAsync(string json, CancellationToken cancellationToken = default)
    {
        var service = RequireService();
        var remote = await FindRemoteAsync(cancellationToken);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var progress = remote is null
            ? await service.Files.Create(
                    new DriveFile { Name = RemoteFileName, Parents = ["appDataFolder"] },
                    content, "application/json")
                .UploadAsync(cancellationToken)
            : await service.Files.Update(new DriveFile(), remote.Id, content, "application/json")
                .UploadAsync(cancellationToken);

        if (progress.Exception is not null)
        {
            throw progress.Exception;
        }
    }

    private async Task<DriveFile?> FindRemoteAsync(CancellationToken cancellationToken)
    {
        var list = RequireService().Files.List();
        list.Spaces = "appDataFolder";
        list.Q = $"name = '{RemoteFileName}' and trashed = false";
        list.Fields = "files(id, modifiedTime)";
        list.OrderBy = "modifiedTime desc";
        list.PageSize = 1;
        var result = await list.ExecuteAsync(cancellationToken);
        return result.Files?.FirstOrDefault();
    }

    private DriveService RequireService() =>
        _service ?? throw new InvalidOperationException("Sign in to Google Drive first.");

    public void Dispose() => _service?.Dispose();
}
