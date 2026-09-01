using System.Net.Http.Headers;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    public static readonly Uri LatestReleaseApi =
        new("https://api.github.com/repos/chattymin/PokeTokenBar/releases/latest");

    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateChecker(HttpClient httpClient, string currentVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion;
    }

    public string CurrentVersion { get; }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"PokeTokenBar/{CurrentVersion}");
            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Failed();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = json.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
                root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean() ||
                !root.TryGetProperty("tag_name", out var tagElement) ||
                !root.TryGetProperty("html_url", out var urlElement))
            {
                return Failed();
            }

            var latest = tagElement.GetString()?.Trim().TrimStart('v', 'V');
            var urlText = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(latest) ||
                !Uri.TryCreate(urlText, UriKind.Absolute, out var releaseUri) ||
                releaseUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return Failed();
            }

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
            return ReleaseVersion.IsNewer(latest, CurrentVersion)
                ? new(UpdateCheckStatus.Available, CurrentVersion, latest, releaseUri, notes)
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

    private UpdateCheckResult Failed() => new(UpdateCheckStatus.Failed, CurrentVersion);
}
