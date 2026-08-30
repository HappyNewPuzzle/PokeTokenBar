namespace PokeTokenBar.Windows.Core;

public interface ICodexRateLimitsProvider
{
    Task<CodexRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default);
}
