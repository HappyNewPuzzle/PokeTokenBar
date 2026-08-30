namespace PokeTokenBar.Windows.Core;

public interface IAntigravityRateLimitsProvider
{
    Task<AntigravityRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default);
}
