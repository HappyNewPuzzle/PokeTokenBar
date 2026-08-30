using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class AntigravityRateLimitsProvider : IAntigravityRateLimitsProvider
{
    public static readonly Uri DailyUri = new(
        "https://daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary");
    public static readonly Uri PrimaryUri = new(
        "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary");
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _httpClient;
    private readonly IAntigravityCredentialProvider _credentials;
    private readonly IReadOnlyList<Uri> _endpoints;

    public AntigravityRateLimitsProvider()
        : this(SharedHttpClient, new AntigravityCredentialProvider(), GetDefaultEndpoints())
    {
    }

    public AntigravityRateLimitsProvider(
        HttpClient httpClient,
        IAntigravityCredentialProvider credentials,
        IEnumerable<Uri>? endpoints = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _endpoints = (endpoints ?? GetDefaultEndpoints()).ToArray();
    }

    public static IReadOnlyList<Uri> GetDefaultEndpoints(string? cloudCodeUrl = null)
    {
        cloudCodeUrl ??= Environment.GetEnvironmentVariable("CLOUD_CODE_URL");
        var result = new List<Uri>();
        if (!string.IsNullOrWhiteSpace(cloudCodeUrl) &&
            Uri.TryCreate(
                cloudCodeUrl.TrimEnd('/') + "/v1internal:retrieveUserQuotaSummary",
                UriKind.Absolute,
                out var configured))
        {
            result.Add(configured);
        }

        result.Add(DailyUri);
        result.Add(PrimaryUri);
        return result.AsReadOnly();
    }

    public async Task<AntigravityRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _credentials.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        try
        {
            return await FetchStatusAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var refreshed = await _credentials.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed is null || refreshed == token)
            {
                throw;
            }

            return await FetchStatusAsync(refreshed, cancellationToken).ConfigureAwait(false);
        }
    }

    public static AntigravityRateLimitStatus Parse(JsonElement root)
    {
        var parsed = JsonSerializer.Deserialize<AntigravityRateLimitStatus>(root.GetRawText()) ??
            new AntigravityRateLimitStatus([], null);
        var groups = (parsed.Groups ?? [])
            .Select(group => group with { Buckets = group.Buckets ?? [] })
            .ToArray();
        return parsed with { Groups = groups };
    }

    private async Task<AntigravityRateLimitStatus> FetchStatusAsync(
        string token,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var endpoint in _endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd("antigravity/2.9.1");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException(
                        "Antigravity quota request was rejected.",
                        null,
                        response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastError = new HttpRequestException(
                        "Antigravity quota endpoint failed.",
                        null,
                        response.StatusCode);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Parse(document.RootElement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode is HttpStatusCode.Unauthorized or
                    HttpStatusCode.Forbidden or
                    HttpStatusCode.TooManyRequests)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new HttpRequestException("No Antigravity quota endpoint is available.");
    }
}
