using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalCodexUsageProviderTests : IDisposable
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    private static readonly TimeZoneInfo KoreaTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("Provider/Test/Korea", KoreaOffset, "Korea", "Korea");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, KoreaOffset);
    private static readonly DateTimeOffset ScanStart =
        new(2026, 8, 1, 0, 0, 0, KoreaOffset);

    private readonly string _temporaryDirectory;

    public LocalCodexUsageProviderTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-LocalCodexUsageProviderTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task IUsageProvider_ExposesDailyAndEnrichmentAsSeparateCalls()
    {
        var daily = Daily(10);
        var enrichment = Enrichment(20, 30, 40);
        IUsageProvider provider = new FakeUsageProvider(daily, enrichment);

        Assert.Equal(daily, await provider.FetchDailyAsync());
        Assert.Equal(enrichment, await provider.FetchEnrichmentAsync());
    }

    [Fact]
    public async Task FakeProvider_CanExpressDailySuccessThrowAndEnrichmentIndependently()
    {
        var enrichment = Enrichment(20, 30, 40);
        var success = new FakeUsageProvider(Daily(10), enrichment);
        var failure = new FakeUsageProvider(null, enrichment, dailyError: new TestException());

        Assert.Equal(Daily(10), await success.FetchDailyAsync());
        await Assert.ThrowsAsync<TestException>(() => failure.FetchDailyAsync());
        Assert.Equal(enrichment, await failure.FetchEnrichmentAsync());
    }

    [Fact]
    public void Metadata_MatchesSwiftLocalCodexProvider()
    {
        IUsageProvider provider = new LocalCodexUsageProvider([]);

        Assert.Equal("codex", provider.Id);
        Assert.Equal("Codex", provider.DisplayName);
        Assert.True(provider.ReportsCost);
    }

    [Fact]
    public async Task FetchDailyAsync_MapsTodayToExactDailyUsage()
    {
        var root = Root("sessions");
        WriteRollout(root, "one.jsonl", Now, SessionMeta("one"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(80, 10, 40)));
        var provider = new LocalCodexUsageProvider([root]);

        var result = await FetchDaily(provider);

        Assert.Equal(
            new DailyUsage("2026-08-28", 80, 10, 0, 40, 130, 0),
            result);
    }

    [Fact]
    public async Task FetchDailyAsync_EmptyLocalData_ReturnsNull()
    {
        var provider = new LocalCodexUsageProvider([]);

        var result = await FetchDaily(provider);

        Assert.Null(result);
    }

    [Fact]
    public async Task Provider_DailyAndEnrichmentUseTheirSeparateSwiftScanWindows()
    {
        var root = Root("sessions");
        WriteRollout(root, "old-mtime.jsonl", Now.AddDays(-1), SessionMeta("old-mtime"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([root]);

        var daily = await FetchDaily(provider);
        var enrichment = await FetchEnrichment(provider);

        Assert.Null(daily);
        Assert.Equal(50, enrichment.ActiveBlock?.TotalTokens);
        Assert.Equal(50, enrichment.WeekTotal?.TotalTokens);
        Assert.Equal(50, enrichment.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task FetchDailyAsync_CodexCostIsZero()
    {
        var root = Root("sessions");
        WriteRollout(root, "cost.jsonl", Now, SessionMeta("cost"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([root]);

        var result = await FetchDaily(provider);

        Assert.Equal(0, Assert.IsType<DailyUsage>(result).TotalCost);
    }

    [Fact]
    public async Task FetchEnrichmentAsync_MapsRecentFiveHoursToActiveBlock()
    {
        var root = Root("sessions");
        var first = Now.AddHours(-1);
        WriteRollout(root, "recent.jsonl", Now, SessionMeta("recent"),
            StateLine(Instant(first), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([root]);

        var result = await FetchEnrichment(provider);
        var block = Assert.IsType<BlockUsage>(result.ActiveBlock);

        Assert.True(result.BlocksOK);
        Assert.Equal(50, block.TotalTokens);
        Assert.Equal($"block-{first.ToUnixTimeSeconds()}", block.Id);
        Assert.Equal("2026-08-28T02:00:00Z", block.StartTime);
        Assert.Equal("2026-08-28T07:00:00Z", block.EndTime);
        Assert.True(block.IsActive);
        Assert.Equal(0, block.CostUSD);
        Assert.Equal(50d / 60d, block.TokensPerMinute!.Value, precision: 10);
    }

    [Fact]
    public async Task FetchEnrichmentAsync_MapsThisWeekToPeriodUsage()
    {
        var root = Root("sessions");
        WriteRollout(root, "week.jsonl", Now, SessionMeta("week"),
            StateLine("2026-08-25T03:00:00.000Z", 100, Entry(30)));
        var provider = new LocalCodexUsageProvider([root]);

        var result = await FetchEnrichment(provider);

        Assert.Equal(new PeriodUsage("2026-08-24", 30, 0), result.WeekTotal);
    }

    [Fact]
    public async Task FetchEnrichmentAsync_MapsThisMonthToPeriodUsage()
    {
        var root = Root("sessions");
        WriteRollout(root, "month.jsonl", Now, SessionMeta("month"),
            StateLine("2026-08-10T03:00:00.000Z", 100, Entry(40)));
        var provider = new LocalCodexUsageProvider([root]);

        var result = await FetchEnrichment(provider);

        Assert.Equal(new PeriodUsage("2026-08", 40, 0), result.MonthTotal);
    }

    [Fact]
    public async Task FetchEnrichmentAsync_SuccessSetsMetadataAndZeroCosts()
    {
        var provider = new LocalCodexUsageProvider([]);

        var result = await FetchEnrichment(provider);

        Assert.True(result.BlocksOK);
        Assert.True(result.PeriodsOK);
        Assert.Null(result.ActiveBlock);
        Assert.Equal(new PeriodUsage("2026-08-24", 0, 0), result.WeekTotal);
        Assert.Equal(new PeriodUsage("2026-08", 0, 0), result.MonthTotal);
    }

    [Fact]
    public async Task FetchDailyAsync_UnexpectedServiceErrorFaultsTask()
    {
        var provider = new LocalCodexUsageProvider(["\0"]);

        await Assert.ThrowsAsync<ArgumentException>(() => FetchDaily(provider));
    }

    [Fact]
    public async Task FetchEnrichmentAsync_ServiceErrorReturnsUnsuccessfulEmptyResult()
    {
        var provider = new LocalCodexUsageProvider(["\0"]);

        var result = await FetchEnrichment(provider);

        Assert.Equal(new ProviderEnrichment(), result);
        Assert.False(result.BlocksOK);
        Assert.False(result.PeriodsOK);
    }

    [Fact]
    public async Task Provider_PreCanceledTokenCancelsBothPhases()
    {
        var provider = new LocalCodexUsageProvider([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.FetchDailyAsync(Now, KoreaTimeZone, DayOfWeek.Monday, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.FetchEnrichmentAsync(Now, KoreaTimeZone, DayOfWeek.Monday, cancellation.Token));
    }

    [Fact]
    public async Task Provider_StructuralForkMapsOwnedUsageToBothPhases()
    {
        var root = Root("sessions");
        WriteRollout(root, "parent.jsonl", ScanStart.AddDays(-1), SessionMeta("parent"),
            StateLine("2026-07-31T01:00:00.000Z", 100, Entry(100)));
        WriteRollout(root, "child.jsonl", Now, SessionMeta("child", "parent"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(100)),
            StateLine(Instant(Now.AddHours(-1)), 200, Entry(60)));
        var provider = new LocalCodexUsageProvider([root]);

        var daily = Assert.IsType<DailyUsage>(await FetchDaily(provider));
        var enrichment = await FetchEnrichment(provider);

        Assert.Equal(60, daily.TotalTokens);
        Assert.Equal(60, Assert.IsType<BlockUsage>(enrichment.ActiveBlock).TotalTokens);
        Assert.Equal(60, enrichment.WeekTotal?.TotalTokens);
        Assert.Equal(60, enrichment.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task Provider_FallbackForkMapsOwnedSuffixOnly()
    {
        var root = Root("sessions");
        var start = Now.AddHours(-2);
        WriteRollout(root, "fallback.jsonl", Now, SessionMeta("child", "missing"),
            StateLine(Instant(start), 100, Entry(100)),
            StateLine(Instant(start.AddMilliseconds(100)), 200, Entry(100)),
            StateLine(Instant(start.AddSeconds(3)), 300, Entry(60)));
        var provider = new LocalCodexUsageProvider([root]);

        var daily = Assert.IsType<DailyUsage>(await FetchDaily(provider));
        var enrichment = await FetchEnrichment(provider);

        Assert.Equal(60, daily.TotalTokens);
        Assert.Equal(60, enrichment.ActiveBlock?.TotalTokens);
    }

    [Fact]
    public async Task Provider_ActiveArchiveDuplicateIsCountedOnceInBothPhases()
    {
        var sessions = Root("sessions");
        var archived = Root("archived_sessions");
        WriteRollout(sessions, "active.jsonl", Now, SessionMeta("same"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(50)));
        WriteRollout(archived, "archived.jsonl", Now, SessionMeta("same"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([sessions, archived]);

        var daily = Assert.IsType<DailyUsage>(await FetchDaily(provider));
        var enrichment = await FetchEnrichment(provider);

        Assert.Equal(50, daily.TotalTokens);
        Assert.Equal(50, enrichment.ActiveBlock?.TotalTokens);
    }

    [Fact]
    public async Task Provider_ExplicitTimeZoneAndWeekStartApplyToSeparatePhases()
    {
        var root = Root("sessions");
        WriteRollout(root, "today-boundary.jsonl", Now, SessionMeta("today"),
            StateLine("2026-08-27T15:30:00.000Z", 100, Entry(10)));
        WriteRollout(root, "sunday.jsonl", Now, SessionMeta("sunday"),
            StateLine("2026-08-23T03:00:00.000Z", 200, Entry(20)));
        var provider = new LocalCodexUsageProvider([root]);

        var daily = Assert.IsType<DailyUsage>(await provider.FetchDailyAsync(
            Now, KoreaTimeZone, DayOfWeek.Sunday));
        var sundayFirst = await provider.FetchEnrichmentAsync(
            Now, KoreaTimeZone, DayOfWeek.Sunday);
        var mondayFirst = await provider.FetchEnrichmentAsync(
            Now, KoreaTimeZone, DayOfWeek.Monday);

        Assert.Equal(10, daily.TotalTokens);
        Assert.Equal(30, sundayFirst.WeekTotal?.TotalTokens);
        Assert.Equal(10, mondayFirst.WeekTotal?.TotalTokens);
    }

    [Fact]
    public async Task Provider_RepeatedCallsKeepNoStaleMutableUsage()
    {
        var root = Root("sessions");
        WriteRollout(root, "first.jsonl", Now, SessionMeta("first"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(10)));
        var provider = new LocalCodexUsageProvider([root]);

        var first = Assert.IsType<DailyUsage>(await FetchDaily(provider));
        WriteRollout(root, "second.jsonl", Now, SessionMeta("second"),
            StateLine(Instant(Now.AddHours(-2)), 200, Entry(20)));
        var second = Assert.IsType<DailyUsage>(await FetchDaily(provider));

        Assert.Equal(10, first.TotalTokens);
        Assert.Equal(30, second.TotalTokens);
    }

    [Fact]
    public async Task Provider_KeylessUsageIsPreserved()
    {
        var root = Root("sessions");
        WriteRollout(root, "keyless.jsonl", Now,
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([root]);

        Assert.Equal(50, (await FetchDaily(provider))?.TotalTokens);
    }

    [Fact]
    public async Task Provider_MalformedLineKeepsValidUsage()
    {
        var root = Root("sessions");
        WriteRollout(root, "malformed.jsonl", Now, "{not-json", SessionMeta("valid"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(50)));
        var provider = new LocalCodexUsageProvider([root]);

        Assert.Equal(50, (await FetchDaily(provider))?.TotalTokens);
    }

    [Fact]
    public async Task Provider_MissingRootReturnsNullDailyAndSuccessfulZeroEnrichment()
    {
        var provider = new LocalCodexUsageProvider(
            [Path.Combine(_temporaryDirectory, "missing")]);

        var daily = await FetchDaily(provider);
        var enrichment = await FetchEnrichment(provider);

        Assert.Null(daily);
        Assert.True(enrichment.BlocksOK);
        Assert.True(enrichment.PeriodsOK);
        Assert.Null(enrichment.ActiveBlock);
        Assert.Equal(0, enrichment.WeekTotal?.TotalTokens);
        Assert.Equal(0, enrichment.MonthTotal?.TotalTokens);
    }

    [Fact]
    public async Task Provider_MapsTheSameTodayTotalAsUnderlyingService()
    {
        var root = Root("sessions");
        WriteRollout(root, "fields.jsonl", Now, SessionMeta("fields"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(80, 10, 40)));
        var provider = new LocalCodexUsageProvider([root]);
        var service = CodexLocalUsageService.LoadFromRoots(
            [root], Now, KoreaTimeZone, DayOfWeek.Monday);

        var daily = Assert.IsType<DailyUsage>(await FetchDaily(provider));

        Assert.Equal(service.Today.TotalTokens, daily.TotalTokens);
        Assert.Equal(service.Today.InputTokens, daily.InputTokens);
        Assert.Equal(service.Today.OutputTokens, daily.OutputTokens);
        Assert.Equal(service.Today.CacheReadTokens, daily.CacheReadTokens);
    }

    private static Task<DailyUsage?> FetchDaily(LocalCodexUsageProvider provider) =>
        provider.FetchDailyAsync(Now, KoreaTimeZone, DayOfWeek.Monday);

    private static Task<ProviderEnrichment> FetchEnrichment(LocalCodexUsageProvider provider) =>
        provider.FetchEnrichmentAsync(Now, KoreaTimeZone, DayOfWeek.Monday);

    private string Root(string name) =>
        Path.Combine(_temporaryDirectory, name);

    private static DailyUsage Daily(long total) =>
        new("2026-08-28", total, 0, 0, 0, total, 0);

    private static ProviderEnrichment Enrichment(
        long block,
        long week,
        long month) =>
        new(
            new BlockUsage("block", "start", "end", true, block, 0, 1),
            BlocksOK: true,
            new PeriodUsage("week", week, 0),
            new PeriodUsage("month", month, 0),
            PeriodsOK: true);

    private static CodexUsageEntry Entry(
        long input,
        long output = 0,
        long cacheRead = 0) =>
        new(input, output, cacheRead, 0);

    private static string WriteRollout(
        string root,
        string relativePath,
        DateTimeOffset mtime,
        params string[] lines)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        File.SetLastWriteTimeUtc(path, mtime.UtcDateTime);
        return path;
    }

    private static string SessionMeta(string sessionId, string? parentId = null)
    {
        var parentFields = parentId is null
            ? string.Empty
            : $",\"forked_from_id\":\"{parentId}\",\"parent_thread_id\":\"{parentId}\"";
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-08-28T00:00:00.000Z\","
            + $"\"payload\":{{\"id\":\"{sessionId}\"{parentFields},\"thread_source\":\"user\"}}}}";
    }

    private static string StateLine(
        string timestamp,
        long cumulativeInput,
        CodexUsageEntry entry)
    {
        var lastInput = entry.InputTokens + entry.CacheReadTokens;
        var cumulativeOutput = entry.OutputTokens;
        var lastTotal = lastInput + entry.OutputTokens;
        return "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
            + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
            + "\"total_token_usage\":{"
            + $"\"input_tokens\":{cumulativeInput},\"cached_input_tokens\":0,"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{cumulativeOutput},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{cumulativeInput + cumulativeOutput}}},"
            + "\"last_token_usage\":{"
            + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{entry.CacheReadTokens},"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{entry.OutputTokens},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{lastTotal}}}}}}}}}";
    }

    private static string Instant(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private sealed class FakeUsageProvider(
        DailyUsage? daily,
        ProviderEnrichment enrichment,
        Exception? dailyError = null) : IUsageProvider
    {
        public string Id => "fake";

        public string DisplayName => "Fake";

        public bool ReportsCost => false;

        public Task<DailyUsage?> FetchDailyAsync(
            CancellationToken cancellationToken = default) =>
            dailyError is null
                ? Task.FromResult(daily)
                : Task.FromException<DailyUsage?>(dailyError);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(enrichment);
    }

    private sealed class TestException : Exception;
}
