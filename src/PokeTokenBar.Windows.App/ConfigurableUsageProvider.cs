using System.IO;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App;

internal sealed class ConfigurableUsageProvider(
    string id,
    string displayName,
    bool reportsCost,
    Func<IReadOnlyList<string>> defaultRoots,
    Func<IReadOnlyList<string>> customRoots,
    Func<IReadOnlyList<string>, IUsageProvider> factory) : IUsageProvider
{
    private readonly object _sync = new();
    private string? _signature;
    private IUsageProvider? _provider;

    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool ReportsCost { get; } = reportsCost;

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
        Current().FetchDailyAsync(cancellationToken);

    public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
        Current().FetchEnrichmentAsync(cancellationToken);

    private IUsageProvider Current()
    {
        var roots = defaultRoots().Concat(customRoots())
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var signature = string.Join('\n', roots);
        lock (_sync)
        {
            if (_provider is null || !string.Equals(_signature, signature, StringComparison.Ordinal))
            {
                _provider = factory(roots);
                _signature = signature;
            }

            return _provider;
        }
    }
}
