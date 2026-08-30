namespace PokeTokenBar.Windows.Core;

public interface IAntigravityCredentialProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
