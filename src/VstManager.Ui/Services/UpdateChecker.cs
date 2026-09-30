using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VstManager.Ui.Services;

public record UpdateCheckResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string? AssetDownloadUrl,
    string? Error,
    string? ReleaseNotes = null,
    long? AssetSize = null);

/// <summary>
/// Ported from VstManager.App/Services/UpdateChecker.cs — same GitHub API call, same version
/// comparison, same no-verification download. Deliberately unchanged from WPF's behavior (kept
/// exact per the user's own choice), except for the installer asset name, which is the one thing
/// that has to differ per platform.
/// </summary>
public class UpdateChecker
{
    private const string RepoOwner = "K0D1M";
    private const string RepoName = "VSTManager";

    /// <summary>
    /// The exact asset filename a published GitHub release must use for auto-update to find and
    /// download it. Windows looks for the same installer WPF already publishes; macOS looks for a
    /// disk image the user will start publishing once one exists — this is the one line to change
    /// if that name or format ever changes. If a release has no matching asset (e.g. an older
    /// release, or one where the platform-specific installer wasn't uploaded), auto-update falls
    /// back to just opening the release page for a manual download.
    /// </summary>
    public static string InstallerAssetName =>
        OperatingSystem.IsWindows() ? "VstManagerSetup.exe" : "VstManagerSetup.dmg";

    private readonly HttpClient _httpClient;

    public UpdateChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VstManager", CurrentVersion));
    }

    public static string CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release?.TagName is null)
            {
                return new UpdateCheckResult(false, null, null, null, "No release information found.");
            }

            var latestVersion = release.TagName.TrimStart('v', 'V');
            var isNewer = IsNewerVersion(latestVersion, CurrentVersion);

            var asset = release.Assets?
                .FirstOrDefault(a => string.Equals(a.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase));

            return new UpdateCheckResult(isNewer, latestVersion, release.HtmlUrl, asset?.BrowserDownloadUrl, null,
                release.Body, asset?.Size > 0 ? asset.Size : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateCheckResult(false, null, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Downloads the release installer to a local path. Returns false (never throws) on any
    /// network/IO failure, so the caller can fall back to a manual "open release page" flow.
    /// </summary>
    public async Task<bool> DownloadInstallerAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<(long Read, long? Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = File.Create(destinationPath))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                    read += n;
                    progress?.Report((read, total));
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // A half-written installer must never be launched later by mistake.
            try { File.Delete(destinationPath); } catch (IOException) { }
            return false;
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var latestVersion) && Version.TryParse(current, out var currentVersion))
        {
            return latestVersion > currentVersion;
        }

        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
