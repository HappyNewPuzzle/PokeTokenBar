using System.Globalization;
using System.Reflection;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class UsageStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Now);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-UsageStoreTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyProviderList_CompletesWithEmptySuccessfulState()
    {
        var store = Store();

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
        Assert.Equal(Now, store.LastUpdated);
        Assert.Null(store.LastErrorDescription);
        Assert.False(store.IsRefreshing);
    }

    [Fact]
    public async Task SingleProviderDailySuccess_CreatesSnapshot()
    {
        var provider = Provider("one", daily: Daily(10));
        var store = Store(provider);

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(Daily(10), snapshot.Today);
        Assert.Equal(10, store.TodayTotalTokens);
    }

    [Fact]
    public async Task MultipleProviderDailySuccess_AggregatesAllProviders()
    {
        var store = Store(
            Provider("one", daily: Daily(10)),
            Provider("two", daily: Daily(20)));

        await store.RefreshAsync();

        Assert.Equal(2, store.Snapshots.Count);
        Assert.Equal(30, store.TodayTotalTokens);
        Assert.Equal(10, store.TodayTokensByProvider["one"]);
        Assert.Equal(20, store.TodayTokensByProvider["two"]);
    }

    [Fact]
    public async Task DailyCalls_RunConcurrently()
    {
        var bothStarted = new CountdownEvent(2);
        var release = NewSignal();
        Task<DailyUsage?> DailyHandler(CancellationToken _)
        {
            bothStarted.Signal();
            return WaitForDailyAsync(release.Task);
        }

        var store = Store(
            Provider("one", dailyHandler: DailyHandler),
            Provider("two", dailyHandler: DailyHandler));

        var refresh = store.RefreshAsync();
        Assert.True(bothStarted.Wait(TimeSpan.FromSeconds(3)));
        release.SetResult();
        await refresh;
    }

    [Fact]
    public async Task OneDailyFailure_DoesNotDiscardOtherProviderSuccess()
    {
        var store = Store(
            Provider("good", daily: Daily(10)),
            Provider("bad", dailyError: new TestException("boom")));

        await store.RefreshAsync();

        Assert.Equal("good", Assert.Single(store.Snapshots).ProviderId);
        Assert.Contains("bad: boom", store.LastErrorDescription);
        Assert.Equal(Now, store.LastUpdated);
    }

    [Fact]
    public async Task FailedProvider_PreservesPreviousSameDayDailyAndEnrichment()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.DailyError = new TestException("boom");
        provider.Enrichment = new ProviderEnrichment();

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(10, snapshot.TodayTotalTokens);
        Assert.Equal(1, snapshot.ActiveBlock?.TotalTokens);
        Assert.Equal(2, snapshot.WeekTotal?.TotalTokens);
        Assert.Equal(3, snapshot.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task FailedProvider_DoesNotPreservePreviousDailyFromAnotherDate()
    {
        var provider = Provider("one", daily: Daily(10, "2026-08-28"));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.DailyError = new TestException("boom");

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public async Task NewFailingProviderWithoutPreviousValue_HasNoSnapshot()
    {
        var store = Store(Provider("one", dailyError: new TestException("boom")));

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
        Assert.Null(store.LastUpdated);
        Assert.NotNull(store.LastErrorDescription);
    }

    [Fact]
    public async Task SuccessfulNullDaily_RemovesPreviousSnapshot()
    {
        var provider = Provider("one", daily: Daily(10));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Daily = null;

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
        Assert.Null(store.LastErrorDescription);
        Assert.Equal(Now, store.LastUpdated);
    }

    [Fact]
    public async Task AllDailyFailuresWithoutHistory_ExposeErrorsAndNoLastUpdated()
    {
        var store = Store(
            Provider("one", dailyError: new TestException("first")),
            Provider("two", dailyError: new TestException("second")));

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
        Assert.Null(store.LastUpdated);
        Assert.Contains("one: first", store.LastErrorDescription);
        Assert.Contains("two: second", store.LastErrorDescription);
    }

    [Fact]
    public async Task AllDailyFailuresWithHistory_PreserveHistoryAndLastUpdated()
    {
        var one = Provider("one", daily: Daily(10));
        var two = Provider("two", daily: Daily(20));
        var store = Store(one, two);
        await store.RefreshAsync();
        one.DailyError = new TestException("first");
        two.DailyError = new TestException("second");

        await store.RefreshAsync();

        Assert.Equal(2, store.Snapshots.Count);
        Assert.Equal(Now, store.LastUpdated);
        Assert.NotNull(store.LastErrorDescription);
    }

    [Fact]
    public async Task DailyPhaseIsCommittedBeforeEnrichmentCompletes()
    {
        var enrichmentStarted = NewSignal();
        var release = NewSignal();
        var provider = Provider(
            "one",
            daily: Daily(10),
            enrichmentHandler: async _ =>
            {
                enrichmentStarted.TrySetResult();
                await release.Task;
                return new ProviderEnrichment();
            });
        var store = Store(provider);

        var refresh = store.RefreshAsync();
        await enrichmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(10, Assert.Single(store.Snapshots).TodayTotalTokens);
        Assert.Equal(Now, store.LastUpdated);
        Assert.True(store.IsRefreshing);
        release.SetResult();
        await refresh;
    }

    [Fact]
    public async Task EnrichmentDoesNotStartUntilAllDailyCallsFinish()
    {
        var slowDailyStarted = NewSignal();
        var release = NewSignal();
        var enrichmentCalls = 0;
        var slow = Provider(
            "slow",
            dailyHandler: async _ =>
            {
                slowDailyStarted.TrySetResult();
                await release.Task;
                return Daily(10);
            },
            enrichmentHandler: _ =>
            {
                Interlocked.Increment(ref enrichmentCalls);
                return Task.FromResult(new ProviderEnrichment());
            });
        var fast = Provider(
            "fast",
            daily: Daily(20),
            enrichmentHandler: _ =>
            {
                Interlocked.Increment(ref enrichmentCalls);
                return Task.FromResult(new ProviderEnrichment());
            });
        var store = Store(slow, fast);

        var refresh = store.RefreshAsync();
        await slowDailyStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, Volatile.Read(ref enrichmentCalls));
        release.SetResult();
        await refresh;
        Assert.Equal(2, enrichmentCalls);
    }

    [Fact]
    public async Task EnrichmentCalls_RunConcurrently()
    {
        var bothStarted = new CountdownEvent(2);
        var release = NewSignal();
        async Task<ProviderEnrichment> EnrichmentHandler(CancellationToken _)
        {
            bothStarted.Signal();
            await release.Task;
            return new ProviderEnrichment();
        }

        var store = Store(
            Provider("one", daily: Daily(1), enrichmentHandler: EnrichmentHandler),
            Provider("two", daily: Daily(2), enrichmentHandler: EnrichmentHandler));

        var refresh = store.RefreshAsync();
        Assert.True(bothStarted.Wait(TimeSpan.FromSeconds(3)));
        release.SetResult();
        await refresh;
    }

    [Fact]
    public async Task BlocksOkTrue_ReplacesActiveBlockIncludingWithNull()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Enrichment = new ProviderEnrichment(ActiveBlock: null, BlocksOK: true);

        await store.RefreshAsync();

        Assert.Null(Assert.Single(store.Snapshots).ActiveBlock);
    }

    [Fact]
    public async Task BlocksOkFalse_PreservesPreviousActiveBlock()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Enrichment = new ProviderEnrichment(
            ActiveBlock: Block(99),
            BlocksOK: false);

        await store.RefreshAsync();

        Assert.Equal(1, Assert.Single(store.Snapshots).ActiveBlock?.TotalTokens);
    }

    [Fact]
    public async Task PeriodsOkTrue_ReplacesWeekAndMonthIncludingWithNull()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Enrichment = new ProviderEnrichment(
            WeekTotal: null,
            MonthTotal: null,
            PeriodsOK: true);

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Null(snapshot.WeekTotal);
        Assert.Null(snapshot.MonthTotal);
    }

    [Fact]
    public async Task PeriodsOkFalse_PreservesPreviousWeekAndMonth()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Enrichment = new ProviderEnrichment(
            WeekTotal: Period(20),
            MonthTotal: Period(30),
            PeriodsOK: false);

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(2, snapshot.WeekTotal?.TotalTokens);
        Assert.Equal(3, snapshot.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task EnrichmentFailure_IsBestEffortAndPreservesPreviousValues()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.EnrichmentError = new TestException("ignored");

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(1, snapshot.ActiveBlock?.TotalTokens);
        Assert.Equal(2, snapshot.WeekTotal?.TotalTokens);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task OneEnrichmentFailure_DoesNotDiscardAnotherSuccess()
    {
        var good = Provider("good", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var bad = Provider("bad", daily: Daily(20), enrichmentError: new TestException("ignored"));
        var store = Store(good, bad);

        await store.RefreshAsync();

        Assert.Equal(1, store.Snapshot("good")?.ActiveBlock?.TotalTokens);
        Assert.Null(store.Snapshot("bad")?.ActiveBlock);
    }

    [Fact]
    public async Task ActiveBlockCreatesCarrierSnapshotWhenDailyIsNull()
    {
        var provider = Provider("one", daily: null, enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Null(snapshot.Today);
        Assert.Equal(1, snapshot.ActiveBlock?.TotalTokens);
        Assert.Equal(2, snapshot.WeekTotal?.TotalTokens);
    }

    [Fact]
    public async Task MonthWithoutDailyOrActiveBlock_CreatesCarrierSnapshot()
    {
        var enrichment = new ProviderEnrichment(
            ActiveBlock: null,
            BlocksOK: true,
            WeekTotal: Period(0),
            MonthTotal: Period(3),
            PeriodsOK: true);
        var store = Store(Provider("one", daily: null, enrichment: enrichment));

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Null(snapshot.Today);
        Assert.Null(snapshot.ActiveBlock);
        Assert.Equal(0, snapshot.WeekTotal?.TotalTokens);
        Assert.Equal(3, snapshot.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task ZeroPeriodsWithoutDailyOrActiveBlock_RemainsEmpty()
    {
        var enrichment = new ProviderEnrichment(
            ActiveBlock: null,
            BlocksOK: true,
            WeekTotal: Period(0),
            MonthTotal: Period(0),
            PeriodsOK: true);
        var store = Store(Provider("one", daily: null, enrichment: enrichment));

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public async Task SnapshotContainsExactProviderMetadataAndFetchedAt()
    {
        var provider = Provider("provider-id", "Provider Name", Daily(10), reportsCost: false);
        var store = Store(provider);

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal("provider-id", snapshot.ProviderId);
        Assert.Equal("provider-id", snapshot.Id);
        Assert.Equal("Provider Name", snapshot.DisplayName);
        Assert.False(snapshot.ReportsCost);
        Assert.Equal(Now, snapshot.FetchedAt);
    }

    [Fact]
    public async Task ReportsCostControlsCostAggregatesOnly()
    {
        var paid = Provider("paid", daily: Daily(10, cost: 4), reportsCost: true);
        var flat = Provider("flat", daily: Daily(20, cost: 99), reportsCost: false);
        paid.Enrichment = Enrichment(1, 2, 3, weekCost: 5, monthCost: 6);
        flat.Enrichment = Enrichment(1, 20, 30, weekCost: 50, monthCost: 60);
        var store = Store(paid, flat);

        await store.RefreshAsync();

        Assert.Equal(30, store.TodayTotalTokens);
        Assert.Equal(4, store.TodayCostTotal);
        Assert.Equal(22, store.WeekTotalTokens);
        Assert.Equal(5, store.WeekCostTotal);
        Assert.Equal(33, store.MonthTotalTokens);
        Assert.Equal(6, store.MonthCostTotal);
    }

    [Fact]
    public async Task SnapshotsFollowProviderRegistrationOrder()
    {
        var store = Store(
            Provider("third", daily: Daily(3)),
            Provider("first", daily: Daily(1)),
            Provider("second", daily: Daily(2)));

        await store.RefreshAsync();

        Assert.Equal(["third", "first", "second"], store.Snapshots.Select(x => x.ProviderId));
        Assert.Equal(["third", "first", "second"], store.RegisteredProviderIds);
    }

    [Fact]
    public async Task DuplicateProviderIds_FollowSwiftIdKeyedAndFirstSnapshotMergeContract()
    {
        var first = Provider("same", "First", Daily(10), enrichment: Enrichment(1, 2, 3));
        var second = Provider("same", "Second", Daily(20), enrichment: new ProviderEnrichment());
        var store = Store(first, second);

        await store.RefreshAsync();

        Assert.Equal(2, store.Snapshots.Count);
        Assert.All(store.Snapshots, snapshot => Assert.Equal(20, snapshot.TodayTotalTokens));
        Assert.Equal("First", store.Snapshots[0].DisplayName);
        Assert.Equal("Second", store.Snapshots[1].DisplayName);
        Assert.Equal(1, store.Snapshots[0].ActiveBlock?.TotalTokens);
        Assert.Null(store.Snapshots[1].ActiveBlock);
    }

    [Fact]
    public async Task CancellationDuringDaily_DoesNotCommitOrRecordProviderError()
    {
        var started = NewSignal();
        var provider = Provider(
            "one",
            dailyHandler: async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Daily(10);
            });
        var store = Store(provider);
        using var cancellation = new CancellationTokenSource();

        var refresh = store.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Empty(store.Snapshots);
        Assert.Null(store.LastUpdated);
        Assert.Null(store.LastErrorDescription);
        Assert.False(store.IsRefreshing);
    }

    [Fact]
    public async Task CancellationBetweenPhases_KeepsCommittedDailyAndSkipsEnrichment()
    {
        using var cancellation = new CancellationTokenSource();
        var enrichmentCalls = 0;
        var provider = Provider(
            "one",
            dailyHandler: _ =>
            {
                cancellation.Cancel();
                return Task.FromResult<DailyUsage?>(Daily(10));
            },
            enrichmentHandler: _ =>
            {
                Interlocked.Increment(ref enrichmentCalls);
                return Task.FromResult(Enrichment(1, 2, 3));
            });
        var store = Store(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RefreshAsync(cancellation.Token));

        Assert.Equal(10, Assert.Single(store.Snapshots).TodayTotalTokens);
        Assert.Equal(Now, store.LastUpdated);
        Assert.Equal(0, enrichmentCalls);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task CancellationDuringEnrichment_KeepsDailyCommitAndPriorEnrichment()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(1, 2, 3));
        var store = Store(provider);
        await store.RefreshAsync();

        var started = NewSignal();
        provider.Daily = Daily(20);
        provider.EnrichmentHandler = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ProviderEnrichment();
        };
        using var cancellation = new CancellationTokenSource();

        var refresh = store.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(20, snapshot.TodayTotalTokens);
        Assert.Equal(1, snapshot.ActiveBlock?.TotalTokens);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task ProviderThrownCancellation_IsNotRecordedAsOrdinaryError()
    {
        var provider = Provider(
            "one",
            dailyHandler: _ => Task.FromCanceled<DailyUsage?>(new CancellationToken(true)));
        var store = Store(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RefreshAsync());

        Assert.Null(store.LastErrorDescription);
        Assert.Null(store.LastUpdated);
    }

    [Fact]
    public async Task RepeatedRefresh_ReplacesDailyAndKeepsSnapshotIdentityByProviderId()
    {
        var provider = Provider("one", daily: Daily(10));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.Daily = Daily(20);

        await store.RefreshAsync();

        Assert.Equal(20, Assert.Single(store.Snapshots).TodayTotalTokens);
    }

    [Fact]
    public async Task DailyFailureThenSuccess_ClearsErrorAndUsesNewValue()
    {
        var provider = Provider("one", dailyError: new TestException("boom"));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.DailyError = null;
        provider.Daily = Daily(20);

        await store.RefreshAsync();

        Assert.Equal(20, Assert.Single(store.Snapshots).TodayTotalTokens);
        Assert.Null(store.LastErrorDescription);
        Assert.Equal(Now, store.LastUpdated);
    }

    [Fact]
    public async Task EnrichmentFailureThenSuccess_ReplacesEnrichment()
    {
        var provider = Provider("one", daily: Daily(10), enrichmentError: new TestException("boom"));
        var store = Store(provider);
        await store.RefreshAsync();
        provider.EnrichmentError = null;
        provider.Enrichment = Enrichment(1, 2, 3);

        await store.RefreshAsync();

        Assert.Equal(1, Assert.Single(store.Snapshots).ActiveBlock?.TotalTokens);
    }

    [Fact]
    public async Task ConcurrentRefresh_IsCoalescedIntoOneFollowUp()
    {
        var firstStarted = NewSignal();
        var release = NewSignal();
        var provider = Provider(
            "one",
            dailyHandler: async _ =>
            {
                var call = Interlocked.Increment(ref _coalescingDailyCalls);
                if (call == 1)
                {
                    firstStarted.TrySetResult();
                    await release.Task;
                }

                return Daily(call);
            });
        _coalescingDailyCalls = 0;
        var store = Store(provider);

        var first = store.RefreshAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await store.RefreshAsync();
        await store.RefreshAsync();
        release.SetResult();
        await first;

        await WaitUntilAsync(() => Volatile.Read(ref _coalescingDailyCalls) >= 2);
        Assert.Equal(2, _coalescingDailyCalls);
        await WaitUntilAsync(() => !store.IsRefreshing);
    }

    [Fact]
    public async Task SnapshotPreference_MatchesSwiftPreferredThenFirstFallback()
    {
        var store = Store(
            Provider("one", daily: Daily(1)),
            Provider("two", daily: Daily(2)));
        await store.RefreshAsync();

        Assert.Equal("two", store.Snapshot("two")?.ProviderId);
        Assert.Equal("one", store.Snapshot("missing")?.ProviderId);
        Assert.Equal("one", store.Snapshot()?.ProviderId);
    }

    [Fact]
    public async Task LocalCodexProvider_TemporaryRootFlowsThroughStore()
    {
        var root = Path.Combine(_temporaryDirectory, "codex");
        var actualNow = DateTimeOffset.Now;
        WriteRollout(root, "one.jsonl", actualNow, "local-codex", 80, 10, 40);
        var store = new UsageStore(
            [new LocalCodexUsageProvider([root])],
            new FixedTimeProvider(actualNow));

        await store.RefreshAsync();

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Equal("codex", snapshot.ProviderId);
        Assert.Equal(130, snapshot.TodayTotalTokens);
        Assert.Equal(80, snapshot.Today?.InputTokens);
        Assert.Equal(40, snapshot.Today?.CacheReadTokens);
    }

    [Fact]
    public async Task LocalCodexAndFakeProvider_AggregateWithoutStoreSpecialCases()
    {
        var root = Path.Combine(_temporaryDirectory, "codex-multi");
        var actualNow = DateTimeOffset.Now;
        WriteRollout(root, "one.jsonl", actualNow, "local-codex", 80, 10, 40);
        var store = new UsageStore(
            [new LocalCodexUsageProvider([root]), Provider("fake", daily: CurrentDaily(70))],
            new FixedTimeProvider(actualNow));

        await store.RefreshAsync();

        Assert.Equal(200, store.TodayTotalTokens);
        Assert.Equal(["codex", "fake"], store.Snapshots.Select(x => x.ProviderId));
    }

    [Fact]
    public void UsageStoreCoreAssembly_DoesNotReferenceInfrastructureOrWpf()
    {
        var references = typeof(UsageStore).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();

        Assert.DoesNotContain("PokeTokenBar.Windows.Infrastructure", references);
        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain(
            typeof(UsageStore).GetInterfaces(),
            type => type.Name == "INotifyPropertyChanged");
    }

    [Fact]
    public void UsageStorePublicApi_KeepsOfficialLimitsInCoreAndAvoidsInfrastructureTypes()
    {
        var exposedTypes = typeof(UsageStore)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(MemberTypes)
            .Where(type => type is not null)
            .Cast<Type>();

        Assert.DoesNotContain(exposedTypes, type =>
            type.Namespace?.Contains("Infrastructure", StringComparison.Ordinal) == true);
        Assert.Contains(typeof(CodexRateLimitStatus), exposedTypes);
    }

    private int _coalescingDailyCalls;

    private static IEnumerable<Type?> MemberTypes(MemberInfo member) => member switch
    {
        PropertyInfo property => [property.PropertyType],
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(x => x.ParameterType)],
        ConstructorInfo constructor => constructor.GetParameters().Select(x => x.ParameterType),
        _ => [],
    };

    private static UsageStore Store(params TestUsageProvider[] providers) =>
        new(providers, Clock);

    private static TestUsageProvider Provider(
        string id,
        string? displayName = null,
        DailyUsage? daily = null,
        bool reportsCost = true,
        ProviderEnrichment? enrichment = null,
        Exception? dailyError = null,
        Exception? enrichmentError = null,
        Func<CancellationToken, Task<DailyUsage?>>? dailyHandler = null,
        Func<CancellationToken, Task<ProviderEnrichment>>? enrichmentHandler = null) =>
        new(id, displayName ?? id, reportsCost)
        {
            Daily = daily,
            Enrichment = enrichment ?? new ProviderEnrichment(),
            DailyError = dailyError,
            EnrichmentError = enrichmentError,
            DailyHandler = dailyHandler,
            EnrichmentHandler = enrichmentHandler,
        };

    private static DailyUsage Daily(
        long total,
        string date = "2026-08-29",
        double cost = 0) =>
        new(date, total, 0, 0, 0, total, cost);

    private static DailyUsage CurrentDaily(long total) =>
        new(
            DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            total,
            0,
            0,
            0,
            total,
            0);

    private static ProviderEnrichment Enrichment(
        long block,
        long week,
        long month,
        double weekCost = 0,
        double monthCost = 0) =>
        new(
            Block(block),
            BlocksOK: true,
            Period(week, weekCost),
            Period(month, monthCost),
            PeriodsOK: true);

    private static BlockUsage Block(long tokens) =>
        new("block", "start", "end", true, tokens, 0, 1);

    private static PeriodUsage Period(long tokens, double cost = 0) =>
        new("period", tokens, cost);

    private static async Task<DailyUsage?> WaitForDailyAsync(Task release)
    {
        await release;
        return Daily(1);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("The expected asynchronous state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private static void WriteRollout(
        string root,
        string relativePath,
        DateTimeOffset now,
        string sessionId,
        long input,
        long output,
        long cacheRead)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var timestamp = now.AddMinutes(-5).ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
        var meta = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + sessionId
            + "\",\"thread_source\":\"user\"}}";
        var lastInput = input + cacheRead;
        var total = input + output + cacheRead;
        var state = "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
            + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
            + "\"total_token_usage\":{"
            + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{cacheRead},"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{output},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{total}}},"
            + "\"last_token_usage\":{"
            + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{cacheRead},"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{output},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{total}}}}}}}}}";
        File.WriteAllText(path, string.Join(Environment.NewLine, meta, state));
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
    }

    private sealed class TestUsageProvider(
        string id,
        string displayName,
        bool reportsCost) : IUsageProvider
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public bool ReportsCost { get; } = reportsCost;

        public DailyUsage? Daily { get; set; }

        public ProviderEnrichment Enrichment { get; set; } = new();

        public Exception? DailyError { get; set; }

        public Exception? EnrichmentError { get; set; }

        public Func<CancellationToken, Task<DailyUsage?>>? DailyHandler { get; set; }

        public Func<CancellationToken, Task<ProviderEnrichment>>? EnrichmentHandler { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default)
        {
            if (DailyHandler is not null)
            {
                return DailyHandler(cancellationToken);
            }

            return DailyError is null
                ? Task.FromResult(Daily)
                : Task.FromException<DailyUsage?>(DailyError);
        }

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default)
        {
            if (EnrichmentHandler is not null)
            {
                return EnrichmentHandler(cancellationToken);
            }

            return EnrichmentError is null
                ? Task.FromResult(Enrichment)
                : Task.FromException<ProviderEnrichment>(EnrichmentError);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone(
                $"UsageStoreTests/{Guid.NewGuid():N}",
                now.Offset,
                "Test",
                "Test");
    }

    private sealed class TestException(string message) : Exception(message);
}
