using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VstManager.App.Services;

public record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion, string? ReleaseUrl, string? Error);

public class UpdateChecker
{
    // TODO: replace with the real "owner/repo" once the GitHub repo hosting releases is created.
    private const string RepoOwner = "yourname";
    private const string RepoName = "vst-manager";

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
                return new UpdateCheckResult(false, null, null, "No release information found.");
            }

            var latestVersion = release.TagName.TrimStart('v', 'V');
            var isNewer = IsNewerVersion(latestVersion, CurrentVersion);

            return new UpdateCheckResult(isNewer, latestVersion, release.HtmlUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateCheckResult(false, null, null, ex.Message);
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
    }
}
