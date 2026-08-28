using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexUsagePeriodAggregatorTests : IDisposable
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    private static readonly TimeZoneInfo KoreaTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("Test/Korea", KoreaOffset, "Test/Korea", "Test/Korea");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, KoreaOffset);
    private static readonly CodexUsageEntry Usage = new(80, 10, 40, 0);

    private readonly string _temporaryDirectory;

    public CodexUsagePeriodAggregatorTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexUsagePeriodAggregatorTests-{Guid.NewGuid():N}");
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
    public void Calculate_EmptyEvents_ReturnsFourZeroBuckets()
    {
        var result = Calculate([]);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(Zero, result.ThisWeek);
        Assert.Equal(Zero, result.ThisMonth);
        Assert.Equal(Zero, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_TodayEvent_IsIncludedInToday()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 28, 8))]);

        Assert.Equal(Usage, result.Today);
    }

    [Fact]
    public void Calculate_YesterdayLocalEvent_IsExcludedFromToday()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 27, 23))]);

        Assert.Equal(Zero, result.Today);
    }

    [Fact]
    public void Calculate_PreviousUtcDateButCurrentLocalDate_IsToday()
    {
        var timestamp = new DateTimeOffset(2026, 8, 27, 15, 30, 0, TimeSpan.Zero);

        var result = Calculate([Canonical(timestamp)]);

        Assert.Equal(Usage, result.Today);
    }

    [Fact]
    public void Calculate_CurrentUtcDateButPreviousLocalDate_IsNotToday()
    {
        var west = TimeZoneInfo.CreateCustomTimeZone(
            "Test/West",
            TimeSpan.FromHours(-8),
            "Test/West",
            "Test/West");
        var now = new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero);

        var result = Calculate([Canonical(timestamp)], now, west);

        Assert.Equal(Zero, result.Today);
    }

    [Fact]
    public void Calculate_WeekStartAtLocalMidnight_IsIncluded()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 24, 0))]);

        Assert.Equal(Usage, result.ThisWeek);
    }

    [Fact]
    public void Calculate_InstantBeforeWeekStart_IsExcluded()
    {
        var timestamp = new DateTimeOffset(2026, 8, 23, 23, 59, 59, KoreaOffset);

        var result = Calculate([Canonical(timestamp)]);

        Assert.Equal(Zero, result.ThisWeek);
    }

    [Fact]
    public void Calculate_FirstDayOfWeek_ControlsCalendarWeekLikeSwiftCurrentCalendar()
    {
        var sunday = Canonical(AtLocal(2026, 8, 23, 12));

        var sundayFirst = Calculate([sunday], firstDayOfWeek: DayOfWeek.Sunday);
        var mondayFirst = Calculate([sunday], firstDayOfWeek: DayOfWeek.Monday);

        Assert.Equal(Usage, sundayFirst.ThisWeek);
        Assert.Equal(Zero, mondayFirst.ThisWeek);
    }

    [Fact]
    public void Calculate_ThreeParameterOverload_UsesCurrentCultureFirstDayOfWeek()
    {
        var events = Enumerable.Range(0, 8)
            .Select(day => Canonical(
                Now.AddDays(-day),
                new CodexUsageEntry(day + 1, 0, 0, 0),
                $"day-{day}.jsonl"))
            .ToArray();
        var firstDayOfWeek =
            System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        var implicitCulture = CodexUsagePeriodAggregator.Calculate(
            events,
            Now,
            KoreaTimeZone);
        var explicitCulture = CodexUsagePeriodAggregator.Calculate(
            events,
            Now,
            KoreaTimeZone,
            firstDayOfWeek);

        Assert.Equal(explicitCulture, implicitCulture);
    }

    [Fact]
    public void Calculate_MonthStartAtLocalMidnight_IsIncluded()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 1, 0))]);

        Assert.Equal(Usage, result.ThisMonth);
    }

    [Fact]
    public void Calculate_InstantBeforeMonthStart_IsExcluded()
    {
        var timestamp = new DateTimeOffset(2026, 7, 31, 23, 59, 59, KoreaOffset);

        var result = Calculate([Canonical(timestamp)]);

        Assert.Equal(Zero, result.ThisMonth);
    }

    [Fact]
    public void Calculate_ExactlyFiveHoursAgo_IsIncludedInRecentWindow()
    {
        var result = Calculate([Canonical(Now.AddHours(-5))]);

        Assert.Equal(Usage, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_InstantBeforeFiveHourThreshold_IsExcluded()
    {
        var result = Calculate([Canonical(Now.AddHours(-5).AddTicks(-1))]);

        Assert.Equal(Zero, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_RecentWindow_IsIndependentOfTargetTimeZone()
    {
        var timestamp = Now.AddHours(-4);

        var utc = Calculate([Canonical(timestamp)], Now, TimeZoneInfo.Utc);
        var korea = Calculate([Canonical(timestamp)], Now, KoreaTimeZone);

        Assert.Equal(Usage, utc.RecentFiveHours);
        Assert.Equal(utc.RecentFiveHours, korea.RecentFiveHours);
    }

    [Fact]
    public void Calculate_EventExactlyAtNow_IsIncludedInEveryBucket()
    {
        var result = Calculate([Canonical(Now)]);

        AssertAllBuckets(result, Usage);
    }

    [Fact]
    public void Calculate_FutureEvents_FollowSwiftLowerBoundAndLocalDayContracts()
    {
        var laterToday = Canonical(AtLocal(2026, 8, 28, 23));
        var tomorrow = Canonical(AtLocal(2026, 8, 29, 1), path: "tomorrow.jsonl");

        var result = Calculate([laterToday, tomorrow]);

        Assert.Equal(Usage, result.Today);
        Assert.Equal(Usage, result.ThisWeek);
        Assert.Equal(Usage, result.ThisMonth);
        Assert.Equal(Add(Usage, Usage), result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_TodayEvent_IsAlsoIncludedInWeekAndMonth()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 28, 6))]);

        Assert.Equal(Usage, result.Today);
        Assert.Equal(Usage, result.ThisWeek);
        Assert.Equal(Usage, result.ThisMonth);
    }

    [Fact]
    public void Calculate_RecentTodayEvent_IsIncludedInAllFourBuckets()
    {
        var result = Calculate([Canonical(Now.AddHours(-1))]);

        AssertAllBuckets(result, Usage);
    }

    [Fact]
    public void Calculate_TodayEventOlderThanFiveHours_IsNotRecent()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 28, 1))]);

        Assert.Equal(Usage, result.Today);
        Assert.Equal(Zero, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_ThisWeekButNotToday_IsOnlyInBroaderCalendarBuckets()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 25, 12))]);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(Usage, result.ThisWeek);
        Assert.Equal(Usage, result.ThisMonth);
    }

    [Fact]
    public void Calculate_ThisMonthButBeforeThisWeek_IsOnlyInMonth()
    {
        var result = Calculate([Canonical(AtLocal(2026, 8, 10, 12))]);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(Zero, result.ThisWeek);
        Assert.Equal(Usage, result.ThisMonth);
    }

    [Fact]
    public void Calculate_PreviousMonth_IsExcludedFromMonth()
    {
        var result = Calculate([Canonical(AtLocal(2026, 7, 31, 12))]);

        Assert.Equal(Zero, result.ThisMonth);
    }

    [Fact]
    public void Calculate_MultipleEvents_UsesExistingFieldAggregator()
    {
        var first = new CodexUsageEntry(10, 20, 30, 40);
        var second = new CodexUsageEntry(1, 2, 3, 4);
        var events = new[]
        {
            Canonical(Now.AddHours(-1), first),
            Canonical(Now.AddHours(-2), second, "second.jsonl")
        };

        var result = Calculate(events);
        var expected = CodexUsageAggregator.Sum(events);

        Assert.Equal(expected, result.Today);
        Assert.Equal(new CodexUsageEntry(11, 22, 33, 44), result.Today);
        Assert.Equal(110, result.Today.TotalTokens);
    }

    [Fact]
    public void Calculate_KeylessEvent_IsAggregatedNormally()
    {
        var result = Calculate(
            [Canonical(Now.AddHours(-1), hasCanonicalKey: false)]);

        Assert.Equal(Usage, result.Today);
        Assert.Equal(Usage, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_ActiveArchiveDuplicateAfterFinalizer_IsCountedOnce()
    {
        var active = Resolved(
            Path.Combine("sessions", "rollout.jsonl"),
            "session",
            null,
            EpochEvent(100, Usage, Now.AddHours(-2), "session"));
        var archived = Resolved(
            Path.Combine("archived_sessions", "rollout.jsonl"),
            "session",
            null,
            EpochEvent(100, Usage, Now.AddHours(-1), "session"));
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([active, archived]);

        var result = Calculate(finalized);

        Assert.Single(finalized);
        AssertAllBuckets(result, Usage);
    }

    [Fact]
    public void Calculate_StructuralForkAfterFinalizer_CountsOwnedUsageOnly()
    {
        var replay = new CodexUsageEntry(100, 10, 0, 0);
        var owned = new CodexUsageEntry(70, 7, 20, 0);
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, replay, Now.AddHours(-2), "parent"));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Token(100, replay, Now.AddHours(-2), "child"),
            Token(200, owned, Now.AddHours(-1), "child"));
        var resolved = ResolveByPath([parent, child], child.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var result = Calculate(finalized);

        Assert.Equal(1, resolved.ReplayCount);
        AssertAllBuckets(result, owned);
    }

    [Fact]
    public void Calculate_FallbackForkAfterFinalizer_CountsOwnedSuffixOnly()
    {
        var replay = new CodexUsageEntry(100, 10, 0, 0);
        var owned = new CodexUsageEntry(60, 6, 5, 0);
        var start = Now.AddHours(-2);
        var child = Rollout(
            "fallback.jsonl",
            "child",
            "missing",
            Token(100, replay, start, "child"),
            Token(200, replay, start.AddMilliseconds(100), "child"),
            Token(300, owned, start.AddSeconds(3), "child"));
        var resolved = ResolveByPath([child], child.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var result = Calculate(finalized);

        Assert.Equal(2, resolved.ReplayCount);
        AssertAllBuckets(result, owned);
    }

    [Fact]
    public void Calculate_SubagentWithoutStructuralReplay_PreservesUsage()
    {
        var subagent = Rollout(
            "subagent.jsonl",
            "subagent",
            "missing-parent",
            isSubagent: true,
            Token(100, Usage, Now.AddHours(-1), "subagent"));
        var resolved = ResolveByPath([subagent], subagent.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var result = Calculate(finalized);

        Assert.Equal(0, resolved.ReplayCount);
        AssertAllBuckets(result, Usage);
    }

    [Fact]
    public void Calculate_DstTransition_UsesLocalCalendarDayAndAbsoluteRecentWindow()
    {
        var timeZone = CreateDstTimeZone();
        var now = new DateTimeOffset(2026, 3, 8, 8, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 3, 8, 4, 30, 0, TimeSpan.Zero);

        var result = Calculate([Canonical(timestamp)], now, timeZone);

        Assert.Equal(Zero, result.Today);
        Assert.Equal(Usage, result.RecentFiveHours);
    }

    [Fact]
    public void Calculate_YearBoundary_WeekCanCrossYearWhileMonthDoesNot()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, KoreaOffset);
        var timestamp = new DateTimeOffset(2025, 12, 29, 0, 0, 0, KoreaOffset);

        var result = Calculate([Canonical(timestamp)], now, KoreaTimeZone);

        Assert.Equal(Usage, result.ThisWeek);
        Assert.Equal(Zero, result.ThisMonth);
    }

    [Fact]
    public void Calculate_ExplicitNow_IsDeterministic()
    {
        var events = new[]
        {
            Canonical(Now.AddHours(-1)),
            Canonical(Now.AddDays(-2), path: "older.jsonl")
        };

        var first = Calculate(events);
        var second = Calculate(events);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Calculate_TempJsonlPipelineFinalizerIntegration_ReturnsExpectedPeriods()
    {
        var sessions = Path.Combine(_temporaryDirectory, "sessions");
        var archived = Path.Combine(_temporaryDirectory, "archived_sessions");
        var cutoff = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var old = cutoff.AddDays(-1);
        var recent = cutoff.AddDays(1);

        WriteJsonl(
            archived,
            "parent.jsonl",
            old,
            SessionMeta("parent"),
            StateLine("2026-08-27T01:00:01.000Z", 120, 40, 10, 120, 40, 10));
        WriteJsonl(
            sessions,
            "child.jsonl",
            recent,
            SessionMeta("child", "parent"),
            StateLine("2026-08-28T01:00:01.000Z", 120, 40, 10, 120, 40, 10),
            StateLine("2026-08-28T01:00:03.000Z", 300, 100, 30, 180, 60, 20));

        var pipeline = CodexLocalRolloutPipeline.LoadFromRoots(
            [sessions, archived],
            cutoff);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipeline);
        var result = Calculate(finalized);
        var expected = new CodexUsageEntry(120, 20, 60, 0);

        Assert.Single(pipeline.DependencyRollouts);
        Assert.Single(finalized);
        AssertAllBuckets(result, expected);
    }

    private static CodexUsageEntry Zero => new(0, 0, 0, 0);

    private static CodexUsagePeriods Calculate(
        IEnumerable<CodexCanonicalEvent> events,
        DateTimeOffset? now = null,
        TimeZoneInfo? timeZone = null,
        DayOfWeek firstDayOfWeek = DayOfWeek.Monday) =>
        CodexUsagePeriodAggregator.Calculate(
            events,
            now ?? Now,
            timeZone ?? KoreaTimeZone,
            firstDayOfWeek);

    private static DateTimeOffset AtLocal(
        int year,
        int month,
        int day,
        int hour) =>
        new(year, month, day, hour, 0, 0, KoreaOffset);

    private static CodexUsageEntry Add(
        CodexUsageEntry first,
        CodexUsageEntry second) =>
        new(
            first.InputTokens + second.InputTokens,
            first.OutputTokens + second.OutputTokens,
            first.CacheReadTokens + second.CacheReadTokens,
            first.CacheWriteTokens + second.CacheWriteTokens);

    private static void AssertAllBuckets(
        CodexUsagePeriods periods,
        CodexUsageEntry expected)
    {
        Assert.Equal(expected, periods.Today);
        Assert.Equal(expected, periods.ThisWeek);
        Assert.Equal(expected, periods.ThisMonth);
        Assert.Equal(expected, periods.RecentFiveHours);
    }

    private static CodexCanonicalEvent Canonical(
        DateTimeOffset timestamp,
        CodexUsageEntry? entry = null,
        string path = "event.jsonl",
        bool hasCanonicalKey = true)
    {
        var tokenEvent = EpochEvent(100, entry ?? Usage, timestamp, "session");
        var rollout = new CodexEpochRollout(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult("session", null, false),
            [tokenEvent]);
        CodexCanonicalUsageKey? key = hasCanonicalKey
            ? new CodexCanonicalUsageKey(
                "session",
                0,
                tokenEvent.TokenEvent.TokenCount.CumulativeUsageVector!.Value,
                tokenEvent.TokenEvent.TokenCount.LastUsageVector)
            : null;

        return new CodexCanonicalEvent(rollout, tokenEvent, key);
    }

    private static CodexInMemoryResolvedRollout Resolved(
        string path,
        string sessionId,
        string? parentSessionId,
        params CodexEpochTokenEvent[] events)
    {
        var rollout = new CodexEpochRollout(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult(sessionId, parentSessionId, false),
            events);
        return new CodexInMemoryResolvedRollout(
            rollout,
            rollout,
            events,
            SelectedParent: null,
            ReplayCount: 0);
    }

    private static CodexEpochRollout Rollout(
        string path,
        string sessionId,
        string? parentSessionId,
        params CodexRolloutTokenEvent[] events) =>
        Rollout(path, sessionId, parentSessionId, isSubagent: false, events);

    private static CodexEpochRollout Rollout(
        string path,
        string sessionId,
        string? parentSessionId,
        bool isSubagent,
        params CodexRolloutTokenEvent[] events)
    {
        var parsed = new CodexParsedRollout(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult(sessionId, parentSessionId, isSubagent),
            events);
        return CodexCumulativeEpochAssigner.Assign(parsed);
    }

    private static CodexInMemoryResolvedRollout ResolveByPath(
        IEnumerable<CodexEpochRollout> rollouts,
        string path) =>
        Assert.Single(
            CodexInMemoryForkResolver.Resolve(rollouts),
            item => string.Equals(item.FilePath, path, StringComparison.Ordinal));

    private static CodexEpochTokenEvent EpochEvent(
        long cumulativeInput,
        CodexUsageEntry entry,
        DateTimeOffset timestamp,
        string sessionId) =>
        new(Token(cumulativeInput, entry, timestamp, sessionId), Epoch: 0);

    private static CodexRolloutTokenEvent Token(
        long cumulativeInput,
        CodexUsageEntry entry,
        DateTimeOffset timestamp,
        string sessionId)
    {
        var vector = new CodexUsageVector(
            cumulativeInput,
            0,
            0,
            0,
            0,
            cumulativeInput);
        var tokenCount = new CodexTokenCountParseResult(
            timestamp,
            entry,
            vector,
            vector);
        return new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            ParentSessionId: null,
            IsSubagent: false);
    }

    private static TimeZoneInfo CreateDstTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 3,
            week: 2,
            dayOfWeek: DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 11,
            week: 1,
            dayOfWeek: DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/DST",
            TimeSpan.FromHours(-5),
            "Test/DST",
            "Test/Standard",
            "Test/Daylight",
            [rule]);
    }

    private static string WriteJsonl(
        string root,
        string fileName,
        DateTimeOffset mtime,
        params string[] lines)
    {
        Directory.CreateDirectory(root);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        File.SetLastWriteTimeUtc(path, mtime.UtcDateTime);
        return path;
    }

    private static string SessionMeta(string sessionId, string? parentId = null)
    {
        var parentFields = parentId is null
            ? string.Empty
            : $",\"forked_from_id\":\"{parentId}\",\"parent_thread_id\":\"{parentId}\"";
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-08-28T01:00:00.000Z\","
            + $"\"payload\":{{\"id\":\"{sessionId}\"{parentFields},\"thread_source\":\"user\"}}}}";
    }

    private static string StateLine(
        string timestamp,
        long cumulativeInput,
        long cumulativeCached,
        long cumulativeOutput,
        long lastInput,
        long lastCached,
        long lastOutput) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
        + "\"total_token_usage\":{"
        + $"\"input_tokens\":{cumulativeInput},\"cached_input_tokens\":{cumulativeCached},"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{cumulativeOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{cumulativeInput + cumulativeOutput}}},"
        + "\"last_token_usage\":{"
        + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{lastCached},"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{lastOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{lastInput + lastOutput}}}}}}}}}";
}
