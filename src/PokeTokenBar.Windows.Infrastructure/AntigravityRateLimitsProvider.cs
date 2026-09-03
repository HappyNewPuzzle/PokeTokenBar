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
    public static readonly Uri GoogleTokenUri = new("https://oauth2.googleapis.com/token");
    internal const string GoogleClientId =
        "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _httpClient;
    private readonly IAntigravityCredentialProvider _credentials;
    private readonly IReadOnlyList<Uri> _endpoints;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _credentialAccessEnabled;
    private AntigravityOAuthCredential? _cachedCredential;

    public AntigravityRateLimitsProvider()
        : this(SharedHttpClient, new AntigravityCredentialProvider(), GetDefaultEndpoints())
    {
    }

    public AntigravityRateLimitsProvider(Func<bool> credentialAccessEnabled)
        : this(
            SharedHttpClient,
            new AntigravityCredentialProvider(credentialAccessEnabled),
            GetDefaultEndpoints(),
            credentialAccessEnabled: credentialAccessEnabled)
    {
    }

    public AntigravityRateLimitsProvider(
        HttpClient httpClient,
        IAntigravityCredentialProvider credentials,
        IEnumerable<Uri>? endpoints = null,
        TimeProvider? timeProvider = null,
        Func<bool>? credentialAccessEnabled = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _endpoints = (endpoints ?? GetDefaultEndpoints()).ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _credentialAccessEnabled = credentialAccessEnabled ?? (() => true);
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
        if (!_credentialAccessEnabled())
        {
            _cachedCredential = null;
            return null;
        }

        var token = await GetAccessTokenAsync(bypassCache: false, cancellationToken)
            .ConfigureAwait(false);
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
            _cachedCredential = null;
            var refreshed = await GetAccessTokenAsync(bypassCache: true, cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is null || refreshed == token)
            {
                throw;
            }

            return await FetchStatusAsync(refreshed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> GetAccessTokenAsync(
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (!bypassCache && _cachedCredential is { } cached && !cached.IsExpired(_timeProvider))
        {
            return cached.AccessToken;
        }

        var credential = await _credentials.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        if (credential.IsExpired(_timeProvider) && !string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            credential = await RefreshGoogleTokenAsync(credential, cancellationToken)
                .ConfigureAwait(false) ?? credential;
        }

        _cachedCredential = credential;
        return credential.AccessToken;
    }

    private async Task<AntigravityOAuthCredential?> RefreshGoogleTokenAsync(
        AntigravityOAuthCredential credential,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, GoogleTokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = GoogleClientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = credential.RefreshToken!,
                }),
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("access_token", out var access) ||
                access.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(access.GetString()))
            {
                return null;
            }

            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry) &&
                            expiry.TryGetDouble(out var seconds)
                ? seconds
                : 3600;
            return new AntigravityOAuthCredential(
                access.GetString()!,
                credential.RefreshToken,
                _timeProvider.GetUtcNow().AddSeconds(expiresIn));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return null;
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
