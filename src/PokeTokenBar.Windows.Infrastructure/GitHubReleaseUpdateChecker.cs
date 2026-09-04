using System.Net.Http.Headers;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    public static readonly Uri ReleasesApi =
        new("https://api.github.com/repos/HappyNewPuzzle/PokeTokenBar/releases?per_page=100");

    private readonly HttpClient _httpClient;
    private readonly string _currentComparisonVersion;

    public GitHubReleaseUpdateChecker(HttpClient httpClient, string currentVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _currentComparisonVersion = currentVersion;
        CurrentVersion = ReleaseVersion.Canonicalize(currentVersion) ?? "0.0.0";
    }

    public string CurrentVersion { get; }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"PokeTokenBar/{CurrentVersion}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _httpClient.SendAsync(request, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Failed();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return Failed();

            string? latest = null;
            Uri? releaseUri = null;
            string? notes = null;
            string? releaseName = null;
            DateTimeOffset? publishedAt = null;
            foreach (var release in root.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object ||
                    !IsFalse(release, "draft") || !IsFalse(release, "prerelease") ||
                    !release.TryGetProperty("tag_name", out var tagElement) ||
                    !ReleaseVersion.TryParseWindowsTag(tagElement.GetString(), out var version) ||
                    !release.TryGetProperty("html_url", out var urlElement) ||
                    !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri) ||
                    !ReleaseVersion.IsTrustedWindowsReleaseUri(uri, tagElement.GetString()) ||
                    latest is not null && !ReleaseVersion.IsNewer(version, latest))
                {
                    continue;
                }

                latest = version;
                releaseUri = uri;
                notes = release.TryGetProperty("body", out var body) ? body.GetString() : null;
                releaseName = release.TryGetProperty("name", out var name) ? name.GetString() : null;
                publishedAt = release.TryGetProperty("published_at", out var published) &&
                              DateTimeOffset.TryParse(published.GetString(), out var timestamp)
                    ? timestamp : null;
            }

            if (latest is null || releaseUri is null) return Failed();
            return ReleaseVersion.IsNewer(latest, _currentComparisonVersion)
                ? new(UpdateCheckStatus.UpdateAvailable, CurrentVersion, latest, releaseUri, notes, releaseName, publishedAt)
                : new(UpdateCheckStatus.UpToDate, CurrentVersion, latest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed();
        }
    }

    private static bool IsFalse(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.False;

    private UpdateCheckResult Failed() => new(UpdateCheckStatus.Failed, CurrentVersion);
}
