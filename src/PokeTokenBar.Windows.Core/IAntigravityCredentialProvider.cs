namespace PokeTokenBar.Windows.Core;

public interface IAntigravityCredentialProvider
{
    Task<AntigravityOAuthCredential?> GetCredentialAsync(
        CancellationToken cancellationToken = default);
}
