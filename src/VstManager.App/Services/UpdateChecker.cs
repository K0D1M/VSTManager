using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VstManager.App.Services;

public record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion, string? ReleaseUrl, string? AssetDownloadUrl, string? Error);

public class UpdateChecker
{
    private const string RepoOwner = "K0D1M";
    private const string RepoName = "VSTManager";

    /// <summary>
    /// The exact asset filename a published GitHub release must use for auto-update to find
    /// and download it. If a release has no asset with this name (e.g. an older release, or one
    /// where the installer wasn't uploaded), auto-update falls back to just opening the release
    /// page for a manual download.
    /// </summary>
    public const string InstallerAssetName = "VstManagerSetup.exe";

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

            var assetUrl = release.Assets?
                .FirstOrDefault(a => string.Equals(a.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;

            return new UpdateCheckResult(isNewer, latestVersion, release.HtmlUrl, assetUrl, null);
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
    public async Task<bool> DownloadInstallerAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(destinationPath);
            await httpStream.CopyToAsync(fileStream, cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
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

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
