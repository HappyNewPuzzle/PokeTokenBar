using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class AntigravityCredentialProvider : IAntigravityCredentialProvider
{
    private readonly IReadOnlyList<string> _filePaths;

    public AntigravityCredentialProvider()
        : this(GetDefaultFilePaths())
    {
    }

    public AntigravityCredentialProvider(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        _filePaths = LocalUsageSupport.NormalizeRoots(filePaths);
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

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
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
                    return token.GetString();
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

        return null;
    }
}
