namespace PokeTokenBar.Windows.Core;

public interface IClaudeCredentialProvider
{
    Task<ClaudeOAuthCredential?> GetCredentialAsync(
        CancellationToken cancellationToken = default);
}
