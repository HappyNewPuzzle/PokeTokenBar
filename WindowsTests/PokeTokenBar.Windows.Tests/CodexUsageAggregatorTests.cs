using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexUsageAggregatorTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 30, 1, 0, 0, TimeSpan.Zero);

    private readonly string _temporaryDirectory;

    public CodexUsageAggregatorTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexUsageAggregatorTests-{Guid.NewGuid():N}");
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
    public void Sum_EmptyEvents_ReturnsZeroEntry()
    {
        var result = CodexUsageAggregator.Sum([]);

        Assert.Equal(new CodexUsageEntry(0, 0, 0, 0), result);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void Sum_SingleEvent_ReturnsSameEntryValues()
    {
        var entry = new CodexUsageEntry(100, 20, 50, 7);

        var result = CodexUsageAggregator.Sum([Canonical(entry)]);

        Assert.Equal(entry, result);
    }

    [Fact]
    public void Sum_MultipleEvents_AddsEveryFieldIndependently()
    {
        var first = Canonical(new CodexUsageEntry(100, 20, 50, 0), "a.jsonl");
        var second = Canonical(new CodexUsageEntry(200, 30, 80, 4), "b.jsonl");

        var result = CodexUsageAggregator.Sum([first, second]);

        Assert.Equal(300, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
        Assert.Equal(130, result.CacheReadTokens);
        Assert.Equal(4, result.CacheWriteTokens);
    }

    [Fact]
    public void Sum_AddsInputTokens()
    {
        var result = CodexUsageAggregator.Sum(
            [Canonical(new CodexUsageEntry(10, 0, 0, 0)),
             Canonical(new CodexUsageEntry(20, 0, 0, 0), "b.jsonl")]);

        Assert.Equal(30, result.InputTokens);
        Assert.Equal(new CodexUsageEntry(30, 0, 0, 0), result);
    }

    [Fact]
    public void Sum_AddsOutputTokens()
    {
        var result = CodexUsageAggregator.Sum(
            [Canonical(new CodexUsageEntry(0, 10, 0, 0)),
             Canonical(new CodexUsageEntry(0, 20, 0, 0), "b.jsonl")]);

        Assert.Equal(new CodexUsageEntry(0, 30, 0, 0), result);
    }

    [Fact]
    public void Sum_AddsCacheReadTokens()
    {
        var result = CodexUsageAggregator.Sum(
            [Canonical(new CodexUsageEntry(0, 0, 10, 0)),
             Canonical(new CodexUsageEntry(0, 0, 20, 0), "b.jsonl")]);

        Assert.Equal(new CodexUsageEntry(0, 0, 30, 0), result);
    }

    [Fact]
    public void Sum_AddsCacheWriteTokens()
    {
        var result = CodexUsageAggregator.Sum(
            [Canonical(new CodexUsageEntry(0, 0, 0, 10)),
             Canonical(new CodexUsageEntry(0, 0, 0, 20), "b.jsonl")]);

        Assert.Equal(new CodexUsageEntry(0, 0, 0, 30), result);
    }

    [Fact]
    public void Sum_TotalTokensEqualsFourFieldSum()
    {
        var result = CodexUsageAggregator.Sum(
            [Canonical(new CodexUsageEntry(80, 10, 40, 3))]);

        Assert.Equal(133, result.TotalTokens);
    }

    [Fact]
    public void Sum_KeylessCanonicalEvent_IsIncluded()
    {
        var keyless = Canonical(
            new CodexUsageEntry(40, 5, 10, 2),
            hasCanonicalKey: false);

        var result = CodexUsageAggregator.Sum([keyless]);

        Assert.Equal(new CodexUsageEntry(40, 5, 10, 2), result);
    }

    [Fact]
    public void Sum_FinalizedCanonicalDuplicate_IsCountedOnce()
    {
        var first = Resolved(
            "a.jsonl",
            "session",
            null,
            EpochEvent(100, new CodexUsageEntry(80, 10, 20, 0), BaseTime, "session"));
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            EpochEvent(100, new CodexUsageEntry(80, 10, 20, 0), BaseTime.AddSeconds(1), "session"));
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Single(finalized);
        Assert.Equal(new CodexUsageEntry(80, 10, 20, 0), result);
    }

    [Fact]
    public void Sum_DuplicateWithDifferentEntries_UsesEarliestSelectedEntryOnly()
    {
        var later = Resolved(
            "a.jsonl",
            "session",
            null,
            EpochEvent(100, new CodexUsageEntry(999, 99, 9, 0), BaseTime.AddMinutes(20), "session"));
        var expectedEntry = new CodexUsageEntry(100, 20, 50, 3);
        var earlier = Resolved(
            "b.jsonl",
            "session",
            null,
            EpochEvent(100, expectedEntry, BaseTime.AddMinutes(5), "session"));
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([later, earlier]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Single(finalized);
        Assert.Equal(expectedEntry, result);
    }

    [Fact]
    public void Sum_DependencyOnlyParent_IsExcludedByFinalizerInputContract()
    {
        var parent = Resolved(
            "parent.jsonl",
            "parent",
            null,
            EpochEvent(100, new CodexUsageEntry(1_000, 100, 0, 0), BaseTime, "parent"));
        var child = Resolved(
            "child.jsonl",
            "child",
            "parent",
            EpochEvent(200, new CodexUsageEntry(200, 20, 0, 0), BaseTime.AddSeconds(2), "child"));
        var pipelineResult = PipelineResult([child], [parent]);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipelineResult);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(new CodexUsageEntry(200, 20, 0, 0), result);
    }

    [Fact]
    public void Sum_StructuralFork_CountsOwnedUsageWithoutReplayOrDependencyParent()
    {
        var replayEntry = new CodexUsageEntry(100, 10, 0, 0);
        var ownedEntry = new CodexUsageEntry(200, 20, 50, 0);
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, replayEntry, BaseTime, "parent"));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Token(100, replayEntry, BaseTime.AddSeconds(1), "child"),
            Token(200, ownedEntry, BaseTime.AddSeconds(3), "child"));
        var resolvedChild = ResolveByPath([parent, child], child.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolvedChild]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(1, resolvedChild.ReplayCount);
        Assert.Equal(ownedEntry, result);
    }

    [Fact]
    public void Sum_FallbackFork_ExcludesLeadingBurstAndCountsSuffix()
    {
        var replayEntry = new CodexUsageEntry(100, 10, 0, 0);
        var ownedEntry = new CodexUsageEntry(300, 30, 20, 0);
        var child = Rollout(
            "fallback.jsonl",
            "child",
            "missing",
            Token(100, replayEntry, BaseTime, "child"),
            Token(200, replayEntry, BaseTime.AddMilliseconds(100), "child"),
            Token(300, ownedEntry, BaseTime.AddSeconds(3), "child"));
        var resolvedChild = ResolveByPath([child], child.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolvedChild]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(2, resolvedChild.ReplayCount);
        Assert.Equal(ownedEntry, result);
    }

    [Fact]
    public void Sum_PreservedSubagentUsage_IsIncluded()
    {
        var entry = new CodexUsageEntry(50, 5, 10, 0);
        var subagent = Rollout(
            "subagent.jsonl",
            "subagent",
            "missing",
            isSubagent: true,
            Token(50, entry, BaseTime, "subagent"));
        var resolved = ResolveByPath([subagent], subagent.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(0, resolved.ReplayCount);
        Assert.Equal(entry, result);
    }

    [Fact]
    public void Sum_SubagentStructuralReplay_IsExcluded()
    {
        var replayEntry = new CodexUsageEntry(100, 10, 0, 0);
        var ownedEntry = new CodexUsageEntry(60, 6, 5, 0);
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, replayEntry, BaseTime, "parent"));
        var subagent = Rollout(
            "subagent.jsonl",
            "subagent",
            "parent",
            isSubagent: true,
            Token(100, replayEntry, BaseTime.AddSeconds(1), "subagent"),
            Token(200, ownedEntry, BaseTime.AddSeconds(3), "subagent"));
        var resolved = ResolveByPath([parent, subagent], subagent.FilePath);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(1, resolved.ReplayCount);
        Assert.Equal(ownedEntry, result);
    }

    [Fact]
    public void Sum_ActiveArchiveCanonicalDuplicate_IsCountedOnce()
    {
        var entry = new CodexUsageEntry(80, 10, 40, 0);
        var active = Resolved(
            Path.Combine("sessions", "rollout.jsonl"),
            "session",
            null,
            EpochEvent(100, entry, BaseTime, "session"));
        var archived = Resolved(
            Path.Combine("archived_sessions", "rollout.jsonl"),
            "session",
            null,
            EpochEvent(100, entry, BaseTime.AddSeconds(1), "session"));
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([active, archived]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Single(finalized);
        Assert.Equal(entry, result);
    }

    [Fact]
    public void Sum_ForkActiveArchiveCopies_CountOwnedUsageOnce()
    {
        var replayEntry = new CodexUsageEntry(100, 10, 0, 0);
        var ownedEntry = new CodexUsageEntry(70, 7, 20, 0);
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, replayEntry, BaseTime, "parent"));
        var active = Rollout(
            Path.Combine("sessions", "child.jsonl"),
            "child",
            "parent",
            Token(100, replayEntry, BaseTime.AddSeconds(1), "child"),
            Token(200, ownedEntry, BaseTime.AddSeconds(3), "child"));
        var archived = Rollout(
            Path.Combine("archived_sessions", "child.jsonl"),
            "child",
            "parent",
            Token(100, replayEntry, BaseTime.AddSeconds(2), "child"),
            Token(200, ownedEntry, BaseTime.AddSeconds(4), "child"));
        var resolved = CodexInMemoryForkResolver.Resolve([parent, active, archived]);
        var primaryResults = resolved.Where(item => item.FilePath != parent.FilePath).ToArray();
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(primaryResults);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Single(finalized);
        Assert.Equal(ownedEntry, result);
    }

    [Fact]
    public void Sum_KeylessDuplicateLikeEvents_AreEachIncluded()
    {
        var firstEntry = new CodexUsageEntry(10, 1, 2, 0);
        var secondEntry = new CodexUsageEntry(20, 2, 3, 0);
        var first = Resolved(
            "a.jsonl",
            "session",
            null,
            EpochEvent(
                cumulativeInput: null,
                firstEntry,
                BaseTime,
                "session"));
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            EpochEvent(
                cumulativeInput: null,
                secondEntry,
                BaseTime,
                "session"));
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Equal(2, finalized.Count);
        Assert.Equal(new CodexUsageEntry(30, 3, 5, 0), result);
    }

    [Fact]
    public void Sum_UsesCheckedOverflowLikeSwiftIntAddition()
    {
        var maximum = Canonical(
            new CodexUsageEntry(long.MaxValue, 0, 0, 0),
            "maximum.jsonl");
        var one = Canonical(
            new CodexUsageEntry(1, 0, 0, 0),
            "one.jsonl");

        Assert.Throws<OverflowException>(() =>
            CodexUsageAggregator.Sum([maximum, one]));
        Assert.Throws<OverflowException>(() =>
            new CodexUsageEntry(long.MaxValue, 1, 0, 0).TotalTokens);
    }

    [Fact]
    public void Sum_TempJsonlPipelineFinalizerIntegration_ReturnsOwnedUsageTotal()
    {
        var sessions = Path.Combine(_temporaryDirectory, "sessions");
        var archived = Path.Combine(_temporaryDirectory, "archived_sessions");
        var cutoff = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var old = cutoff.AddDays(-1);
        var recent = cutoff.AddDays(1);

        WriteJsonl(
            archived,
            "parent.jsonl",
            old,
            SessionMeta("parent"),
            StateLine("2026-07-29T01:00:01.000Z", 120, 40, 10, 120, 40, 10));
        WriteJsonl(
            sessions,
            "child.jsonl",
            recent,
            SessionMeta("child", "parent"),
            StateLine("2026-07-30T01:00:01.000Z", 120, 40, 10, 120, 40, 10),
            StateLine("2026-07-30T01:00:03.000Z", 300, 100, 30, 180, 60, 20));

        var pipelineResult = CodexLocalRolloutPipeline.LoadFromRoots(
            [sessions, archived],
            cutoff);
        var finalized = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipelineResult);
        var result = CodexUsageAggregator.Sum(finalized);

        Assert.Single(pipelineResult.DependencyRollouts);
        Assert.Single(finalized);
        Assert.Equal(new CodexUsageEntry(120, 20, 60, 0), result);
        Assert.Equal(200, result.TotalTokens);
    }

    [Fact]
    public void Sum_FinalizedInputOrderDoesNotChangeTotal()
    {
        var first = Resolved(
            "a.jsonl",
            "a",
            null,
            EpochEvent(100, new CodexUsageEntry(10, 1, 2, 0), BaseTime, "a"));
        var second = Resolved(
            "b.jsonl",
            "b",
            null,
            EpochEvent(200, new CodexUsageEntry(20, 2, 3, 0), BaseTime, "b"));

        var forward = CodexUsageAggregator.Sum(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]));
        var reverse = CodexUsageAggregator.Sum(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([second, first]));

        Assert.Equal(forward, reverse);
        Assert.Equal(new CodexUsageEntry(30, 3, 5, 0), forward);
    }

    private static CodexCanonicalEvent Canonical(
        CodexUsageEntry entry,
        string path = "event.jsonl",
        bool hasCanonicalKey = true)
    {
        var tokenEvent = EpochEvent(100, entry, BaseTime, "session");
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

    private static CodexLocalRolloutPipelineResult PipelineResult(
        IReadOnlyList<CodexInMemoryResolvedRollout> primaries,
        IReadOnlyList<CodexInMemoryResolvedRollout> dependencies)
    {
        var primaryRollouts = primaries.Select(item => item.OriginalRollout).ToArray();
        var dependencyRollouts = dependencies.Select(item => item.OriginalRollout).ToArray();
        var resolutionRollouts = primaryRollouts.Concat(dependencyRollouts).ToArray();
        var resolutionResults = primaries.Concat(dependencies).ToArray();
        return new CodexLocalRolloutPipelineResult(
            new CodexForkDependencyExpansion(
                primaryRollouts,
                dependencyRollouts,
                resolutionRollouts),
            resolutionResults,
            primaries);
    }

    private static CodexInMemoryResolvedRollout Resolved(
        string path,
        string sessionId,
        string? parentSessionId,
        params CodexEpochTokenEvent[] events)
    {
        var rollout = new CodexEpochRollout(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult(
                sessionId,
                parentSessionId,
                IsSubagent: false),
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
            new CodexSessionMetaParseResult(
                sessionId,
                parentSessionId,
                isSubagent),
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
        long? cumulativeInput,
        CodexUsageEntry entry,
        DateTimeOffset timestamp,
        string sessionId,
        int? epoch = 0) =>
        new(
            Token(cumulativeInput, entry, timestamp, sessionId),
            epoch);

    private static CodexRolloutTokenEvent Token(
        long? cumulativeInput,
        CodexUsageEntry entry,
        DateTimeOffset timestamp,
        string sessionId)
    {
        CodexUsageVector? cumulative = cumulativeInput is long input
            ? Vector(input)
            : null;
        var last = Vector(cumulativeInput ?? 10);
        var tokenCount = new CodexTokenCountParseResult(
            timestamp,
            entry,
            last,
            cumulative);
        return new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            ParentSessionId: null,
            IsSubagent: false);
    }

    private static CodexUsageVector Vector(long input) =>
        new(input, 0, 0, 0, 0, input);

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
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-07-30T01:00:00.000Z\","
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
