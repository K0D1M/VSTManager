using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VstManager.Core.Services.Cloud;

/// <summary>
/// Sync through the user's own Dropbox, in the app's private folder (<c>Apps/&lt;app name&gt;</c>).
/// Like Google Drive, pairing is implicit: machines signed into the same Dropbox account share
/// the same file, so the sync id isn't used. Talks to the HTTP API directly — sign-in, token
/// refresh, metadata, download, upload and revoke are the whole surface, not worth an SDK.
/// </summary>
public sealed class DropboxSyncProvider : ICloudSyncProvider, IDisposable
{
#if NO_LICENSE_STATUS
    private const string RemotePath = "/vstmanager-nostatus-settings.json";
#else
    private const string RemotePath = "/vstmanager-settings.json";
#endif
    private const string ApiBase = "https://api.dropboxapi.com/2/";
    private const string ContentBase = "https://content.dropboxapi.com/2/";
    private const string TokenUrl = "https://api.dropboxapi.com/oauth2/token";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private SavedToken? _token;

    public string DisplayName => "Dropbox";

    public bool IsConfigured => IsSignedIn;

    public bool IsSignedIn => _token is not null;

    public string? SignedInEmail => _token?.Email;

    /// <summary>Loads a saved sign-in from disk. No network, no browser.</summary>
    public Task RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(DropboxConfig.TokenFile))
            {
                _token = JsonSerializer.Deserialize<SavedToken>(File.ReadAllText(DropboxConfig.TokenFile));
                if (string.IsNullOrEmpty(_token?.RefreshToken))
                {
                    _token = null;
                }
            }
        }
        catch (Exception)
        {
            _token = null;   // Unreadable token file: behave as signed out.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens Dropbox's consent page in the system browser and waits for it to redirect back to a
    /// one-shot listener on the registered loopback address (PKCE: no app secret involved).
    /// </summary>
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add(DropboxConfig.RedirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Couldn't listen on {DropboxConfig.RedirectUri} for the sign-in reply ({ex.Message}). " +
                "Is another program using that port?", ex);
        }

        var authorizeUrl = "https://www.dropbox.com/oauth2/authorize" +
                           $"?client_id={Uri.EscapeDataString(DropboxConfig.AppKey)}" +
                           "&response_type=code&token_access_type=offline" +
                           $"&code_challenge={challenge}&code_challenge_method=S256" +
                           $"&redirect_uri={Uri.EscapeDataString(DropboxConfig.RedirectUri)}" +
                           $"&state={state}";
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        HttpListenerContext context;
        await using (cancellationToken.Register(listener.Stop))
        {
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        var query = context.Request.QueryString;
        var error = query["error_description"] ?? query["error"];
        var ok = error is null && query["state"] == state && !string.IsNullOrEmpty(query["code"]);
        await RespondAsync(context, ok
            ? "Signed in to Dropbox. You can close this tab and return to VST Manager."
            : "Dropbox sign-in didn't complete. You can close this tab and try again from VST Manager.");

        if (!ok)
        {
            throw new InvalidOperationException(error ?? "Dropbox returned an unexpected reply.");
        }

        var tokens = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = query["code"]!,
            ["client_id"] = DropboxConfig.AppKey,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = DropboxConfig.RedirectUri
        }, cancellationToken);

        var token = new SavedToken
        {
            RefreshToken = tokens["refresh_token"]!.GetValue<string>(),
            AccessToken = tokens["access_token"]!.GetValue<string>(),
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokens["expires_in"]?.GetValue<int>() ?? 14400)
        };
        _token = token;
        token.Email = await FetchEmailAsync(cancellationToken);
        Save();
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_token is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + "auth/token/revoke");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancellationToken));
                using var _ = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception)
            {
                // Offline or already revoked: forgetting the local token is what matters.
            }
        }

        _token = null;
        try
        {
            File.Delete(DropboxConfig.TokenFile);
        }
        catch (IOException)
        {
        }
    }

    public async Task<DateTime?> GetRemoteTimestampAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync(ApiBase + "files/get_metadata",
            new StringContent(JsonSerializer.Serialize(new { path = RemotePath }), Encoding.UTF8, "application/json"),
            argHeader: null, cancellationToken);
        if (await IsNotFoundAsync(response))
        {
            return null;
        }

        var meta = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return meta?["server_modified"]?.GetValue<DateTime>().ToUniversalTime();
    }

    public async Task<string?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync(ContentBase + "files/download", content: null,
            JsonSerializer.Serialize(new { path = RemotePath }), cancellationToken);
        if (await IsNotFoundAsync(response))
        {
            return null;
        }

        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    public async Task UploadAsync(string json, CancellationToken cancellationToken = default)
    {
        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendApiAsync(ContentBase + "files/upload", body,
            JsonSerializer.Serialize(new { path = RemotePath, mode = "overwrite", mute = true }), cancellationToken);
        await EnsureOkAsync(response);
    }

    // ---- plumbing ------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendApiAsync(string url, HttpContent? content, string? argHeader,
                                                         CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancellationToken));
        if (argHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", argHeader);
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    /// <summary>A valid access token, refreshed with the stored refresh token when near expiry.</summary>
    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _token ?? throw new InvalidOperationException("Sign in to Dropbox first.");
        if (token.AccessToken is not null && token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2))
        {
            return token.AccessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (token.AccessToken is null || token.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(2))
            {
                var fresh = await PostTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = token.RefreshToken,
                    ["client_id"] = DropboxConfig.AppKey
                }, cancellationToken);
                token.AccessToken = fresh["access_token"]!.GetValue<string>();
                token.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(fresh["expires_in"]?.GetValue<int>() ?? 14400);
                Save();
            }

            return token.AccessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonNode> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Dropbox sign-in failed: {DescribeError(text)}");
        }

        return JsonNode.Parse(text)!;
    }

    private async Task<string?> FetchEmailAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendApiAsync(ApiBase + "users/get_current_account", content: null,
                argHeader: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["email"]?.GetValue<string>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Dropbox answers 409 with a "not_found" path error when the file doesn't exist yet.</summary>
    private static async Task<bool> IsNotFoundAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (text.Contains("not_found", StringComparison.Ordinal))
            {
                return true;
            }

            throw new InvalidOperationException($"Dropbox: {DescribeError(text)}");
        }

        await EnsureOkAsync(response);
        return false;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Dropbox returned {(int)response.StatusCode}: {DescribeError(text)}");
        }
    }

    private static string DescribeError(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["error_summary"]?.GetValue<string>()
                   ?? node?["error_description"]?.GetValue<string>()
                   ?? node?["error"]?.ToString()
                   ?? body;
        }
        catch (JsonException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, string message)
    {
        var html = "<!doctype html><meta charset=\"utf-8\"><title>VST Manager</title>" +
                   "<body style=\"font-family:system-ui,sans-serif;background:#121218;color:#EDEDF2;" +
                   "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">" +
                   $"<p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p></body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private void Save()
    {
        if (_token is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DropboxConfig.TokenFile)!);
        File.WriteAllText(DropboxConfig.TokenFile, JsonSerializer.Serialize(_token));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _http.Dispose();
        _tokenLock.Dispose();
    }

    private sealed class SavedToken
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string? Email { get; set; }
    }
}
