using System.Globalization;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase7CUsageCacheTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"PokeTokenBar-Phase7C-{Guid.NewGuid():N}");

    [Fact]
    public void MissingCacheIsSafe()
    {
        var result = Json().Load();

        Assert.Equal(UsageCacheLoadStatus.Missing, result.Status);
        Assert.Null(result.Cache);
    }

    [Fact]
    public void CacheRoundTripsOnlyPresentationSnapshotFields()
    {
        var cache = Cache(Cached("codex", 10, 20, 30, 40, cost: 1.25));
        Json().Save(cache);

        var loaded = Json().Load();

        Assert.Equal(UsageCacheLoadStatus.Available, loaded.Status);
        Assert.Equal(cache.SavedAt, loaded.Cache!.SavedAt);
        Assert.Equal(cache.Providers.ToArray(), loaded.Cache.Providers.ToArray());
    }

    [Fact]
    public void SaveAtomicallyReplacesExistingCache()
    {
        var persistence = Json();
        persistence.Save(Cache(Cached("codex", 1, 2, 3, 4)));
        persistence.Save(Cache(Cached("codex", 5, 6, 7, 8)));

        Assert.Equal(5, Assert.Single(persistence.Load().Cache!.Providers).Today!.TotalTokens);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void MalformedOrPartialCacheIsIgnoredWithoutMutation(string json)
    {
        WriteRaw(json);

        var result = Json().Load();

        Assert.Equal(UsageCacheLoadStatus.Corrupt, result.Status);
        Assert.True(File.Exists(Json().FilePath));
    }

    [Fact]
    public void FutureFormatIsIgnored()
    {
        WriteRaw("{\"formatVersion\":2,\"savedAt\":\"2026-09-02T12:00:00Z\",\"providers\":[]}");

        Assert.Equal(UsageCacheLoadStatus.Unsupported, Json().Load().Status);
    }

    [Theory]
    [InlineData("inputTokens")]
    [InlineData("outputTokens")]
    [InlineData("cacheCreationTokens")]
    [InlineData("cacheReadTokens")]
    [InlineData("totalTokens")]
    public void NegativeTokenValuesInvalidateCache(string field)
    {
        var json = ValidJson().Replace($"\"{field}\":10", $"\"{field}\":-1");
        WriteRaw(json);

        Assert.Equal(UsageCacheLoadStatus.Corrupt, Json().Load().Status);
    }

    [Fact]
    public void InvalidTimestampIsIgnored()
    {
        WriteRaw(ValidJson().Replace("2026-09-02T11:30:00+00:00", "not-a-time"));

        Assert.Equal(UsageCacheLoadStatus.Corrupt, Json().Load().Status);
    }

    [Fact]
    public void DuplicateProviderRowsInvalidateCache()
    {
        var one = ValidJson();
        var provider = one[(one.IndexOf("[{", StringComparison.Ordinal) + 1)..^2];
        WriteRaw(one.Replace(provider, $"{provider},{provider}"));

        Assert.Equal(UsageCacheLoadStatus.Corrupt, Json().Load().Status);
    }

    [Fact]
    public void ExplicitPathDoesNotDependOnConfiguredProviderRoots()
    {
        var path = Path.Combine(_directory, "custom", "cache.json");
        var persistence = new JsonUsageSnapshotPersistence(path);
        persistence.Save(Cache(Cached("codex", 1, 0, 0, 1)));

        Assert.Equal(Path.GetFullPath(path), persistence.FilePath);
        Assert.Equal(UsageCacheLoadStatus.Available, persistence.Load().Status);
    }

    [Fact]
    public void CacheJsonContainsNoCredentialOrRawSessionFields()
    {
        Json().Save(Cache(Cached("codex", 10, 20, 30, 40)));
        var json = File.ReadAllText(Json().FilePath);

        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourcePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionProviderCompositionRestoresTempCacheWithoutScanningUserData()
    {
        var now = DateTimeOffset.Now;
        var day = DateOnly.FromDateTime(now.Date);
        var persistence = Json();
        persistence.Save(new UsageSnapshotCache(now.AddMinutes(-5),
        [
            new CachedProviderUsage(
                "codex",
                new DailyUsage(day.ToString("yyyy-MM-dd"), 123, 0, 0, 0, 123, 0),
                null,
                new PeriodUsage(day.ToString("yyyy-MM-dd"), 123, 0),
                new PeriodUsage(day.ToString("yyyy-MM"), 456, 0),
                now.AddMinutes(-5)),
        ]));
        using var httpClient = new HttpClient();

        var usage = AppComposition.CreateUsageViewModel(
            httpClient,
            _ => Array.Empty<string>(),
            snapshotPersistence: persistence);

        Assert.Equal(12, usage.RegisteredProviderIds.Count);
        Assert.Equal("codex", usage.SelectedProviderId);
        Assert.Equal(456, usage.MonthTokens);
        Assert.Equal(ProviderRuntimeStatus.Stale, usage.ProviderStatuses.Single(x => x.ProviderId == "codex").RuntimeStatus);
    }

    [Fact]
    public void CachedSnapshotSeedsStaleProviderWithOriginalTimestamp()
    {
        var persistence = Memory(Cache(Cached("codex", 10, 20, 30, 40)));
        var store = Store([Provider("codex")], persistence);

        Assert.Equal(Now.AddMinutes(-30), store.LastUpdated);
        Assert.Equal(Now.AddMinutes(-45), Assert.Single(store.Snapshots).FetchedAt);
        Assert.Equal(ProviderRuntimeStatus.Stale, Assert.Single(store.ProviderStatuses).RuntimeStatus);
    }

    [Fact]
    public void UnknownProviderIsIgnored()
    {
        var store = Store(
            [Provider("codex")],
            Memory(Cache(Cached("unknown", 10, 20, 30, 40))));

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void AllTwelveCanonicalProvidersRestoreById()
    {
        var ids = new[]
        {
            "codex", "claude_code", "gemini", "antigravity", "cursor", "opencode",
            "hermes", "grok", "copilot", "kiro", "pi", "omp",
        };
        var store = Store(
            ids.Select(Provider).ToArray(),
            Memory(Cache(ids.Select(id => Cached(id, 1, 0, 1, 1)).ToArray())));

        Assert.Equal(ids, store.Snapshots.Select(snapshot => snapshot.ProviderId));
    }

    [Fact]
    public void YesterdayTodayIsClearedButCurrentWeekAndMonthRemain()
    {
        var cached = Cached("codex", 10, 0, 30, 40) with
        {
            Today = Daily(10, "2026-09-01"),
        };
        var store = Store([Provider("codex")], Memory(Cache(cached)));

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Null(snapshot.Today);
        Assert.Equal(30, snapshot.WeekTotal!.TotalTokens);
        Assert.Equal(40, snapshot.MonthTotal!.TotalTokens);
    }

    [Fact]
    public void PreviousWeekIsClearedWhileCurrentMonthRemains()
    {
        var cached = Cached("codex", 0, 0, 30, 40) with
        {
            WeekTotal = new PeriodUsage("2026-08-24", 30, 0),
        };
        var store = Store([Provider("codex")], Memory(Cache(cached)));

        Assert.Null(Assert.Single(store.Snapshots).WeekTotal);
        Assert.Equal(40, Assert.Single(store.Snapshots).MonthTotal!.TotalTokens);
    }

    [Fact]
    public void PreviousMonthIsCleared()
    {
        var cached = Cached("codex", 0, 0, 0, 40) with
        {
            MonthTotal = new PeriodUsage("2026-08", 40, 0),
        };
        var store = Store([Provider("codex")], Memory(Cache(cached)));

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void ExpiredFiveHourBlockIsCleared()
    {
        var cached = Cached("codex", 0, 20, 0, 40) with
        {
            ActiveBlock = Block(20, Now.AddMinutes(-1)),
        };
        var store = Store([Provider("codex")], Memory(Cache(cached)));

        Assert.Null(Assert.Single(store.Snapshots).ActiveBlock);
    }

    [Fact]
    public void CurrentFiveHourBlockIsRestored()
    {
        var store = Store(
            [Provider("codex")],
            Memory(Cache(Cached("codex", 0, 20, 0, 40))));

        Assert.Equal(20, Assert.Single(store.Snapshots).ActiveBlock!.TotalTokens);
    }

    [Fact]
    public async Task ProviderFailurePreservesCachedSnapshotWithoutWriting()
    {
        var provider = Provider("codex");
        provider.DailyError = new IOException("offline");
        provider.EnrichmentError = new IOException("offline");
        var persistence = Memory(Cache(Cached("codex", 10, 20, 30, 40)));
        var store = Store([provider], persistence);

        await store.RefreshAsync();

        Assert.Equal(40, Assert.Single(store.Snapshots).MonthTotal!.TotalTokens);
        Assert.Equal(0, persistence.SaveCalls);
        Assert.Equal(ProviderRuntimeStatus.Stale, Assert.Single(store.ProviderStatuses).RuntimeStatus);
    }

    [Fact]
    public async Task SuccessfulRefreshReplacesCachedSnapshot()
    {
        var provider = Provider("codex", 50, 60, 70, 80);
        var persistence = Memory(Cache(Cached("codex", 10, 20, 30, 40)));
        var store = Store([provider], persistence);

        await store.RefreshAsync();

        var saved = Assert.Single(persistence.Cache!.Providers);
        Assert.Equal(50, saved.Today!.TotalTokens);
        Assert.Equal(80, saved.MonthTotal!.TotalTokens);
        Assert.Equal(1, persistence.SaveCalls);
        Assert.Equal(ProviderRuntimeStatus.Ready, Assert.Single(store.ProviderStatuses).RuntimeStatus);
    }

    [Fact]
    public async Task SuccessfulEmptyRefreshRemovesCachedProvider()
    {
        var persistence = Memory(Cache(Cached("codex", 10, 20, 30, 40)));
        var store = Store([Provider("codex")], persistence);

        await store.RefreshAsync();

        Assert.Empty(store.Snapshots);
        Assert.Empty(persistence.Cache!.Providers);
    }

    [Fact]
    public async Task OneProviderFailureDoesNotBlockAnotherProviderCacheUpdate()
    {
        var good = Provider("codex", 50, 0, 70, 80);
        var bad = Provider("claude_code");
        bad.DailyError = new IOException("offline");
        bad.EnrichmentError = new IOException("offline");
        var persistence = Memory(Cache(
            Cached("codex", 10, 0, 30, 40),
            Cached("claude_code", 11, 0, 31, 41)));
        var store = Store([good, bad], persistence);

        await store.RefreshAsync();

        Assert.Equal(80, persistence.Cache!.Providers.Single(x => x.ProviderId == "codex").MonthTotal!.TotalTokens);
        Assert.Equal(41, persistence.Cache.Providers.Single(x => x.ProviderId == "claude_code").MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task CacheWriteFailureDoesNotFailUsageRefresh()
    {
        var persistence = Memory();
        persistence.SaveError = new IOException("disk full");
        var store = Store([Provider("codex", 10, 0, 0, 10)], persistence);

        await store.RefreshAsync();

        Assert.Equal(10, Assert.Single(store.Snapshots).TodayTotalTokens);
    }

    [Fact]
    public void CacheLoadFailureDoesNotFailStoreConstruction()
    {
        var persistence = Memory();
        persistence.LoadError = new IOException("locked");

        var store = Store([Provider("codex")], persistence);

        Assert.Empty(store.Snapshots);
        Assert.Equal(UsageCacheLoadStatus.Corrupt, store.UsageCacheStatus);
    }

    [Fact]
    public async Task ZeroTodayMonthCarrierIsPersisted()
    {
        var persistence = Memory();
        var store = Store([Provider("codex", 0, 0, 0, 40)], persistence);

        await store.RefreshAsync();

        var cached = Assert.Single(persistence.Cache!.Providers);
        Assert.Null(cached.Today);
        Assert.Equal(40, cached.MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task OfficialLimitsAndAuthenticationAreNotPersisted()
    {
        var persistence = Memory();
        var first = new UsageStore(
            [Provider("codex")], Clock(),
            codexRateLimitsProvider: new FixedCodexLimits(),
            snapshotPersistence: persistence);
        await first.RefreshAsync();
        var restarted = Store([Provider("codex")], persistence);

        Assert.Empty(persistence.Cache!.Providers);
        Assert.Empty(restarted.Snapshots);
        Assert.Equal(ProviderAuthStatus.NotApplicable, Assert.Single(restarted.ProviderStatuses).AuthStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private JsonUsageSnapshotPersistence Json() =>
        new(Path.Combine(_directory, "usage-cache.json"));

    private void WriteRaw(string value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Json().FilePath, value);
    }

    private string ValidJson()
    {
        Json().Save(Cache(Cached("codex", 10, 20, 30, 40)));
        return File.ReadAllText(Json().FilePath);
    }

    private static UsageStore Store(
        IReadOnlyList<MutableProvider> providers,
        IUsageSnapshotPersistence persistence) =>
        new(providers, Clock(), snapshotPersistence: persistence);

    private static MemoryUsagePersistence Memory(UsageSnapshotCache? cache = null) => new(cache);

    private static UsageSnapshotCache Cache(params CachedProviderUsage[] providers) =>
        new(Now.AddMinutes(-30), providers);

    private static CachedProviderUsage Cached(
        string id, long today, long block, long week, long month, double cost = 0) => new(
        id,
        today > 0 ? Daily(today, "2026-09-02", cost) : null,
        block > 0 ? Block(block, Now.AddHours(1)) : null,
        new PeriodUsage("2026-08-31", week, cost),
        new PeriodUsage("2026-09", month, cost),
        Now.AddMinutes(-45));

    private static DailyUsage Daily(long tokens, string date, double cost = 0) =>
        new(date, tokens, tokens, tokens, tokens, tokens, cost);

    private static BlockUsage Block(long tokens, DateTimeOffset end) => new(
        "block",
        Now.AddHours(-1).ToString("O", CultureInfo.InvariantCulture),
        end.ToString("O", CultureInfo.InvariantCulture),
        true,
        tokens,
        0,
        1);

    private static MutableProvider Provider(string id) => new(id);

    private static MutableProvider Provider(
        string id, long today, long block, long week, long month) => new(id)
        {
            Daily = today > 0 ? Daily(today, "2026-09-02") : null,
            Enrichment = new ProviderEnrichment(
                block > 0 ? Block(block, Now.AddHours(1)) : null,
                BlocksOK: true,
                new PeriodUsage("2026-08-31", week, 0),
                new PeriodUsage("2026-09", month, 0),
                PeriodsOK: true),
        };

    private static FixedTimeProvider Clock() => new(Now);

    private sealed class MutableProvider(string id) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool ReportsCost => true;
        public DailyUsage? Daily { get; set; }
        public ProviderEnrichment Enrichment { get; set; } = new();
        public Exception? DailyError { get; set; }
        public Exception? EnrichmentError { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            DailyError is null ? Task.FromResult(Daily) : Task.FromException<DailyUsage?>(DailyError);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            EnrichmentError is null
                ? Task.FromResult(Enrichment)
                : Task.FromException<ProviderEnrichment>(EnrichmentError);
    }

    private sealed class MemoryUsagePersistence(UsageSnapshotCache? cache = null) : IUsageSnapshotPersistence
    {
        public UsageSnapshotCache? Cache { get; private set; } = cache;
        public int SaveCalls { get; private set; }
        public Exception? LoadError { get; set; }
        public Exception? SaveError { get; set; }

        public UsageSnapshotCacheLoadResult Load()
        {
            if (LoadError is not null) throw LoadError;
            return Cache is null
                ? new(UsageCacheLoadStatus.Missing)
                : new(UsageCacheLoadStatus.Available, Cache);
        }

        public void Save(UsageSnapshotCache cache)
        {
            SaveCalls++;
            if (SaveError is not null) throw SaveError;
            Cache = cache;
        }
    }

    private sealed class FixedCodexLimits : ICodexRateLimitsProvider
    {
        public Task<CodexRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CodexRateLimitStatus?>(new(
                new CodexRateLimitSnapshot(
                    "codex", "Codex",
                    new CodexRateLimitWindow(0, 300, null),
                    null, null, null, null, null)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

public sealed class Phase7CBackgroundReliabilityTests
{
    [Fact]
    public async Task BackgroundFailureIsObserved()
    {
        await AppReliability.ObserveAsync(Task.FromException(new IOException("offline")));
    }

    [Fact]
    public async Task BackgroundCancellationIsObserved()
    {
        await AppReliability.ObserveAsync(Task.FromCanceled(new CancellationToken(true)));
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(StackOverflowException))]
    [InlineData(typeof(AccessViolationException))]
    public void FatalRuntimeFailuresAreNotRecoverable(Type type)
    {
        var exception = (Exception)Activator.CreateInstance(type)!;

        Assert.True(AppReliability.IsFatal(exception));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public void KnownUiSideEffectFailuresAreDispatcherRecoverable(Type type)
    {
        var exception = (Exception)Activator.CreateInstance(type)!;

        Assert.True(AppReliability.IsRecoverableDispatcherException(exception));
    }

    [Fact]
    public void UnknownProgrammingFailureIsNotGloballySwallowed()
    {
        Assert.False(AppReliability.IsRecoverableDispatcherException(new NullReferenceException()));
    }
}
