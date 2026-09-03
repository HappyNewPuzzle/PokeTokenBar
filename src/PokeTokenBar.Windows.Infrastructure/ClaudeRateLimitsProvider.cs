using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class ClaudeRateLimitsProvider : IClaudeRateLimitsProvider
{
    private static readonly Uri UsageUri = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly Uri ProfileUri = new("https://api.anthropic.com/api/oauth/profile");
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _httpClient;
    private readonly IClaudeCredentialProvider _credentials;

    public ClaudeRateLimitsProvider()
        : this(SharedHttpClient, new ClaudeCredentialProvider())
    {
    }

    public ClaudeRateLimitsProvider(Func<bool> credentialAccessEnabled)
        : this(SharedHttpClient,
            new ClaudeCredentialProvider(credentialAccessEnabled: credentialAccessEnabled))
    {
    }

    public ClaudeRateLimitsProvider(
        HttpClient httpClient,
        IClaudeCredentialProvider credentials)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public async Task<ClaudeRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentials.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        var response = await SendAsync(UsageUri, credential.AccessToken, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            var refreshed = await _credentials.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed is null || refreshed.AccessToken == credential.AccessToken)
            {
                throw new HttpRequestException("Claude OAuth credential was rejected.", null, statusCode);
            }

            credential = refreshed;
            response = await SendAsync(UsageUri, credential.AccessToken, cancellationToken)
                .ConfigureAwait(false);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var parsed = Parse(document.RootElement, credential);
            var identity = await FetchIdentityAsync(credential.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            return parsed with
            {
                AccountEmail = identity.Email,
                AccountOrganizationName = identity.Organization,
            };
        }
    }

    public static ClaudeRateLimitStatus Parse(
        JsonElement root,
        ClaudeOAuthCredential credential)
    {
        var fiveHour = WindowProperty(root, "five_hour");
        var sevenDay = WindowProperty(root, "seven_day");
        if ((fiveHour is null || sevenDay is null) &&
            root.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in limits.EnumerateArray())
            {
                var kind = String(item, "kind");
                if (fiveHour is null && kind == "session")
                {
                    fiveHour = WindowValue(item, "percent");
                }
                else if (sevenDay is null && kind == "weekly_all")
                {
                    sevenDay = WindowValue(item, "percent");
                }
            }
        }

        return new ClaudeRateLimitStatus(
            fiveHour,
            sevenDay,
            credential.SubscriptionType,
            credential.RateLimitTier,
            AccountEmail: null,
            AccountOrganizationName: null);
    }

    private async Task<(string? Email, string? Organization)> FetchIdentityAsync(
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(ProfileUri, token, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var email = root.TryGetProperty("account", out var account)
                ? String(account, "email")
                : null;
            var organization = root.TryGetProperty("organization", out var org)
                ? String(org, "name")
                : null;
            return string.IsNullOrWhiteSpace(email) ? default : (email, organization);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return default;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static ClaudeRateLimitWindow? WindowProperty(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return WindowValue(value, "utilization");
    }

    private static ClaudeRateLimitWindow? WindowValue(JsonElement value, string percentProperty)
    {
        if (!value.TryGetProperty(percentProperty, out var percent) ||
            percent.ValueKind != JsonValueKind.Number ||
            !percent.TryGetDouble(out var used) ||
            !double.IsFinite(used))
        {
            return null;
        }

        DateTimeOffset? reset = null;
        var rawReset = String(value, "resets_at");
        if (rawReset is not null && DateTimeOffset.TryParse(rawReset, out var parsed))
        {
            reset = parsed;
        }

        return new ClaudeRateLimitWindow(used, reset);
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
