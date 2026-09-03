using System.Text;
using System.Text.Json;
using System.Globalization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class AntigravityCredentialProvider : IAntigravityCredentialProvider
{
    internal const string WindowsCredentialTarget = "gemini:antigravity";
    private const string Base64Prefix = "go-keyring-base64:";
    private readonly IReadOnlyList<string> _filePaths;
    private readonly Func<bool> _credentialAccessEnabled;
    private readonly Func<string?>? _readWindowsCredential;

    public AntigravityCredentialProvider()
        : this(GetDefaultFilePaths(), () => true,
            () => WindowsCredentialStore.Read(WindowsCredentialTarget))
    {
    }

    public AntigravityCredentialProvider(IEnumerable<string> filePaths)
        : this(filePaths, () => true, readWindowsCredential: null)
    {
    }

    public AntigravityCredentialProvider(Func<bool> credentialAccessEnabled)
        : this(GetDefaultFilePaths(), credentialAccessEnabled,
            () => WindowsCredentialStore.Read(WindowsCredentialTarget))
    {
    }

    internal AntigravityCredentialProvider(
        IEnumerable<string> filePaths,
        Func<bool> credentialAccessEnabled,
        Func<string?>? readWindowsCredential)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        _credentialAccessEnabled = credentialAccessEnabled ??
            throw new ArgumentNullException(nameof(credentialAccessEnabled));
        _filePaths = LocalUsageSupport.NormalizeRoots(filePaths);
        _readWindowsCredential = readWindowsCredential;
    }

    public static IReadOnlyList<string> GetDefaultFilePaths(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return LocalUsageSupport.NormalizeRoots(
        [
            Path.Combine(userProfile, ".gemini", "jetski-standalone-oauth-token"),
            Path.Combine(userProfile, ".gemini", "antigravity", "jetski-standalone-oauth-token"),
        ]);
    }

    public async Task<AntigravityOAuthCredential?> GetCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_credentialAccessEnabled())
        {
            return null;
        }

        foreach (var path in _filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("token", out var token) &&
                    token.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(token.GetString()))
                {
                    return new AntigravityOAuthCredential(token.GetString()!);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Continue to the alternate read-only credential location.
            }
        }

        try
        {
            return _readWindowsCredential is null
                ? null
                : ParseCredential(_readWindowsCredential());
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and
            not StackOverflowException and
            not AccessViolationException)
        {
            return null;
        }
    }

    internal static AntigravityOAuthCredential? ParseCredential(string? raw)
    {
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        byte[] json;
        if (raw.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            try
            {
                json = Convert.FromBase64String(raw[Base64Prefix.Length..]);
            }
            catch (FormatException)
            {
                return null;
            }
        }
        else
        {
            json = Encoding.UTF8.GetBytes(raw);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("token", out var token))
            {
                return null;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                var accessToken = token.GetString();
                return string.IsNullOrWhiteSpace(accessToken)
                    ? null
                    : new AntigravityOAuthCredential(accessToken);
            }

            if (token.ValueKind != JsonValueKind.Object ||
                !token.TryGetProperty("access_token", out var access) ||
                access.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(access.GetString()))
            {
                return null;
            }

            var refresh = token.TryGetProperty("refresh_token", out var refreshToken) &&
                          refreshToken.ValueKind == JsonValueKind.String
                ? refreshToken.GetString()
                : null;
            var expiry = token.TryGetProperty("expiry", out var expiryValue) &&
                         expiryValue.ValueKind == JsonValueKind.String &&
                         DateTimeOffset.TryParse(
                             expiryValue.GetString(),
                             CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                             out var parsedExpiry)
                ? parsedExpiry
                : (DateTimeOffset?)null;
            return new AntigravityOAuthCredential(access.GetString()!, refresh, expiry);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
