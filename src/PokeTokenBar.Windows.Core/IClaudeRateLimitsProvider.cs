namespace PokeTokenBar.Windows.Core;

public interface IClaudeRateLimitsProvider
{
    Task<ClaudeRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default);
}
