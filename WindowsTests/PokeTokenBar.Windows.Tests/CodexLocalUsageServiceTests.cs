using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexLocalUsageServiceTests : IDisposable
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    private static readonly TimeZoneInfo KoreaTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("Service/Test/Korea", KoreaOffset, "Korea", "Korea");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, KoreaOffset);
    private static readonly DateTimeOffset ScanStart =
        new(2026, 8, 1, 0, 0, 0, KoreaOffset);

    private readonly string _temporaryDirectory;

    public CodexLocalUsageServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexLocalUsageServiceTests-{Guid.NewGuid():N}");
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
    public void LoadFromRoots_EmptyRoots_ReturnsZeroPeriods()
    {
        var result = Load([]);

        AssertZero(result);
    }

    [Fact]
    public void LoadFromRoots_SingleTodayEvent_ReturnsAllPeriodUsage()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "one.jsonl",
            Now,
            SessionMeta("one"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 80, output: 10, cacheRead: 40)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(80, 10, 40, 0));
    }

    [Fact]
    public void LoadFromRoots_TodayEventOlderThanFiveHours_IsNotRecent()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "old-today.jsonl",
            Now,
            SessionMeta("old-today"),
            StateLine("2026-08-27T16:00:00.000Z", 100, Entry(input: 50)));

        var result = Load([root]);

        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.Today);
        Assert.Equal(Zero, result.RecentFiveHours);
    }

    [Fact]
    public void LoadFromRoots_ThisWeekButNotToday_IsInWeekAndMonth()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "week.jsonl",
            Now,
            SessionMeta("week"),
            StateLine("2026-08-25T03:00:00.000Z", 100, Entry(input: 50)));

        var result = Load([root]);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.ThisWeek);
        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.ThisMonth);
    }

    [Fact]
    public void LoadFromRoots_ThisMonthButBeforeThisWeek_IsOnlyInMonth()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "month.jsonl",
            Now,
            SessionMeta("month"),
            StateLine("2026-08-10T03:00:00.000Z", 100, Entry(input: 50)));

        var result = Load([root]);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(Zero, result.ThisWeek);
        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.ThisMonth);
    }

    [Fact]
    public void LoadFromRoots_UsesTargetTimeZoneForLocalDateBoundary()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "boundary.jsonl",
            Now,
            SessionMeta("boundary"),
            StateLine("2026-08-27T15:30:00.000Z", 100, Entry(input: 50)));

        var result = Load([root]);

        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.Today);
    }

    [Fact]
    public void LoadFromRoots_UsesExplicitFirstDayOfWeek()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "sunday.jsonl",
            Now,
            SessionMeta("sunday"),
            StateLine("2026-08-23T03:00:00.000Z", 100, Entry(input: 50)));

        var sundayFirst = Load([root], firstDayOfWeek: DayOfWeek.Sunday);
        var mondayFirst = Load([root], firstDayOfWeek: DayOfWeek.Monday);

        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), sundayFirst.ThisWeek);
        Assert.Equal(Zero, mondayFirst.ThisWeek);
    }

    [Fact]
    public void LoadFromRoots_ExplicitNowIsSharedByScanAndAllPeriodBoundaries()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "exact-boundaries.jsonl",
            ScanStart,
            SessionMeta("exact"),
            StateLine(Instant(Now.AddHours(-5)), 100, Entry(input: 50)));

        var first = Load([root]);
        var second = Load([root]);

        Assert.Equal(first, second);
        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), first.RecentFiveHours);
    }

    [Fact]
    public void LoadFromRoots_ModifiedSinceIncludesExactEarliestWindowBoundary()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "at-boundary.jsonl",
            ScanStart,
            SessionMeta("at-boundary"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50)));
        WriteRollout(
            root,
            "before-boundary.jsonl",
            ScanStart.AddSeconds(-1),
            SessionMeta("before-boundary"),
            StateLine(Instant(Now.AddHours(-1)), 200, Entry(input: 999)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_ModifiedSinceUsesWeekStartWhenWeekPrecedesMonthAndRecent()
    {
        var root = Root("sessions");
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, KoreaOffset);
        var weekStart = new DateTimeOffset(2026, 8, 31, 0, 0, 0, KoreaOffset);
        WriteRollout(
            root,
            "week-start.jsonl",
            weekStart,
            SessionMeta("week-start"),
            StateLine("2026-08-31T03:00:00.000Z", 100, Entry(input: 50)));

        var result = Load([root], now);

        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.ThisWeek);
        Assert.Equal(Zero, result.ThisMonth);
    }

    [Fact]
    public void LoadFromRoots_ModifiedSinceUsesRollingWindowWhenItStartsEarliest()
    {
        var root = Root("sessions");
        var now = new DateTimeOffset(2026, 9, 1, 2, 0, 0, KoreaOffset);
        var recentStart = now.AddHours(-5);
        WriteRollout(
            root,
            "recent-start.jsonl",
            recentStart,
            SessionMeta("recent-start"),
            StateLine(Instant(recentStart), 100, Entry(input: 50)));

        var result = Load([root], now);

        Assert.Equal(new CodexUsageEntry(50, 0, 0, 0), result.RecentFiveHours);
    }

    [Fact]
    public void LoadFromRoots_PrimaryMtimeDoesNotForceOldEventIntoPeriods()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "recent-file-old-event.jsonl",
            Now,
            SessionMeta("old-event"),
            StateLine("2026-07-01T03:00:00.000Z", 100, Entry(input: 50)));

        var pipeline = CodexLocalRolloutPipeline.LoadFromRoots([root], ScanStart);
        var result = Load([root]);

        Assert.Single(pipeline.PrimaryRollouts);
        AssertZero(result);
    }

    [Fact]
    public void LoadFromRoots_OldParentIsDependencyButItsUsageIsExcluded()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "parent.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("parent"),
            StateLine("2026-07-31T01:00:00.000Z", 100, Entry(input: 100)));
        WriteRollout(
            root,
            "child.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(input: 100)),
            StateLine(Instant(Now.AddHours(-1)), 200, Entry(input: 60)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(60, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_StructuralForkCountsOwnedEventOnly()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "parent.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("parent"),
            StateLine(Instant(Now.AddHours(-3)), 100, Entry(input: 100)));
        WriteRollout(
            root,
            "child.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(input: 100)),
            StateLine(Instant(Now.AddHours(-1)), 200, Entry(input: 60)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(60, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_MissingParentUsesFallbackOwnedSuffix()
    {
        var root = Root("sessions");
        var start = Now.AddHours(-2);
        WriteRollout(
            root,
            "fallback.jsonl",
            Now,
            SessionMeta("child", "missing"),
            StateLine(Instant(start), 100, Entry(input: 100)),
            StateLine(Instant(start.AddMilliseconds(100)), 200, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(3)), 300, Entry(input: 60)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(60, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_ForkOfForkResolvesAncestorsBeforeFinalUsage()
    {
        var root = Root("sessions");
        var start = Now.AddHours(-3);
        WriteRollout(
            root,
            "a.jsonl",
            ScanStart.AddDays(-2),
            SessionMeta("a"),
            StateLine(Instant(start), 100, Entry(input: 100)));
        WriteRollout(
            root,
            "b.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("b", "a"),
            StateLine(Instant(start.AddSeconds(1)), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(3)), 200, Entry(input: 60)));
        WriteRollout(
            root,
            "c.jsonl",
            Now,
            SessionMeta("c", "b"),
            StateLine(Instant(start.AddSeconds(4)), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(5)), 200, Entry(input: 60)),
            StateLine(Instant(start.AddSeconds(7)), 300, Entry(input: 30)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(30, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_SubagentWithoutStructuralReplayPreservesFirstUsage()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "subagent.jsonl",
            Now,
            SessionMeta("subagent", "missing", isSubagent: true),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_ActiveArchiveCanonicalDuplicateCountsOnce()
    {
        var sessions = Root("sessions");
        var archived = Root("archived_sessions");
        WriteRollout(
            sessions,
            "active.jsonl",
            Now,
            SessionMeta("same"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(input: 50)));
        WriteRollout(
            archived,
            "archived.jsonl",
            Now,
            SessionMeta("same"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50)));

        var result = Load([sessions, archived]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_TokenWithoutSessionMetaRemainsKeylessUsage()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            "keyless.jsonl",
            Now,
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_ConsecutiveDuplicateTokenCountIsCountedOnce()
    {
        var root = Root("sessions");
        var line = StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50));
        WriteRollout(root, "duplicate.jsonl", Now, SessionMeta("duplicate"), line, line);

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_PostReplayOwnedResetStartsFreshAndKeepsOwnedDecrease()
    {
        var root = Root("sessions");
        var start = Now.AddHours(-3);
        WriteRollout(
            root,
            "parent.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("parent"),
            StateLine(Instant(start), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(2)), 200, Entry(input: 100)));
        WriteRollout(
            root,
            "child.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(start.AddSeconds(3)), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(4)), 200, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(6)), 50, Entry(input: 50)),
            StateLine(Instant(start.AddSeconds(8)), 70, Entry(input: 20)),
            StateLine(Instant(start.AddSeconds(10)), 30, Entry(input: 10)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(80, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_MissingRoot_ReturnsZeroPeriods()
    {
        var missing = Path.Combine(_temporaryDirectory, "missing");

        var result = Load([missing]);

        AssertZero(result);
    }

    [Fact]
    public void LoadFromRoots_RecursivelyFindsNestedSessionFile()
    {
        var root = Root("sessions");
        WriteRollout(
            root,
            Path.Combine("2026", "08", "28", "nested.jsonl"),
            Now,
            SessionMeta("nested"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 50)));

        var result = Load([root]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_ArchivedOldParentCanResolveActiveChild()
    {
        var sessions = Root("sessions");
        var archived = Root("archived_sessions");
        WriteRollout(
            archived,
            "parent.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("parent"),
            StateLine("2026-07-31T01:00:00.000Z", 100, Entry(input: 100)));
        WriteRollout(
            sessions,
            "child.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(Now.AddHours(-2)), 100, Entry(input: 100)),
            StateLine(Instant(Now.AddHours(-1)), 200, Entry(input: 50)));

        var result = Load([sessions, archived]);

        AssertAll(result, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_RootOrderingDoesNotChangeUsage()
    {
        var firstRoot = Root("first");
        var secondRoot = Root("second");
        WriteRollout(
            firstRoot,
            "a.jsonl",
            Now,
            SessionMeta("a"),
            StateLine(Instant(Now.AddHours(-1)), 100, Entry(input: 20)));
        WriteRollout(
            secondRoot,
            "b.jsonl",
            Now,
            SessionMeta("b"),
            StateLine(Instant(Now.AddHours(-2)), 200, Entry(input: 30)));

        var forward = Load([firstRoot, secondRoot]);
        var reverse = Load([secondRoot, firstRoot]);

        Assert.Equal(forward, reverse);
        AssertAll(forward, new CodexUsageEntry(50, 0, 0, 0));
    }

    [Fact]
    public void LoadFromRoots_AggregationOverflowIsNotSwallowed()
    {
        var root = Root("sessions");
        var lines = Enumerable.Repeat(
                LastOnlyLine(Instant(Now.AddHours(-1)), 1_000_000_000_000_000),
                9_224)
            .ToArray();
        WriteRollout(root, "overflow.jsonl", Now, lines);

        Assert.Throws<OverflowException>(() => Load([root]));
    }

    [Fact]
    public void LoadFromRoots_MultiFilePublicApiRunsCompleteLocalUsagePath()
    {
        var sessions = Root("sessions");
        var archived = Root("archived_sessions");
        var start = Now.AddHours(-3);
        WriteRollout(
            archived,
            "parent.jsonl",
            ScanStart.AddDays(-1),
            SessionMeta("parent"),
            StateLine("2026-07-31T01:00:00.000Z", 100, Entry(input: 100)));
        WriteRollout(
            sessions,
            "child-active.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(start), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(2)), 200, Entry(input: 60)));
        WriteRollout(
            archived,
            "child-archived.jsonl",
            Now,
            SessionMeta("child", "parent"),
            StateLine(Instant(start.AddSeconds(1)), 100, Entry(input: 100)),
            StateLine(Instant(start.AddSeconds(3)), 200, Entry(input: 60)));
        WriteRollout(
            sessions,
            Path.Combine("nested", "keyless.jsonl"),
            Now,
            StateLine(Instant(Now.AddHours(-1)), 300, Entry(input: 30)));

        var result = CodexLocalUsageService.LoadFromRoots(
            [archived, sessions],
            Now,
            KoreaTimeZone,
            DayOfWeek.Monday);

        AssertAll(result, new CodexUsageEntry(90, 0, 0, 0));
    }

    private static CodexUsageEntry Zero => new(0, 0, 0, 0);

    private string Root(string name) =>
        Path.Combine(_temporaryDirectory, name);

    private static CodexUsagePeriods Load(
        IEnumerable<string> roots,
        DateTimeOffset? now = null,
        DayOfWeek firstDayOfWeek = DayOfWeek.Monday) =>
        CodexLocalUsageService.LoadFromRoots(
            roots,
            now ?? Now,
            KoreaTimeZone,
            firstDayOfWeek);

    private static void AssertZero(CodexUsagePeriods periods) =>
        AssertAll(periods, Zero);

    private static void AssertAll(
        CodexUsagePeriods periods,
        CodexUsageEntry expected)
    {
        Assert.Equal(expected, periods.Today);
        Assert.Equal(expected, periods.ThisWeek);
        Assert.Equal(expected, periods.ThisMonth);
        Assert.Equal(expected, periods.RecentFiveHours);
    }

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

    private static string SessionMeta(
        string sessionId,
        string? parentId = null,
        bool isSubagent = false)
    {
        var parentFields = parentId is null
            ? string.Empty
            : $",\"forked_from_id\":\"{parentId}\",\"parent_thread_id\":\"{parentId}\"";
        var source = isSubagent ? "subagent" : "user";
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-08-28T00:00:00.000Z\","
            + $"\"payload\":{{\"id\":\"{sessionId}\"{parentFields},\"thread_source\":\"{source}\"}}}}";
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

    private static string LastOnlyLine(string timestamp, long input) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
        + "\"last_token_usage\":{"
        + $"\"input_tokens\":{input},\"cached_input_tokens\":0,"
        + "\"cache_write_input_tokens\":0,\"output_tokens\":0,"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{input}}}}}}}}}";

    private static string Instant(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
