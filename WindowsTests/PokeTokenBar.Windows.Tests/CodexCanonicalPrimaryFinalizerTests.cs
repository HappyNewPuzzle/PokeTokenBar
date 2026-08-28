using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexCanonicalPrimaryFinalizerTests : IDisposable
{
    private static readonly CodexUsageVector CumulativeA =
        Vector(input: 100, cached: 20, output: 10, reasoning: 2, total: 110);
    private static readonly CodexUsageVector CumulativeB =
        Vector(input: 300, cached: 100, output: 30, reasoning: 5, total: 330);
    private static readonly CodexUsageVector LastA =
        Vector(input: 100, cached: 20, output: 10, reasoning: 2, total: 110);
    private static readonly CodexUsageVector LastB =
        Vector(input: 200, cached: 80, output: 20, reasoning: 3, total: 220);

    private readonly string _temporaryDirectory;

    public CodexCanonicalPrimaryFinalizerTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexCanonicalPrimaryFinalizerTests-{Guid.NewGuid():N}");
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
    public void CreateCanonicalEvents_OneResolvedPrimaryEvent_ReturnsOneEvent()
    {
        var tokenEvent = Event("session");
        var resolved = Resolved("one.jsonl", "session", null, [tokenEvent], [tokenEvent]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        var canonicalEvent = Assert.Single(result);
        Assert.Same(tokenEvent.TokenEvent, canonicalEvent.TokenEvent.TokenEvent);
        Assert.NotNull(canonicalEvent.CanonicalKey);
    }

    [Fact]
    public void CreateCanonicalEvents_DifferentKeysAcrossPrimaries_PreservesBoth()
    {
        var first = Resolved("a.jsonl", "session-a", null, [Event("session-a")]);
        var second = Resolved("b.jsonl", "session-b", null, [Event("session-b")]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CreateCanonicalEvents_SameKeyAcrossPrimaryCopies_CollapsesToOne()
    {
        var first = Resolved("a.jsonl", "session", null, [Event("session")]);
        var second = Resolved("b.jsonl", "session", null, [Event("session")]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Single(result);
    }

    [Fact]
    public void CreateCanonicalEvents_ActiveAndArchivedCopies_CollapseToOne()
    {
        var active = Resolved(
            Path.Combine("home", ".codex", "sessions", "rollout.jsonl"),
            "session",
            null,
            [Event("session")]);
        var archived = Resolved(
            Path.Combine("home", ".codex", "archived_sessions", "rollout.jsonl"),
            "session",
            null,
            [Event("session")]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([active, archived]);

        Assert.Single(result);
    }

    [Fact]
    public void CreateCanonicalEvents_LaterPathHasEarlierTimestamp_KeepsEarlierEvent()
    {
        var laterTimestamp = Resolved(
            "a.jsonl",
            "session",
            null,
            [Event("session", minute: 20)]);
        var earlierTimestamp = Resolved(
            "z.jsonl",
            "session",
            null,
            [Event("session", minute: 5)]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
                [laterTimestamp, earlierTimestamp]));

        Assert.Equal(earlierTimestamp.FilePath, retained.SourceRollout.FilePath);
        Assert.Equal(5, retained.TokenEvent.TokenEvent.TokenCount.Timestamp.Minute);
    }

    [Fact]
    public void CreateCanonicalEvents_EventWithoutKey_IsPreserved()
    {
        var noKey = Event(sessionId: null);
        var resolved = Resolved("no-key.jsonl", sessionId: null, null, [noKey]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]));

        Assert.Same(noKey.TokenEvent, retained.TokenEvent.TokenEvent);
        Assert.Null(retained.CanonicalKey);
    }

    [Fact]
    public void CreateCanonicalEvents_IdenticalEventsWithoutKeys_AreAllPreserved()
    {
        var first = Resolved(
            "a.jsonl",
            "session",
            null,
            [Event("session", hasCumulative: false)]);
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            [Event("session", hasCumulative: false)]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Null(item.CanonicalKey));
    }

    [Fact]
    public void CreateCanonicalEvents_PipelineResultExcludesDependencyOnlyRollout()
    {
        var dependency = Resolved(
            "dependency.jsonl",
            "dependency",
            null,
            [Event("dependency")]);
        var primary = Resolved(
            "primary.jsonl",
            "primary",
            null,
            [Event("primary")]);
        var pipelineResult = PipelineResult([primary], [dependency]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipelineResult);

        var retained = Assert.Single(result);
        Assert.Equal(primary.FilePath, retained.SourceRollout.FilePath);
    }

    [Fact]
    public void CreateCanonicalEvents_DependencyResolutionResultCannotReplacePrimaryDuplicate()
    {
        var dependency = Resolved(
            "dependency.jsonl",
            "same-session",
            null,
            [Event("same-session", minute: 1)]);
        var primary = Resolved(
            "primary.jsonl",
            "same-session",
            null,
            [Event("same-session", minute: 20)]);
        var pipelineResult = PipelineResult([primary], [dependency]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipelineResult));

        Assert.Equal(primary.FilePath, retained.SourceRollout.FilePath);
        Assert.Equal(20, retained.TokenEvent.TokenEvent.TokenCount.Timestamp.Minute);
    }

    [Fact]
    public void CreateCanonicalEvents_ForkReplayEventsFromOriginalRollout_AreExcluded()
    {
        var replay = Event("child", cumulative: CumulativeA, last: LastA);
        var owned = Event("child", cumulative: CumulativeB, last: LastB, minute: 20);
        var child = Resolved(
            "child.jsonl",
            "child",
            "parent",
            [replay, owned],
            [owned]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([child]);

        var retained = Assert.Single(result);
        Assert.Same(owned.TokenEvent, retained.TokenEvent.TokenEvent);
        Assert.DoesNotContain(result, item =>
            ReferenceEquals(item.TokenEvent.TokenEvent, replay.TokenEvent));
    }

    [Fact]
    public void CreateCanonicalEvents_ForkOwnedEvent_IsIncluded()
    {
        var replay = Event("child", cumulative: CumulativeA, last: LastA);
        var owned = Event("child", cumulative: CumulativeB, last: LastB);
        var child = Resolved(
            "child.jsonl",
            "child",
            "parent",
            [replay, owned],
            [owned]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([child]));

        Assert.Same(owned.TokenEvent, retained.TokenEvent.TokenEvent);
        Assert.Equal("child", retained.CanonicalKey?.OwnerSessionId);
    }

    [Fact]
    public void CreateCanonicalEvents_ForkCopiesCollapseAfterReplayRemoval()
    {
        var replayA = Event("child", cumulative: CumulativeA, last: LastA);
        var ownedA = Event("child", cumulative: CumulativeB, last: LastB, minute: 20);
        var active = Resolved(
            Path.Combine("sessions", "child.jsonl"),
            "child",
            "parent",
            [replayA, ownedA],
            [ownedA]);
        var replayB = Event("child", cumulative: CumulativeA, last: LastA, minute: 2);
        var ownedB = Event("child", cumulative: CumulativeB, last: LastB, minute: 10);
        var archived = Resolved(
            Path.Combine("archived_sessions", "child-copy.jsonl"),
            "child",
            "parent",
            [replayB, ownedB],
            [ownedB]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([active, archived]));

        Assert.Same(ownedB.TokenEvent, retained.TokenEvent.TokenEvent);
    }

    [Fact]
    public void CreateCanonicalEvents_FallbackRemovedBurst_IsExcluded()
    {
        var burstOne = Event("child", cumulative: CumulativeA, last: LastA);
        var burstTwo = Event("child", cumulative: CumulativeB, last: LastB, minute: 11);
        var owned = Event(
            "child",
            cumulative: CumulativeB with { InputTokens = 600, TotalTokens = 660 },
            last: LastB with { InputTokens = 300, TotalTokens = 330 },
            minute: 20);
        var child = Resolved(
            "fallback.jsonl",
            "child",
            "missing",
            [burstOne, burstTwo, owned],
            [owned]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([child]);

        Assert.Single(result);
        Assert.DoesNotContain(result, item =>
            ReferenceEquals(item.TokenEvent.TokenEvent, burstOne.TokenEvent));
        Assert.DoesNotContain(result, item =>
            ReferenceEquals(item.TokenEvent.TokenEvent, burstTwo.TokenEvent));
    }

    [Fact]
    public void CreateCanonicalEvents_FallbackOwnedSuffix_IsIncluded()
    {
        var burst = Event("child", cumulative: CumulativeA, last: LastA);
        var owned = Event("child", cumulative: CumulativeB, last: LastB, minute: 20);
        var child = Resolved(
            "fallback.jsonl",
            "child",
            "missing",
            [burst, owned],
            [owned]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([child]));

        Assert.Same(owned.TokenEvent, retained.TokenEvent.TokenEvent);
    }

    [Fact]
    public void CreateCanonicalEvents_PreservedSubagentUsage_IsIncluded()
    {
        var usage = Event("subagent");
        var subagent = Resolved(
            "subagent.jsonl",
            "subagent",
            "parent",
            [usage],
            [usage],
            isSubagent: true);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([subagent]));

        Assert.Same(usage.TokenEvent, retained.TokenEvent.TokenEvent);
    }

    [Fact]
    public void CreateCanonicalEvents_StructurallyRemovedSubagentReplay_DoesNotReturn()
    {
        var replay = Event("subagent", cumulative: CumulativeA, last: LastA);
        var owned = Event("subagent", cumulative: CumulativeB, last: LastB);
        var subagent = Resolved(
            "subagent.jsonl",
            "subagent",
            "parent",
            [replay, owned],
            [owned],
            isSubagent: true);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([subagent]);

        Assert.Same(owned.TokenEvent, Assert.Single(result).TokenEvent.TokenEvent);
        Assert.DoesNotContain(result, item =>
            ReferenceEquals(item.TokenEvent.TokenEvent, replay.TokenEvent));
    }

    [Fact]
    public void CreateCanonicalEvents_DifferentSessionIds_PreserveBoth()
    {
        var first = Resolved("a.jsonl", "a", null, [Event("a")]);
        var second = Resolved("b.jsonl", "b", null, [Event("b")]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CreateCanonicalEvents_PostResolutionEpochDifference_PreservesBothStates()
    {
        var targetAtEpochZero = Event("session", cumulative: CumulativeA, last: LastA);
        var first = Resolved("a.jsonl", "session", null, [targetAtEpochZero]);
        var precedingHigherState = Event(
            "session",
            cumulative: CumulativeB,
            last: LastB,
            minute: 5);
        var targetAtEpochOne = Event(
            "session",
            cumulative: CumulativeA,
            last: LastA,
            epoch: 99,
            minute: 10);
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            [precedingHigherState, targetAtEpochOne]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        var targetEvents = result
            .Where(item => item.TokenEvent.TokenEvent.TokenCount.CumulativeUsageVector == CumulativeA)
            .ToArray();
        Assert.Equal(2, targetEvents.Length);
        Assert.Equal(
            new int?[] { 0, 1 },
            targetEvents.Select(item => item.CanonicalKey?.Epoch).Order().ToArray());
    }

    [Fact]
    public void CreateCanonicalEvents_DifferentCumulativeVectors_PreserveBoth()
    {
        var first = Resolved("a.jsonl", "session", null, [Event("session")]);
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            [Event("session", cumulative: CumulativeA with { CachedInputTokens = 21 })]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CreateCanonicalEvents_DifferentLastVectors_PreserveBoth()
    {
        var first = Resolved("a.jsonl", "session", null, [Event("session")]);
        var second = Resolved(
            "b.jsonl",
            "session",
            null,
            [Event("session", last: LastA with { ReasoningOutputTokens = 3 })]);

        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CreateCanonicalEvents_TimestampOnlyDifference_KeepsEarliest()
    {
        var later = Resolved("a.jsonl", "session", null, [Event("session", minute: 20)]);
        var earlier = Resolved("b.jsonl", "session", null, [Event("session", minute: 5)]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([later, earlier]));

        Assert.Equal(5, retained.TokenEvent.TokenEvent.TokenCount.Timestamp.Minute);
    }

    [Fact]
    public void CreateCanonicalEvents_EntryOnlyDifference_KeepsEarlierEntry()
    {
        var later = Resolved(
            "a.jsonl",
            "session",
            null,
            [Event("session", minute: 20, entry: new CodexUsageEntry(1, 2, 3, 4))]);
        var earlierEntry = new CodexUsageEntry(100, 200, 300, 400);
        var earlier = Resolved(
            "b.jsonl",
            "session",
            null,
            [Event("session", minute: 5, entry: earlierEntry)]);

        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([later, earlier]));

        Assert.Equal(earlierEntry, retained.TokenEvent.TokenEvent.TokenCount.Entry);
    }

    [Fact]
    public void CreateCanonicalEvents_PreservesDeterministicDeduplicatorOrdering()
    {
        var keyXFromA = Event("x", minute: 20);
        var keyYFromA = Event("y", minute: 15);
        var rolloutA = Resolved("a.jsonl", "rollout-a", null, [keyXFromA, keyYFromA]);
        var rolloutB = Resolved("b.jsonl", "z", null, [Event("z", minute: 12)]);
        var earlierKeyX = Event("x", minute: 5);
        var rolloutC = Resolved("c.jsonl", "rollout-c", null, [earlierKeyX]);

        var forward = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            [rolloutA, rolloutB, rolloutC]);
        var reverse = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            [rolloutC, rolloutB, rolloutA]);

        Assert.Equal(
            new[] { "c.jsonl", "a.jsonl", "b.jsonl" }.Select(Path.GetFullPath),
            forward.Select(item => item.FilePath));
        Assert.Equal(
            forward.Select(item => (item.FilePath, item.TokenEvent.TokenEvent.SessionId)),
            reverse.Select(item => (item.FilePath, item.TokenEvent.TokenEvent.SessionId)));
    }

    [Fact]
    public void CreateCanonicalEvents_EmptyResolvedPrimaryRollouts_ReturnsEmpty()
    {
        var result = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            Array.Empty<CodexInMemoryResolvedRollout>());

        Assert.Empty(result);
    }

    [Fact]
    public void CreateCanonicalEvents_TempJsonlPipeline_RemovesReplayThenCanonicalDuplicate()
    {
        var sessions = Path.Combine(_temporaryDirectory, "sessions");
        var archived = Path.Combine(_temporaryDirectory, "archived_sessions");
        var cutoff = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var old = cutoff.AddDays(-1);
        var recent = cutoff.AddDays(1);

        var parentPath = WriteJsonl(
            sessions,
            "parent.jsonl",
            old,
            SessionMeta("parent"),
            StateLine("2026-07-29T01:00:01.000Z", 100, 10, 100, 10));
        WriteJsonl(
            sessions,
            "child.jsonl",
            recent,
            SessionMeta("child", "parent"),
            StateLine("2026-07-30T01:00:01.000Z", 100, 10, 100, 10),
            StateLine("2026-07-30T01:00:03.000Z", 300, 30, 200, 20));
        WriteJsonl(
            archived,
            "child-copy.jsonl",
            recent,
            SessionMeta("child", "parent"),
            StateLine("2026-07-30T01:00:02.000Z", 100, 10, 100, 10),
            StateLine("2026-07-30T01:00:04.000Z", 300, 30, 200, 20));

        var pipelineResult = CodexLocalRolloutPipeline.LoadFromRoots(
            [sessions, archived],
            cutoff);
        var canonicalEvents =
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(pipelineResult);

        Assert.Equal([parentPath], pipelineResult.DependencyRollouts.Select(item => item.FilePath));
        Assert.Equal(2, pipelineResult.ResolvedPrimaryRollouts.Count);
        Assert.All(
            pipelineResult.ResolvedPrimaryRollouts,
            item => Assert.Single(item.ResolvedRollout.TokenEvents));
        var retained = Assert.Single(canonicalEvents);
        Assert.Equal(300, retained.TokenEvent.TokenEvent.TokenCount
            .CumulativeUsageVector?.InputTokens);
        Assert.Equal(3, retained.TokenEvent.TokenEvent.TokenCount.Timestamp.Second);
    }

    private static CodexLocalRolloutPipelineResult PipelineResult(
        IReadOnlyList<CodexInMemoryResolvedRollout> primaries,
        IReadOnlyList<CodexInMemoryResolvedRollout> dependencies)
    {
        var primaryRollouts = primaries.Select(item => item.OriginalRollout).ToArray();
        var dependencyRollouts = dependencies.Select(item => item.OriginalRollout).ToArray();
        var resolutionRollouts = primaryRollouts.Concat(dependencyRollouts).ToArray();
        var resolutionResults = primaries.Concat(dependencies).ToArray();
        var expansion = new CodexForkDependencyExpansion(
            primaryRollouts,
            dependencyRollouts,
            resolutionRollouts);

        return new CodexLocalRolloutPipelineResult(
            expansion,
            resolutionResults,
            primaries);
    }

    private static CodexInMemoryResolvedRollout Resolved(
        string path,
        string? sessionId,
        string? parentSessionId,
        IReadOnlyList<CodexEpochTokenEvent> resolvedEvents,
        bool isSubagent = false) =>
        Resolved(
            path,
            sessionId,
            parentSessionId,
            resolvedEvents,
            resolvedEvents,
            isSubagent);

    private static CodexInMemoryResolvedRollout Resolved(
        string path,
        string? sessionId,
        string? parentSessionId,
        IReadOnlyList<CodexEpochTokenEvent> originalEvents,
        IReadOnlyList<CodexEpochTokenEvent> resolvedEvents,
        bool isSubagent = false)
    {
        CodexSessionMetaParseResult? metadata = sessionId is null
            ? null
            : new CodexSessionMetaParseResult(
                sessionId,
                parentSessionId,
                isSubagent);
        var original = new CodexEpochRollout(
            Path.GetFullPath(path),
            metadata,
            originalEvents);
        var resolved = original with { TokenEvents = resolvedEvents };

        return new CodexInMemoryResolvedRollout(
            original,
            resolved,
            resolvedEvents,
            SelectedParent: null,
            ReplayCount: originalEvents.Count - resolvedEvents.Count);
    }

    private static CodexEpochTokenEvent Event(
        string? sessionId,
        int? epoch = 0,
        CodexUsageVector? cumulative = null,
        CodexUsageVector? last = null,
        CodexUsageEntry? entry = null,
        bool hasCumulative = true,
        int minute = 10)
    {
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, minute, 0, TimeSpan.Zero),
            entry ?? new CodexUsageEntry(80, 10, 20, 0),
            last ?? LastA,
            hasCumulative ? cumulative ?? CumulativeA : null);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            ParentSessionId: null,
            IsSubagent: false);

        return new CodexEpochTokenEvent(tokenEvent, epoch);
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
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-07-30T01:00:00.000Z\","
            + $"\"payload\":{{\"id\":\"{sessionId}\"{parentFields},\"thread_source\":\"user\"}}}}";
    }

    private static string StateLine(
        string timestamp,
        long cumulativeInput,
        long cumulativeOutput,
        long lastInput,
        long lastOutput) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
        + "\"total_token_usage\":{"
        + $"\"input_tokens\":{cumulativeInput},\"cached_input_tokens\":0,"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{cumulativeOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{cumulativeInput + cumulativeOutput}}},"
        + "\"last_token_usage\":{"
        + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":0,"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{lastOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{lastInput + lastOutput}}}}}}}}}";

    private static CodexUsageVector Vector(
        long input = 0,
        long cached = 0,
        long cacheWrite = 0,
        long output = 0,
        long reasoning = 0,
        long total = 0) =>
        new(input, cached, cacheWrite, output, reasoning, total);
}
