using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class ClaudeCredentialProvider : IClaudeCredentialProvider
{
    private readonly string _filePath;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _credentialAccessEnabled;

    public ClaudeCredentialProvider(
        string? filePath = null,
        TimeProvider? timeProvider = null,
        Func<bool>? credentialAccessEnabled = null)
    {
        _filePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
        _timeProvider = timeProvider ?? TimeProvider.System;
        _credentialAccessEnabled = credentialAccessEnabled ?? (() => true);
    }

    public string FilePath => _filePath;

    public static string GetDefaultFilePath(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    public async Task<ClaudeOAuthCredential?> GetCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_credentialAccessEnabled())
        {
            return null;
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                oauth.ValueKind != JsonValueKind.Object ||
                !oauth.TryGetProperty("accessToken", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                return null;
            }

            var credential = new ClaudeOAuthCredential(
                tokenElement.GetString()!,
                ParseExpiry(oauth),
                String(oauth, "subscriptionType"),
                String(oauth, "rateLimitTier"));
            return credential.ExpiresAt is DateTimeOffset expiresAt &&
                   expiresAt <= _timeProvider.GetUtcNow().AddMinutes(1)
                ? null
                : credential;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseExpiry(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("expiresAt", out var value))
        {
            return null;
        }

        double raw;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            raw = number;
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            raw = number;
        }
        else
        {
            return null;
        }

        if (!double.IsFinite(raw) || raw <= 0)
        {
            return null;
        }

        var seconds = raw > 10_000_000_000 ? raw / 1000 : raw;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
