using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexPostResolutionEpochTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 30, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StructuralFork_ReplayDecreaseDoesNotContaminateOwnedEpoch()
    {
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, BaseTime, "parent"),
            Token(200, BaseTime.AddSeconds(2), "parent"));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Token(100, BaseTime, "child"),
            Token(200, BaseTime.AddSeconds(2), "child"),
            Token(50, BaseTime.AddSeconds(4), "child"),
            Token(70, BaseTime.AddSeconds(6), "child"));

        Assert.Equal(new int?[] { 0, 0, 1, 1 }, Epochs(child));

        var resolved = ResolveByPath([parent, child], child.FilePath);
        Assert.Equal(2, resolved.ReplayCount);
        Assert.Equal(new int?[] { 1, 1 }, Epochs(resolved.ResolvedRollout));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        Assert.Equal(new long[] { 50, 70 }, CumulativeInputs(canonical));
        Assert.Equal(new int?[] { 0, 0 }, CanonicalEpochs(canonical));
    }

    [Fact]
    public void FallbackFork_RapidReplayDecreaseDoesNotContaminateOwnedEpoch()
    {
        var child = Rollout(
            "fallback.jsonl",
            "child",
            "missing",
            Token(100, BaseTime, "child"),
            Token(200, BaseTime.AddMilliseconds(100), "child"),
            Token(50, BaseTime.AddSeconds(3), "child"),
            Token(70, BaseTime.AddSeconds(5), "child"));

        var resolved = ResolveByPath([child], child.FilePath);
        Assert.Equal(2, resolved.ReplayCount);
        Assert.Equal(new int?[] { 1, 1 }, Epochs(resolved.ResolvedRollout));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        Assert.Equal(new long[] { 50, 70 }, CumulativeInputs(canonical));
        Assert.Equal(new int?[] { 0, 0 }, CanonicalEpochs(canonical));
    }

    [Fact]
    public void StructuralFork_FirstOwnedStateBelowParentLastState_StartsAtEpochZero()
    {
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, BaseTime, "parent"),
            Token(200, BaseTime.AddSeconds(2), "parent"));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Token(100, BaseTime, "child"),
            Token(200, BaseTime.AddSeconds(2), "child"),
            Token(50, BaseTime.AddSeconds(4), "child"));

        var resolved = ResolveByPath([parent, child], child.FilePath);
        var retained = Assert.Single(
            CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]));

        Assert.Equal(50, CumulativeInput(retained));
        Assert.Equal(0, retained.CanonicalKey?.Epoch);
    }

    [Fact]
    public void StructuralFork_OwnedHistoryDecreaseStillIncrementsEpoch()
    {
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, BaseTime, "parent"),
            Token(200, BaseTime.AddSeconds(2), "parent"));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Token(100, BaseTime, "child"),
            Token(200, BaseTime.AddSeconds(2), "child"),
            Token(50, BaseTime.AddSeconds(4), "child"),
            Token(70, BaseTime.AddSeconds(6), "child"),
            Token(30, BaseTime.AddSeconds(8), "child"));

        var resolved = ResolveByPath([parent, child], child.FilePath);
        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        Assert.Equal(new long[] { 50, 70, 30 }, CumulativeInputs(canonical));
        Assert.Equal(new int?[] { 0, 0, 1 }, CanonicalEpochs(canonical));
    }

    [Fact]
    public void OrdinaryRollout_PreservesExistingEpochBehavior()
    {
        var ordinary = Rollout(
            "ordinary.jsonl",
            "ordinary",
            null,
            Token(100, BaseTime, "ordinary"),
            Token(200, BaseTime.AddSeconds(2), "ordinary"),
            Token(50, BaseTime.AddSeconds(4), "ordinary"));
        var resolved = ResolveByPath([ordinary], ordinary.FilePath);

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        Assert.Equal(new int?[] { 0, 0, 1 }, Epochs(ordinary));
        Assert.Equal(new int?[] { 0, 0, 1 }, CanonicalEpochs(canonical));
    }

    [Fact]
    public void ActiveArchiveOwnedCopies_RecomputedEpochsAllowCanonicalDedup()
    {
        var activeEvent = EpochEvent(Token(50, BaseTime, "child"), epoch: 0);
        var archivedEvent = EpochEvent(Token(50, BaseTime.AddSeconds(1), "child"), epoch: 7);
        var active = Resolved(
            EpochRollout(
                Path.Combine("sessions", "child.jsonl"),
                "child",
                "parent",
                activeEvent));
        var archived = Resolved(
            EpochRollout(
                Path.Combine("archived_sessions", "child.jsonl"),
                "child",
                "parent",
                archivedEvent));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            [active, archived]);

        var retained = Assert.Single(canonical);
        Assert.Equal(0, retained.CanonicalKey?.Epoch);
        Assert.Equal(BaseTime, retained.TokenEvent.TokenEvent.TokenCount.Timestamp);
    }

    [Fact]
    public void ForkCopiesWithDifferentReplayShapes_ShareOwnedCanonicalKey()
    {
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Token(100, BaseTime, "parent"),
            Token(200, BaseTime.AddSeconds(2), "parent"));
        var fullReplayCopy = Rollout(
            Path.Combine("sessions", "child.jsonl"),
            "child",
            "parent",
            Token(100, BaseTime, "child"),
            Token(200, BaseTime.AddSeconds(2), "child"),
            Token(50, BaseTime.AddSeconds(4), "child"));
        var shorterReplayCopy = Rollout(
            Path.Combine("archived_sessions", "child.jsonl"),
            "child",
            "parent",
            Token(100, BaseTime.AddSeconds(1), "child"),
            Token(50, BaseTime.AddSeconds(5), "child"));
        var resolved = CodexInMemoryForkResolver.Resolve(
            [parent, fullReplayCopy, shorterReplayCopy]);
        var primaryResults = resolved
            .Where(item => item.FilePath != parent.FilePath)
            .ToArray();

        Assert.Equal(new[] { 1, 2 }, primaryResults
            .OrderBy(item => item.ReplayCount)
            .Select(item => item.ReplayCount)
            .Order());

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(primaryResults);

        var retained = Assert.Single(canonical);
        Assert.Equal(50, CumulativeInput(retained));
        Assert.Equal(0, retained.CanonicalKey?.Epoch);
    }

    [Fact]
    public void ForkOfFork_AncestorReplayDoesNotAffectChildOwnedEpoch()
    {
        var ancestor = Rollout(
            "a.jsonl",
            "a",
            null,
            Token(100, BaseTime, "a"),
            Token(200, BaseTime.AddSeconds(2), "a"));
        var parent = Rollout(
            "b.jsonl",
            "b",
            "a",
            Token(100, BaseTime, "b"),
            Token(200, BaseTime.AddSeconds(2), "b"),
            Token(50, BaseTime.AddSeconds(4), "b"));
        var child = Rollout(
            "c.jsonl",
            "c",
            "b",
            Token(100, BaseTime, "c"),
            Token(200, BaseTime.AddSeconds(2), "c"),
            Token(50, BaseTime.AddSeconds(4), "c"),
            Token(20, BaseTime.AddSeconds(6), "c"),
            Token(30, BaseTime.AddSeconds(8), "c"));

        var resolved = ResolveByPath([ancestor, parent, child], child.FilePath);
        Assert.Equal(3, resolved.ReplayCount);
        Assert.Equal(new int?[] { 2, 2 }, Epochs(resolved.ResolvedRollout));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]);

        Assert.Equal(new long[] { 20, 30 }, CumulativeInputs(canonical));
        Assert.Equal(new int?[] { 0, 0 }, CanonicalEpochs(canonical));
    }

    [Fact]
    public void FallbackRemovingEntireChild_ProducesNoCanonicalEvents()
    {
        var child = Rollout(
            "fallback-empty.jsonl",
            "child",
            "missing",
            Token(100, BaseTime, "child"));
        var resolved = ResolveByPath([child], child.FilePath);

        Assert.Equal(1, resolved.ReplayCount);
        Assert.Empty(resolved.ResolvedRollout.TokenEvents);
        Assert.Empty(CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([resolved]));
    }

    [Fact]
    public void EventsWithoutOwnerOrCumulative_RemainKeylessAndPreserved()
    {
        var noOwnerToken = Token(100, BaseTime, sessionId: null);
        var noOwner = Resolved(EpochRollout(
            "no-owner.jsonl",
            sessionId: null,
            parentSessionId: null,
            EpochEvent(noOwnerToken, epoch: 9)));
        var noCumulativeToken = Token(
            cumulativeInput: null,
            timestamp: BaseTime.AddSeconds(1),
            sessionId: "session");
        var noCumulative = Resolved(EpochRollout(
            "no-cumulative.jsonl",
            "session",
            null,
            EpochEvent(noCumulativeToken, epoch: 9)));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            [noOwner, noCumulative]);

        Assert.Equal(2, canonical.Count);
        Assert.All(canonical, item => Assert.Null(item.CanonicalKey));
    }

    [Fact]
    public void ForkCanonicalOwnerUsesChildMetadataAcrossEventSessionChanges()
    {
        var first = EpochEvent(
            Token(50, BaseTime, sessionId: "embedded-parent"),
            epoch: 4);
        var second = EpochEvent(
            Token(30, BaseTime.AddSeconds(2), sessionId: "another-session"),
            epoch: 0);
        var child = Resolved(EpochRollout(
            "child.jsonl",
            "child",
            "parent",
            first,
            second));

        var canonical = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents([child]);

        Assert.Equal(new[] { "child", "child" },
            canonical.Select(item => item.CanonicalKey?.OwnerSessionId));
        Assert.Equal(new int?[] { 0, 1 }, CanonicalEpochs(canonical));
    }

    private static CodexEpochRollout Rollout(
        string path,
        string? sessionId,
        string? parentSessionId,
        params CodexRolloutTokenEvent[] tokenEvents)
    {
        var parsed = new CodexParsedRollout(
            Path.GetFullPath(path),
            Metadata(sessionId, parentSessionId),
            tokenEvents);
        return CodexCumulativeEpochAssigner.Assign(parsed);
    }

    private static CodexEpochRollout EpochRollout(
        string path,
        string? sessionId,
        string? parentSessionId,
        params CodexEpochTokenEvent[] tokenEvents) =>
        new(
            Path.GetFullPath(path),
            Metadata(sessionId, parentSessionId),
            tokenEvents);

    private static CodexSessionMetaParseResult? Metadata(
        string? sessionId,
        string? parentSessionId) =>
        sessionId is null
            ? null
            : new CodexSessionMetaParseResult(
                sessionId,
                parentSessionId,
                IsSubagent: false);

    private static CodexInMemoryResolvedRollout ResolveByPath(
        IEnumerable<CodexEpochRollout> rollouts,
        string path) =>
        Assert.Single(
            CodexInMemoryForkResolver.Resolve(rollouts),
            item => string.Equals(item.FilePath, path, StringComparison.Ordinal));

    private static CodexInMemoryResolvedRollout Resolved(CodexEpochRollout rollout) =>
        new(
            rollout,
            rollout,
            rollout.TokenEvents,
            SelectedParent: null,
            ReplayCount: 0);

    private static CodexRolloutTokenEvent Token(
        long? cumulativeInput,
        DateTimeOffset timestamp,
        string? sessionId)
    {
        CodexUsageVector? cumulative = cumulativeInput is long input
            ? Vector(input)
            : null;
        var last = Vector(cumulativeInput ?? 10);
        var tokenCount = new CodexTokenCountParseResult(
            timestamp,
            new CodexUsageEntry(cumulativeInput ?? 10, 0, 0, 0),
            last,
            cumulative);

        return new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            ParentSessionId: null,
            IsSubagent: false);
    }

    private static CodexEpochTokenEvent EpochEvent(
        CodexRolloutTokenEvent tokenEvent,
        int? epoch) =>
        new(tokenEvent, epoch);

    private static CodexUsageVector Vector(long input) =>
        new(
            InputTokens: input,
            CachedInputTokens: 0,
            CacheWriteInputTokens: 0,
            OutputTokens: 0,
            ReasoningOutputTokens: 0,
            TotalTokens: input);

    private static int?[] Epochs(CodexEpochRollout rollout) =>
        rollout.TokenEvents.Select(item => item.Epoch).ToArray();

    private static int?[] CanonicalEpochs(IEnumerable<CodexCanonicalEvent> events) =>
        events.Select(item => item.CanonicalKey?.Epoch).ToArray();

    private static long[] CumulativeInputs(IEnumerable<CodexCanonicalEvent> events) =>
        events.Select(CumulativeInput).ToArray();

    private static long CumulativeInput(CodexCanonicalEvent canonicalEvent) =>
        canonicalEvent.TokenEvent.TokenEvent.TokenCount
            .CumulativeUsageVector?.InputTokens ?? -1;
}
