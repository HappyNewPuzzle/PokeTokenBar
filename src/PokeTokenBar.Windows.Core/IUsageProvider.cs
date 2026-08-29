namespace PokeTokenBar.Windows.Core;

public interface IUsageProvider
{
    string Id { get; }

    string DisplayName { get; }

    bool ReportsCost { get; }

    Task<DailyUsage?> FetchDailyAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken = default);
}
