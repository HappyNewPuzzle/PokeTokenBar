using System.Collections.ObjectModel;
using System.Globalization;

namespace PokeTokenBar.Windows.Core;

public sealed class UsageStore
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly IReadOnlyList<(string Id, string DisplayName)> _registeredProviders;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateLock = new();

    private IReadOnlyList<ProviderSnapshot> _snapshots = Array.Empty<ProviderSnapshot>();
    private DateTimeOffset? _lastUpdated;
    private string? _lastErrorDescription;
    private bool _isRefreshing;
    private bool _refreshPending;

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var providerArray = providers.ToArray();
        if (providerArray.Any(static provider => provider is null))
        {
            throw new ArgumentException("Providers cannot contain null values.", nameof(providers));
        }

        _providers = Array.AsReadOnly(providerArray);
        _registeredProviders = Array.AsReadOnly(
            providerArray
                .Select(static provider => (provider.Id, provider.DisplayName))
                .ToArray());
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<ProviderSnapshot> Snapshots
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshots;
            }
        }
    }

    public IReadOnlyList<(string Id, string DisplayName)> RegisteredProviders =>
        _registeredProviders;

    public IReadOnlyList<string> RegisteredProviderIds =>
        new ReadOnlyCollection<string>(_registeredProviders.Select(static provider => provider.Id).ToArray());

    public DateTimeOffset? LastUpdated
    {
        get
        {
            lock (_stateLock)
            {
                return _lastUpdated;
            }
        }
    }

    public bool IsRefreshing
    {
        get
        {
            lock (_stateLock)
            {
                return _isRefreshing;
            }
        }
    }

    public string? LastErrorDescription
    {
        get
        {
            lock (_stateLock)
            {
                return _lastErrorDescription;
            }
        }
    }

    public long TodayTotalTokens
    {
        get
        {
            var todayKey = TodayKey();
            return Snapshots.Sum(snapshot =>
                snapshot.Today?.Date == todayKey ? snapshot.TodayTotalTokens : 0);
        }
    }

    public IReadOnlyDictionary<string, long> TodayTokensByProvider
    {
        get
        {
            var todayKey = TodayKey();
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var snapshot in Snapshots)
            {
                if (snapshot.Today?.Date == todayKey)
                {
                    result[snapshot.ProviderId] = snapshot.Today.TotalTokens;
                }
            }

            return new ReadOnlyDictionary<string, long>(result);
        }
    }

    public bool HasUsageData => Snapshots.Count > 0;

    public IReadOnlyList<ProviderSnapshot> CostingSnapshots =>
        Array.AsReadOnly(Snapshots.Where(static snapshot => snapshot.ReportsCost).ToArray());

    public bool ShowsCost => CostingSnapshots.Count > 0;

    public double TodayCostTotal
    {
        get
        {
            var todayKey = TodayKey();
            return CostingSnapshots.Sum(snapshot =>
                snapshot.Today?.Date == todayKey ? snapshot.Today.TotalCost : 0);
        }
    }

    public long WeekTotalTokens =>
        Snapshots.Sum(static snapshot => snapshot.WeekTotal?.TotalTokens ?? 0);

    public double WeekCostTotal =>
        CostingSnapshots.Sum(static snapshot => snapshot.WeekTotal?.TotalCost ?? 0);

    public long MonthTotalTokens =>
        Snapshots.Sum(static snapshot => snapshot.MonthTotal?.TotalTokens ?? 0);

    public double MonthCostTotal =>
        CostingSnapshots.Sum(static snapshot => snapshot.MonthTotal?.TotalCost ?? 0);

    public ProviderSnapshot? Snapshot(string? preferredProviderId = null)
    {
        var snapshots = Snapshots;
        if (preferredProviderId is not null)
        {
            var preferred = snapshots.FirstOrDefault(snapshot =>
                snapshot.ProviderId == preferredProviderId);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return snapshots.FirstOrDefault();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isRefreshing)
            {
                _refreshPending = true;
                return;
            }

            _isRefreshing = true;
        }

        var runPendingRefresh = false;
        try
        {
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                _isRefreshing = false;
                if (_refreshPending)
                {
                    _refreshPending = false;
                    runPendingRefresh = true;
                }
            }

            if (runPendingRefresh)
            {
                _ = RefreshAsync();
            }
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var todayKey = TodayKey();
        var previousSnapshots = Snapshots;

        var completionSequence = 0;
        var dailyTasks = _providers
            .Select(provider => FetchDailyOutcomeAsync(
                provider,
                () => Interlocked.Increment(ref completionSequence),
                cancellationToken))
            .ToArray();
        var dailyOutcomes = (await Task.WhenAll(dailyTasks).ConfigureAwait(false))
            .OrderBy(static outcome => outcome.CompletionSequence)
            .ToArray();

        var dailyById = new Dictionary<string, DailyUsage>(StringComparer.Ordinal);
        var failedIds = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var outcome in dailyOutcomes)
        {
            if (outcome.Today is not null)
            {
                dailyById[outcome.Id] = outcome.Today;
            }

            if (outcome.ErrorDescription is not null)
            {
                failedIds.Add(outcome.Id);
                errors.Add($"{outcome.Id}: {outcome.ErrorDescription}");
            }
        }

        var newSnapshots = new List<ProviderSnapshot>();
        foreach (var provider in _providers)
        {
            var previous = previousSnapshots.FirstOrDefault(snapshot =>
                snapshot.ProviderId == provider.Id);
            var previousToday = previous?.Today?.Date == todayKey
                ? previous.Today
                : null;

            DailyUsage? today;
            if (dailyById.TryGetValue(provider.Id, out var fetched))
            {
                today = fetched;
            }
            else if (failedIds.Contains(provider.Id))
            {
                today = previousToday;
            }
            else
            {
                today = null;
            }

            if (today is not null)
            {
                newSnapshots.Add(new ProviderSnapshot(
                    provider.Id,
                    provider.DisplayName,
                    today,
                    previous?.ActiveBlock,
                    previous?.WeekTotal,
                    previous?.MonthTotal,
                    Now(),
                    provider.ReportsCost));
            }
        }

        CommitDailyPhase(newSnapshots, errors);
        cancellationToken.ThrowIfCancellationRequested();

        completionSequence = 0;
        var enrichmentTasks = _providers
            .Select(provider => FetchEnrichmentOutcomeAsync(
                provider,
                () => Interlocked.Increment(ref completionSequence),
                cancellationToken))
            .ToArray();
        var enrichmentOutcomes = (await Task.WhenAll(enrichmentTasks).ConfigureAwait(false))
            .OrderBy(static outcome => outcome.CompletionSequence)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        CommitEnrichmentPhase(enrichmentOutcomes);
    }

    private void CommitDailyPhase(
        IReadOnlyList<ProviderSnapshot> snapshots,
        IReadOnlyCollection<string> errors)
    {
        lock (_stateLock)
        {
            _snapshots = Array.AsReadOnly(snapshots.ToArray());
            if (errors.Count == 0)
            {
                _lastUpdated = Now();
                _lastErrorDescription = null;
            }
            else
            {
                _lastErrorDescription = string.Join(" / ", errors);
                if (_lastUpdated is null && _snapshots.Count > 0)
                {
                    _lastUpdated = Now();
                }
            }
        }
    }

    private void CommitEnrichmentPhase(
        IReadOnlyList<EnrichmentOutcome> outcomes)
    {
        lock (_stateLock)
        {
            var snapshots = _snapshots.ToList();
            foreach (var outcome in outcomes)
            {
                var index = snapshots.FindIndex(snapshot => snapshot.ProviderId == outcome.Id);
                if (index < 0)
                {
                    var hasActiveBlock =
                        outcome.Enrichment.BlocksOK &&
                        outcome.Enrichment.ActiveBlock is not null;
                    if (hasActiveBlock)
                    {
                        var provider = _providers.FirstOrDefault(candidate =>
                            candidate.Id == outcome.Id);
                        if (provider is not null)
                        {
                            snapshots.Add(new ProviderSnapshot(
                                outcome.Id,
                                provider.DisplayName,
                                Today: null,
                                outcome.Enrichment.ActiveBlock,
                                outcome.Enrichment.PeriodsOK
                                    ? outcome.Enrichment.WeekTotal
                                    : null,
                                outcome.Enrichment.PeriodsOK
                                    ? outcome.Enrichment.MonthTotal
                                    : null,
                                Now(),
                                provider.ReportsCost));
                        }
                    }

                    continue;
                }

                var snapshot = snapshots[index];
                if (outcome.Enrichment.BlocksOK)
                {
                    snapshot = snapshot with
                    {
                        ActiveBlock = outcome.Enrichment.ActiveBlock,
                    };
                }

                if (outcome.Enrichment.PeriodsOK)
                {
                    snapshot = snapshot with
                    {
                        WeekTotal = outcome.Enrichment.WeekTotal,
                        MonthTotal = outcome.Enrichment.MonthTotal,
                    };
                }

                snapshots[index] = snapshot;
            }

            _snapshots = Array.AsReadOnly(snapshots.ToArray());
        }
    }

    private static async Task<DailyOutcome> FetchDailyOutcomeAsync(
        IUsageProvider provider,
        Func<int> getCompletionSequence,
        CancellationToken cancellationToken)
    {
        try
        {
            var today = await provider.FetchDailyAsync(cancellationToken).ConfigureAwait(false);
            return new DailyOutcome(
                provider.Id,
                today,
                ErrorDescription: null,
                getCompletionSequence());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DailyOutcome(
                provider.Id,
                Today: null,
                exception.Message,
                getCompletionSequence());
        }
    }

    private static async Task<EnrichmentOutcome> FetchEnrichmentOutcomeAsync(
        IUsageProvider provider,
        Func<int> getCompletionSequence,
        CancellationToken cancellationToken)
    {
        try
        {
            var enrichment = await provider
                .FetchEnrichmentAsync(cancellationToken)
                .ConfigureAwait(false);
            return new EnrichmentOutcome(
                provider.Id,
                enrichment,
                getCompletionSequence());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnrichmentOutcome(
                provider.Id,
                new ProviderEnrichment(),
                getCompletionSequence());
        }
    }

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private string TodayKey() =>
        _timeProvider.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record DailyOutcome(
        string Id,
        DailyUsage? Today,
        string? ErrorDescription,
        int CompletionSequence);

    private sealed record EnrichmentOutcome(
        string Id,
        ProviderEnrichment Enrichment,
        int CompletionSequence);
}
