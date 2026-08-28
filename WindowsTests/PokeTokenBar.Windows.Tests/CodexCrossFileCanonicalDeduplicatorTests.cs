using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexCrossFileCanonicalDeduplicatorTests
{
    private static readonly CodexUsageVector Cumulative =
        Vector(input: 100, cached: 20, cacheWrite: 3, output: 30, reasoning: 5, total: 130);

    private static readonly CodexUsageVector Last =
        Vector(input: 10, cached: 2, cacheWrite: 1, output: 3, reasoning: 1, total: 13);

    [Fact]
    public void Deduplicate_OneRolloutWithOneEvent_PreservesEvent()
    {
        var tokenEvent = Event("session-a");
        var rollout = Rollout("a.jsonl", tokenEvent);

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([rollout]);

        var retained = Assert.Single(result);
        Assert.Same(rollout, retained.SourceRollout);
        Assert.Same(tokenEvent, retained.TokenEvent);
        Assert.NotNull(retained.CanonicalKey);
    }

    [Fact]
    public void Deduplicate_DifferentCanonicalKeys_PreservesAllEvents()
    {
        var first = Rollout("a.jsonl", Event("session-a"));
        var second = Rollout(
            "b.jsonl",
            Event("session-a", cumulative: Cumulative with { InputTokens = 101 }));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_SameCanonicalKeyAcrossTwoRollouts_KeepsOneEvent()
    {
        var first = Rollout("a.jsonl", Event("session-a"));
        var second = Rollout("b.jsonl", Event("session-a"));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_SecondFileHasEarlierTimestamp_KeepsSecondFileEvent()
    {
        var later = Rollout("a.jsonl", Event("session-a", minute: 10));
        var earlier = Rollout("b.jsonl", Event("session-a", minute: 5));

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([later, earlier]));

        Assert.Same(earlier, retained.SourceRollout);
        Assert.Equal(5, retained.TokenEvent.TokenEvent.TokenCount.Timestamp.Minute);
    }

    [Fact]
    public void Deduplicate_SameKeyThreeTimes_KeepsEarliestEvent()
    {
        var middle = Rollout("a.jsonl", Event("session-a", minute: 10));
        var earliest = Rollout("b.jsonl", Event("session-a", minute: 5));
        var latest = Rollout("c.jsonl", Event("session-a", minute: 30));

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([latest, earliest, middle]));

        Assert.Same(earliest, retained.SourceRollout);
    }

    [Fact]
    public void Deduplicate_SameKeyAndTimestamp_KeepsFirstInSortedFileOrder()
    {
        var firstByPath = Rollout("a.jsonl", Event("session-a", minute: 5));
        var secondByPath = Rollout("z.jsonl", Event("session-a", minute: 5));

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([secondByPath, firstByPath]));

        Assert.Same(firstByPath, retained.SourceRollout);
    }

    [Fact]
    public void Deduplicate_DifferentSessionIds_PreservesBothEvents()
    {
        var first = Rollout("a.jsonl", Event("session-a"), rolloutSessionId: "session-a");
        var second = Rollout("b.jsonl", Event("session-b"), rolloutSessionId: "session-b");

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_DifferentEpochs_PreservesBothEvents()
    {
        var first = Rollout("a.jsonl", Event("session-a", epoch: 0));
        var second = Rollout("b.jsonl", Event("session-a", epoch: 1));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_DifferentCumulativeField_PreservesBothEvents()
    {
        var first = Rollout("a.jsonl", Event("session-a"));
        var second = Rollout(
            "b.jsonl",
            Event("session-a", cumulative: Cumulative with { CachedInputTokens = 21 }));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_DifferentLastUsageVector_PreservesBothEvents()
    {
        var first = Rollout("a.jsonl", Event("session-a"));
        var second = Rollout(
            "b.jsonl",
            Event("session-a", last: Last with { OutputTokens = 4 }));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_DifferentTimestampOnly_KeepsEarliestEvent()
    {
        var later = Rollout("a.jsonl", Event("session-a", minute: 20));
        var earlier = Rollout("b.jsonl", Event("session-a", minute: 10));

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([later, earlier]));

        Assert.Same(earlier, retained.SourceRollout);
    }

    [Fact]
    public void Deduplicate_DifferentEntryOnly_UsesSameKeyAndKeepsEarliestEvent()
    {
        var later = Rollout(
            "a.jsonl",
            Event(
                "session-a",
                entry: new CodexUsageEntry(1, 2, 3, 4),
                minute: 20));
        var earlier = Rollout(
            "b.jsonl",
            Event(
                "session-a",
                entry: new CodexUsageEntry(100, 200, 300, 400),
                minute: 10));

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([later, earlier]));

        Assert.Same(earlier, retained.SourceRollout);
        Assert.Equal(100, retained.TokenEvent.TokenEvent.TokenCount.Entry.InputTokens);
    }

    [Fact]
    public void Deduplicate_DifferentEventParentOnly_UsesSameCanonicalKey()
    {
        var first = Rollout(
            "a.jsonl",
            Event("session-a", parentSessionId: "parent-a"));
        var second = Rollout(
            "b.jsonl",
            Event("session-a", parentSessionId: "parent-b"));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_DifferentSubagentFlagOnly_UsesSameCanonicalKey()
    {
        var first = Rollout("a.jsonl", Event("session-a", isSubagent: false));
        var second = Rollout("b.jsonl", Event("session-a", isSubagent: true));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_EventsWithoutCanonicalKeys_ArePreserved()
    {
        var noOwner = Rollout(
            "a.jsonl",
            Event(sessionId: null),
            rolloutSessionId: null);
        var noEpoch = Rollout("b.jsonl", Event("session-a", epoch: null));
        var noCumulative = Rollout(
            "c.jsonl",
            Event("session-a", hasCumulative: false));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate(
            [noOwner, noEpoch, noCumulative]);

        Assert.Equal(3, result.Count);
        Assert.All(result, item => Assert.Null(item.CanonicalKey));
    }

    [Fact]
    public void Deduplicate_IdenticalEventsWithoutCanonicalKeys_AreNotCollapsed()
    {
        var first = Rollout("a.jsonl", Event("session-a", epoch: null));
        var second = Rollout("b.jsonl", Event("session-a", epoch: null));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_ActiveAndArchivedPathsWithSameKey_CollapseToOneEvent()
    {
        var active = Rollout(
            Path.Combine("home", ".codex", "sessions", "rollout.jsonl"),
            Event("session-a"));
        var archived = Rollout(
            Path.Combine("home", ".codex", "archived_sessions", "rollout.jsonl"),
            Event("session-a"));

        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([active, archived]);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_SelectedEventPreservesSourceContext()
    {
        var later = Rollout(
            "a.jsonl",
            Event("session-a", minute: 20),
            rolloutSessionId: "session-a",
            rolloutParentSessionId: "parent-later",
            isSubagent: false);
        var earlier = Rollout(
            "b.jsonl",
            Event("session-a", minute: 10),
            rolloutSessionId: "session-a",
            rolloutParentSessionId: "parent-earlier",
            isSubagent: true);

        var retained = Assert.Single(
            CodexCrossFileCanonicalDeduplicator.Deduplicate([later, earlier]));

        Assert.Same(earlier, retained.SourceRollout);
        Assert.Equal(earlier.FilePath, retained.FilePath);
        Assert.Same(earlier.RolloutMetadata, retained.RolloutMetadata);
        Assert.Equal("parent-earlier", retained.RolloutMetadata?.ParentSessionId);
        Assert.True(retained.RolloutMetadata?.IsSubagent);
    }

    [Fact]
    public void Deduplicate_EmptyInput_ReturnsEmptyResult()
    {
        var result = CodexCrossFileCanonicalDeduplicator.Deduplicate([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Deduplicate_MixedRollouts_ProducesDeterministicFirstKeyOrder()
    {
        var keyXFromA = Event("session-x", minute: 20);
        var keyYFromA = Event("session-y", minute: 15);
        var rolloutA = Rollout(
            "a.jsonl",
            [keyXFromA, keyYFromA],
            rolloutSessionId: "rollout-a");
        var rolloutB = Rollout(
            "b.jsonl",
            Event("session-z", minute: 12),
            rolloutSessionId: "session-z");
        var earlierKeyXFromC = Event("session-x", minute: 5);
        var rolloutC = Rollout(
            "c.jsonl",
            earlierKeyXFromC,
            rolloutSessionId: "rollout-c");

        var forward = CodexCrossFileCanonicalDeduplicator.Deduplicate(
            [rolloutA, rolloutB, rolloutC]);
        var reverse = CodexCrossFileCanonicalDeduplicator.Deduplicate(
            [rolloutC, rolloutB, rolloutA]);

        Assert.Equal(
            new[] { "c.jsonl", "a.jsonl", "b.jsonl" }.Select(Path.GetFullPath),
            forward.Select(item => item.FilePath));
        Assert.Equal(
            forward.Select(item => (item.FilePath, item.TokenEvent.TokenEvent.SessionId)),
            reverse.Select(item => (item.FilePath, item.TokenEvent.TokenEvent.SessionId)));
    }

    private static CodexEpochRollout Rollout(
        string relativePath,
        CodexEpochTokenEvent tokenEvent,
        string? rolloutSessionId = "session-a",
        string? rolloutParentSessionId = null,
        bool isSubagent = false) =>
        Rollout(
            relativePath,
            [tokenEvent],
            rolloutSessionId,
            rolloutParentSessionId,
            isSubagent);

    private static CodexEpochRollout Rollout(
        string relativePath,
        IReadOnlyList<CodexEpochTokenEvent> tokenEvents,
        string? rolloutSessionId,
        string? rolloutParentSessionId = null,
        bool isSubagent = false)
    {
        CodexSessionMetaParseResult? metadata = rolloutSessionId is null
            ? null
            : new CodexSessionMetaParseResult(
                rolloutSessionId,
                rolloutParentSessionId,
                isSubagent);

        return new CodexEpochRollout(
            Path.GetFullPath(relativePath),
            metadata,
            tokenEvents);
    }

    private static CodexEpochTokenEvent Event(
        string? sessionId,
        int? epoch = 0,
        CodexUsageVector? cumulative = null,
        CodexUsageVector? last = null,
        CodexUsageEntry? entry = null,
        string? parentSessionId = null,
        bool isSubagent = false,
        bool hasCumulative = true,
        int minute = 10)
    {
        last ??= Last;
        entry ??= new CodexUsageEntry(8, 3, 2, 0);

        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, minute, 0, TimeSpan.Zero),
            entry.Value,
            last.Value,
            hasCumulative ? cumulative ?? Cumulative : null);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            parentSessionId,
            isSubagent);

        return new CodexEpochTokenEvent(tokenEvent, epoch);
    }

    private static CodexUsageVector Vector(
        long input = 0,
        long cached = 0,
        long cacheWrite = 0,
        long output = 0,
        long reasoning = 0,
        long total = 0) =>
        new(input, cached, cacheWrite, output, reasoning, total);
}
