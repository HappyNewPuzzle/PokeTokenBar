using System.Collections.ObjectModel;
using System.Globalization;

namespace PokeTokenBar.Windows.Core;

public sealed class UsageStore
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly IReadOnlyList<(string Id, string DisplayName)> _registeredProviders;
    private readonly TimeProvider _timeProvider;
    private readonly ICodexRateLimitsProvider? _codexRateLimitsProvider;
    private readonly IClaudeRateLimitsProvider? _claudeRateLimitsProvider;
    private readonly IAntigravityRateLimitsProvider? _antigravityRateLimitsProvider;
    private readonly IUsageSnapshotPersistence? _snapshotPersistence;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, CachedProviderUsage> _cachedProviders =
        new(StringComparer.Ordinal);

    private IReadOnlyList<ProviderSnapshot> _snapshots = Array.Empty<ProviderSnapshot>();
    private DateTimeOffset? _lastUpdated;
    private string? _lastErrorDescription;
    private bool _isRefreshing;
    private bool _refreshPending;
    private CodexRateLimitStatus? _codexRateLimits;
    private DateTimeOffset? _codexRateLimitsUpdatedAt;
    private ClaudeRateLimitStatus? _claudeRateLimits;
    private DateTimeOffset? _claudeRateLimitsUpdatedAt;
    private AntigravityRateLimitStatus? _antigravityRateLimits;
    private DateTimeOffset? _antigravityRateLimitsUpdatedAt;
    private IReadOnlyList<ProviderStatusSnapshot> _providerStatuses = Array.Empty<ProviderStatusSnapshot>();
    private UsageCacheLoadStatus _usageCacheStatus = UsageCacheLoadStatus.Missing;

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        TimeProvider? timeProvider = null,
        ICodexRateLimitsProvider? codexRateLimitsProvider = null,
        IClaudeRateLimitsProvider? claudeRateLimitsProvider = null,
        IAntigravityRateLimitsProvider? antigravityRateLimitsProvider = null,
        IUsageSnapshotPersistence? snapshotPersistence = null)
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
        _codexRateLimitsProvider = codexRateLimitsProvider;
        _claudeRateLimitsProvider = claudeRateLimitsProvider;
        _antigravityRateLimitsProvider = antigravityRateLimitsProvider;
        _snapshotPersistence = snapshotPersistence;
        RestoreCachedSnapshots();
    }

    public UsageCacheLoadStatus UsageCacheStatus
    {
        get
        {
            lock (_stateLock)
            {
                return _usageCacheStatus;
            }
        }
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

    public IReadOnlyList<ProviderStatusSnapshot> ProviderStatuses
    {
        get
        {
            lock (_stateLock)
            {
                return _providerStatuses;
            }
        }
    }

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

    public CodexRateLimitStatus? CodexRateLimits
    {
        get
        {
            lock (_stateLock)
            {
                return _codexRateLimits;
            }
        }
    }

    public DateTimeOffset? CodexRateLimitsUpdatedAt
    {
        get
        {
            lock (_stateLock)
            {
                return _codexRateLimitsUpdatedAt;
            }
        }
    }

    public bool CodexRateLimitsStale
    {
        get
        {
            lock (_stateLock)
            {
                return _codexRateLimits is not null &&
                    _codexRateLimitsUpdatedAt is DateTimeOffset updatedAt &&
                    Now() - updatedAt > TimeSpan.FromMinutes(15);
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

    public double? ClaudeBurnPerMinute =>
        Snapshots.FirstOrDefault(static snapshot => snapshot.ProviderId == "claude_code")
            ?.ActiveBlock?.TokensPerMinute;

    public sealed record FiveHourForecast(DateTimeOffset DepletionTime, bool BeforeReset);

    public FiveHourForecast? ClaudeFiveHourForecast
    {
        get
        {
            var window = ClaudeRateLimits?.FiveHour;
            if (window?.UsedPercent is not double utilization ||
                window.ResetsAt is not DateTimeOffset reset)
            {
                return null;
            }

            if (utilization >= 100)
            {
                return new FiveHourForecast(Now(), true);
            }

            var block = Snapshots.FirstOrDefault(static snapshot =>
                snapshot.ProviderId == "claude_code")?.ActiveBlock;
            var depletion = ForecastDepletion(
                block?.TotalTokens ?? 0,
                block?.TokensPerMinute ?? 0,
                utilization,
                Now());
            return depletion is DateTimeOffset value
                ? new FiveHourForecast(value, value < reset)
                : null;
        }
    }

    public static DateTimeOffset? ForecastDepletion(
        long blockTokens,
        double tokensPerMinute,
        double utilization,
        DateTimeOffset now)
    {
        if (!double.IsFinite(utilization) || !double.IsFinite(tokensPerMinute) ||
            utilization < 5 || utilization >= 100 || blockTokens <= 0 ||
            tokensPerMinute < 10_000)
        {
            return null;
        }

        var tokensPerPercent = blockTokens / utilization;
        var minutesLeft = (100 - utilization) * tokensPerPercent / tokensPerMinute;
        return double.IsFinite(minutesLeft) && minutesLeft < 60 * 24
            ? now.AddMinutes(minutesLeft)
            : null;
    }

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
                await RefreshAsync().ConfigureAwait(false);
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
            else if (failedIds.Contains(provider.Id) && previous is not null)
            {
                var preserved = previous with { Today = previousToday };
                if (HasLocalData(preserved)) newSnapshots.Add(preserved);
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
        CommitEnrichmentPhase(enrichmentOutcomes, previousSnapshots);
        var officialResults = await Task.WhenAll(
            RefreshCodexRateLimitsAsync(cancellationToken),
            RefreshClaudeRateLimitsAsync(cancellationToken),
            RefreshAntigravityRateLimitsAsync(cancellationToken)).ConfigureAwait(false);
        UpdateProviderStatuses(failedIds, officialResults);
        PersistSuccessfulSnapshots(dailyOutcomes, enrichmentOutcomes);
    }

    private async Task<bool> RefreshCodexRateLimitsAsync(CancellationToken cancellationToken)
    {
        if (_codexRateLimitsProvider is null)
        {
            return true;
        }

        var succeeded = true;
        try
        {
            var limits = await _codexRateLimitsProvider
                .FetchAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _codexRateLimits = limits;
                if (limits is not null)
                {
                    _codexRateLimitsUpdatedAt = Now();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Official limits are best effort. Preserve the previous successful value.
            succeeded = false;
        }

        EnsureProviderSnapshotForOfficialLimits(
            "codex",
            _codexRateLimits?.HasVisibleLimit == true);
        return succeeded;
    }

    private async Task<bool> RefreshClaudeRateLimitsAsync(CancellationToken cancellationToken)
    {
        if (_claudeRateLimitsProvider is null)
        {
            return true;
        }

        var succeeded = true;
        try
        {
            var limits = await _claudeRateLimitsProvider
                .FetchAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _claudeRateLimits = limits;
                if (limits is not null)
                {
                    _claudeRateLimitsUpdatedAt = Now();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Claude OAuth limits are best effort; local usage remains available.
            succeeded = false;
        }

        EnsureProviderSnapshotForOfficialLimits(
            "claude_code",
            _claudeRateLimits?.HasVisibleLimit == true);
        return succeeded;
    }

    private void EnsureProviderSnapshotForOfficialLimits(string providerId, bool hasVisibleLimit)
    {
        lock (_stateLock)
        {
            if (!hasVisibleLimit || _snapshots.Any(snapshot => snapshot.ProviderId == providerId))
            {
                return;
            }

            var provider = _providers.FirstOrDefault(provider => provider.Id == providerId);
            if (provider is not null)
            {
                _snapshots = Array.AsReadOnly(_snapshots.Append(new ProviderSnapshot(
                    provider.Id,
                    provider.DisplayName,
                    Today: null,
                    ActiveBlock: null,
                    WeekTotal: null,
                    MonthTotal: null,
                    Now(),
                    provider.ReportsCost)).ToArray());
            }
        }
    }

    private async Task<bool> RefreshAntigravityRateLimitsAsync(CancellationToken cancellationToken)
    {
        if (_antigravityRateLimitsProvider is null)
        {
            return true;
        }

        var succeeded = true;
        try
        {
            var limits = await _antigravityRateLimitsProvider
                .FetchAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _antigravityRateLimits = limits;
                if (limits is not null)
                {
                    _antigravityRateLimitsUpdatedAt = Now();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Antigravity quota is best effort; local usage remains available.
            succeeded = false;
        }

        EnsureProviderSnapshotForOfficialLimits(
            "antigravity",
            _antigravityRateLimits?.HasVisibleLimit == true);
        return succeeded;
    }

    private void UpdateProviderStatuses(
        IReadOnlySet<string> failedProviderIds,
        IReadOnlyList<bool> officialRefreshResults)
    {
        lock (_stateLock)
        {
            _providerStatuses = Array.AsReadOnly(_providers.Select(provider =>
            {
                var snapshot = _snapshots.FirstOrDefault(candidate => candidate.ProviderId == provider.Id);
                (bool? HasLimits, bool RefreshSucceeded) official = provider.Id switch
                {
                    "codex" when _codexRateLimitsProvider is not null =>
                        (_codexRateLimits is not null, officialRefreshResults[0]),
                    "claude_code" when _claudeRateLimitsProvider is not null =>
                        (_claudeRateLimits is not null, officialRefreshResults[1]),
                    "antigravity" when _antigravityRateLimitsProvider is not null =>
                        (_antigravityRateLimits is not null, officialRefreshResults[2]),
                    _ => ((bool?)null, true),
                };
                var auth = official.HasLimits switch
                {
                    true => ProviderAuthStatus.Authenticated,
                    false => ProviderAuthStatus.QuotaUnavailable,
                    null => ProviderAuthStatus.NotApplicable,
                };
                var runtime = failedProviderIds.Contains(provider.Id)
                    ? snapshot is null ? ProviderRuntimeStatus.Error : ProviderRuntimeStatus.Stale
                    : !official.RefreshSucceeded && official.HasLimits == true && snapshot is not null
                        ? ProviderRuntimeStatus.Stale
                        : snapshot is null
                            ? ProviderRuntimeStatus.NoSessions
                            : official.HasLimits == false
                                ? ProviderRuntimeStatus.LocalDataOnly
                                : ProviderRuntimeStatus.Ready;
                return new ProviderStatusSnapshot(provider.Id, provider.DisplayName, runtime, auth);
            }).ToArray());
        }
    }

    public AntigravityRateLimitStatus? AntigravityRateLimits
    {
        get
        {
            lock (_stateLock)
            {
                return _antigravityRateLimits;
            }
        }
    }

    public DateTimeOffset? AntigravityRateLimitsUpdatedAt
    {
        get
        {
            lock (_stateLock)
            {
                return _antigravityRateLimitsUpdatedAt;
            }
        }
    }

    public bool AntigravityRateLimitsStale
    {
        get
        {
            lock (_stateLock)
            {
                return _antigravityRateLimits is not null &&
                    _antigravityRateLimitsUpdatedAt is DateTimeOffset updatedAt &&
                    Now() - updatedAt > TimeSpan.FromMinutes(15);
            }
        }
    }

    public ClaudeRateLimitStatus? ClaudeRateLimits
    {
        get
        {
            lock (_stateLock)
            {
                return _claudeRateLimits;
            }
        }
    }

    public DateTimeOffset? ClaudeRateLimitsUpdatedAt
    {
        get
        {
            lock (_stateLock)
            {
                return _claudeRateLimitsUpdatedAt;
            }
        }
    }

    public bool ClaudeRateLimitsStale
    {
        get
        {
            lock (_stateLock)
            {
                return _claudeRateLimits is not null &&
                    _claudeRateLimitsUpdatedAt is DateTimeOffset updatedAt &&
                    Now() - updatedAt > TimeSpan.FromMinutes(15);
            }
        }
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
        IReadOnlyList<EnrichmentOutcome> outcomes,
        IReadOnlyList<ProviderSnapshot> previousSnapshots)
    {
        lock (_stateLock)
        {
            var snapshots = _snapshots.ToList();
            foreach (var outcome in outcomes)
            {
                var index = snapshots.FindIndex(snapshot => snapshot.ProviderId == outcome.Id);
                if (index < 0)
                {
                    if (!outcome.Succeeded)
                    {
                        var previous = previousSnapshots.FirstOrDefault(snapshot =>
                            snapshot.ProviderId == outcome.Id);
                        if (previous is not null)
                        {
                            var preserved = previous with
                            {
                                Today = previous.Today?.Date == TodayKey() ? previous.Today : null,
                            };
                            if (HasLocalData(preserved)) snapshots.Add(preserved);
                        }
                        continue;
                    }

                    var hasActiveBlock =
                        outcome.Enrichment.BlocksOK &&
                        outcome.Enrichment.ActiveBlock is not null;
                    var hasPeriodUsage =
                        outcome.Enrichment.PeriodsOK &&
                        ((outcome.Enrichment.WeekTotal?.TotalTokens ?? 0) > 0 ||
                         (outcome.Enrichment.MonthTotal?.TotalTokens ?? 0) > 0);
                    if (hasActiveBlock || hasPeriodUsage)
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

    private void RestoreCachedSnapshots()
    {
        if (_snapshotPersistence is null) return;

        UsageSnapshotCacheLoadResult loaded;
        try
        {
            loaded = _snapshotPersistence.Load();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            loaded = new(UsageCacheLoadStatus.Corrupt);
        }

        _usageCacheStatus = loaded.Status;
        if (loaded.Status != UsageCacheLoadStatus.Available || loaded.Cache is null) return;

        var snapshots = new List<ProviderSnapshot>();
        foreach (var cached in loaded.Cache.Providers)
        {
            var provider = _providers.FirstOrDefault(candidate => candidate.Id == cached.ProviderId);
            if (provider is null || _cachedProviders.ContainsKey(cached.ProviderId)) continue;

            var snapshot = SanitizeCachedSnapshot(cached, provider);
            if (!HasLocalData(snapshot)) continue;
            _cachedProviders.Add(cached.ProviderId, ToCached(snapshot));
            snapshots.Add(snapshot);
        }

        _snapshots = Array.AsReadOnly(snapshots.ToArray());
        if (_snapshots.Count > 0) _lastUpdated = loaded.Cache.SavedAt;
        _providerStatuses = Array.AsReadOnly(_providers.Select(provider =>
            new ProviderStatusSnapshot(
                provider.Id,
                provider.DisplayName,
                _snapshots.Any(snapshot => snapshot.ProviderId == provider.Id)
                    ? ProviderRuntimeStatus.Stale
                    : ProviderRuntimeStatus.NoSessions,
                ProviderAuthStatus.NotApplicable)).ToArray());
    }

    private void PersistSuccessfulSnapshots(
        IReadOnlyList<DailyOutcome> dailyOutcomes,
        IReadOnlyList<EnrichmentOutcome> enrichmentOutcomes)
    {
        if (_snapshotPersistence is null) return;

        var successfulIds = dailyOutcomes
            .Where(outcome => outcome.ErrorDescription is null)
            .Select(outcome => outcome.Id)
            .Intersect(
                enrichmentOutcomes.Where(outcome => outcome.Succeeded).Select(outcome => outcome.Id),
                StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (successfulIds.Count == 0) return;

        UsageSnapshotCache cache;
        lock (_stateLock)
        {
            foreach (var id in successfulIds)
            {
                var snapshot = _snapshots.FirstOrDefault(candidate =>
                    candidate.ProviderId == id && HasLocalData(candidate));
                if (snapshot is null) _cachedProviders.Remove(id);
                else _cachedProviders[id] = ToCached(snapshot);
            }

            cache = new UsageSnapshotCache(
                Now(),
                _registeredProviders
                    .Select(provider => _cachedProviders.GetValueOrDefault(provider.Id))
                    .Where(static provider => provider is not null)
                    .Cast<CachedProviderUsage>()
                    .ToArray());
        }

        try
        {
            _snapshotPersistence.Save(cache);
            lock (_stateLock) _usageCacheStatus = UsageCacheLoadStatus.Available;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Cache persistence is best effort; in-memory usage remains authoritative.
        }
    }

    private ProviderSnapshot SanitizeCachedSnapshot(
        CachedProviderUsage cached,
        IUsageProvider provider)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var todayValue = cached.Today?.Date == TodayKey() ? cached.Today : null;
        var activeBlock = cached.ActiveBlock is { IsActive: true } block &&
                          DateTimeOffset.TryParse(
                              block.EndTime,
                              CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal,
                              out var end) && end > Now()
            ? block
            : null;
        var week = cached.WeekTotal is { } weekValue &&
                   DateOnly.TryParseExact(
                       weekValue.Period,
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out var weekStart) &&
                   weekStart <= today && today <= weekStart.AddDays(6)
            ? weekValue
            : null;
        var month = cached.MonthTotal?.Period == today.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ? cached.MonthTotal
            : null;
        return new ProviderSnapshot(
            provider.Id,
            provider.DisplayName,
            todayValue,
            activeBlock,
            week,
            month,
            cached.FetchedAt,
            provider.ReportsCost);
    }

    private static CachedProviderUsage ToCached(ProviderSnapshot snapshot) => new(
        snapshot.ProviderId,
        snapshot.Today,
        snapshot.ActiveBlock,
        snapshot.WeekTotal,
        snapshot.MonthTotal,
        snapshot.FetchedAt);

    private static bool HasLocalData(ProviderSnapshot snapshot) =>
        snapshot.Today is not null ||
        snapshot.ActiveBlock is not null ||
        (snapshot.WeekTotal?.TotalTokens ?? 0) > 0 ||
        (snapshot.WeekTotal?.TotalCost ?? 0) > 0 ||
        (snapshot.MonthTotal?.TotalTokens ?? 0) > 0 ||
        (snapshot.MonthTotal?.TotalCost ?? 0) > 0;

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

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
                getCompletionSequence(),
                Succeeded: false);
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
        int CompletionSequence,
        bool Succeeded = true);
}
