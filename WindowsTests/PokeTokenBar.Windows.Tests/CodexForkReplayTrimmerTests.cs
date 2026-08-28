using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexForkReplayTrimmerTests
{
    private static readonly CodexUsageVector StateA = Vector(input: 100, output: 10, total: 110);
    private static readonly CodexUsageVector StateB = Vector(input: 300, output: 30, total: 330);
    private static readonly CodexUsageVector StateC = Vector(input: 600, output: 60, total: 660);
    private static readonly CodexUsageVector StateD = Vector(input: 1_000, output: 100, total: 1_100);

    [Fact]
    public void Trim_ChildWithoutReplay_PreservesChild()
    {
        var parent = Parent(Event(StateA));
        var child = Child(Event(StateB));

        var result = CodexForkReplayTrimmer.Trim(parent, child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_OneReplayedParentEvent_RemovesFirstChildEvent()
    {
        var replay = Event(StateA);
        var childOwned = Event(StateB, second: 2);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA)),
            Child(replay, childOwned));

        Assert.Equal(1, result.ReplayCount);
        Assert.Collection(
            result.TrimmedChild.TokenEvents,
            tokenEvent => Assert.Same(childOwned, tokenEvent));
    }

    [Fact]
    public void Trim_MultipleReplayedParentEvents_RemovesMatchingPrefix()
    {
        var childOwned = Event(StateD, second: 4);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateB), Event(StateC)),
            Child(Event(StateA), Event(StateB), Event(StateC), childOwned));

        Assert.Equal(3, result.ReplayCount);
        Assert.Same(childOwned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_ReplayFollowedByNewChildEvent_KeepsOnlyNewEvent()
    {
        var childOwned = Event(StateC, second: 3);
        var child = Child(Event(StateA), Event(StateB), childOwned);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateB)),
            child);

        Assert.Equal(2, result.ReplayCount);
        Assert.Same(childOwned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_EntireChildIsReplay_ReturnsEmptyChildEvents()
    {
        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateB), Event(StateC)),
            Child(Event(StateA), Event(StateB)));

        Assert.Equal(2, result.ReplayCount);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_SameCumulativeButDifferentLast_DoesNotMatch()
    {
        var parent = Parent(Event(StateA, last: Vector(input: 10, total: 10)));
        var child = Child(Event(StateA, last: Vector(input: 11, total: 11)));

        var result = CodexForkReplayTrimmer.Trim(parent, child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_SameLastButDifferentCumulative_DoesNotMatch()
    {
        var last = Vector(input: 10, total: 10);
        var parent = Parent(Event(StateA, last: last));
        var child = Child(Event(StateB, last: last));

        var result = CodexForkReplayTrimmer.Trim(parent, child);

        Assert.Equal(0, result.ReplayCount);
    }

    [Fact]
    public void Trim_SameScalarTotalsButDifferentVectorFields_DoesNotMatch()
    {
        var sameTotalDifferentFields = StateA with { InputTokens = 90, OutputTokens = 20 };

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA)),
            Child(Event(sameTotalDifferentFields)));

        Assert.Equal(StateA.TotalTokens, sameTotalDifferentFields.TotalTokens);
        Assert.Equal(0, result.ReplayCount);
    }

    [Fact]
    public void Trim_DifferentTimestampWithSameUsageState_Matches()
    {
        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA, second: 1)),
            Child(Event(StateA, second: 59)));

        Assert.Equal(1, result.ReplayCount);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_DifferentEntryWithSameUsageState_Matches()
    {
        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA, entry: new CodexUsageEntry(1, 2, 3, 4))),
            Child(Event(StateA, entry: new CodexUsageEntry(100, 200, 300, 400))));

        Assert.Equal(1, result.ReplayCount);
    }

    [Fact]
    public void Trim_EventParentAndSubagentDifferences_DoNotAffectUsageStateMatch()
    {
        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(
                StateA,
                eventParentSessionId: "other-parent",
                isSubagent: false)),
            Child(Event(
                StateA,
                eventParentSessionId: "parent-session",
                isSubagent: true)));

        Assert.Equal(1, result.ReplayCount);
    }

    [Fact]
    public void Trim_StopsAtFirstPrefixMismatch()
    {
        var firstChild = Event(StateA);
        var mismatch = Event(StateD, second: 2);
        var laterMatch = Event(StateC, second: 3);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateB), Event(StateC)),
            Child(firstChild, mismatch, laterMatch));

        Assert.Equal(1, result.ReplayCount);
        Assert.Equal([mismatch, laterMatch], result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_DoesNotSearchParentSuffixOrInternalHistory()
    {
        var child = Child(Event(StateA), Event(StateB));

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateD), Event(StateA), Event(StateB)),
            child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_RepeatedStates_UsesLongestExactCommonPrefix()
    {
        var childOwned = Event(StateD, second: 4);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateA), Event(StateA), Event(StateB)),
            Child(Event(StateA), Event(StateA), Event(StateA), childOwned));

        Assert.Equal(3, result.ReplayCount);
        Assert.Same(childOwned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_DifferentEpochsWithSameUsageState_StillMatch()
    {
        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA, epoch: 7)),
            Child(Event(StateA, epoch: 42)));

        Assert.Equal(1, result.ReplayCount);
    }

    [Fact]
    public void Trim_EmptyParent_PreservesChild()
    {
        var child = Child(Event(StateA));

        var result = CodexForkReplayTrimmer.Trim(Parent(), child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_EmptyChild_ReturnsEmptyChild()
    {
        var child = Child();

        var result = CodexForkReplayTrimmer.Trim(Parent(Event(StateA)), child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_MissingCumulativeInsideComparableRange_InvalidatesWholeMatch()
    {
        var child = Child(
            Event(StateA),
            EventWithoutCumulative(second: 2),
            Event(StateC, second: 3));

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA), Event(StateB), Event(StateC)),
            child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_ChildWithoutParentMetadata_ThrowsArgumentException()
    {
        var child = Rollout(
            "child.jsonl",
            sessionId: "child-session",
            parentSessionId: null,
            Event(StateA));

        Assert.Throws<ArgumentException>(() =>
            CodexForkReplayTrimmer.Trim(Parent(Event(StateA)), child));
    }

    [Fact]
    public void Trim_MismatchedParentSession_ThrowsArgumentException()
    {
        var wrongParent = Rollout(
            "wrong-parent.jsonl",
            sessionId: "wrong-session",
            parentSessionId: null,
            Event(StateA));

        Assert.Throws<ArgumentException>(() =>
            CodexForkReplayTrimmer.Trim(wrongParent, Child(Event(StateA))));
    }

    [Fact]
    public void Trim_SameParentAndChildFile_ThrowsArgumentException()
    {
        var parent = Parent(Event(StateA));
        var child = Child(Event(StateA)) with { FilePath = parent.FilePath };

        Assert.Throws<ArgumentException>(() =>
            CodexForkReplayTrimmer.Trim(parent, child));
    }

    [Fact]
    public void Trim_PreservesChildFilePathAndRolloutMetadata()
    {
        var child = Child(Event(StateA), Event(StateB));

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA)),
            child);

        Assert.Equal(child.FilePath, result.TrimmedChild.FilePath);
        Assert.Same(child.RolloutMetadata, result.TrimmedChild.RolloutMetadata);
    }

    [Fact]
    public void Trim_PreservesRemainingEventOrderAndObjects()
    {
        var firstOwned = Event(StateB, second: 2);
        var secondOwned = Event(StateC, second: 3);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(Event(StateA)),
            Child(Event(StateA), firstOwned, secondOwned));

        Assert.Equal([firstOwned, secondOwned], result.TrimmedChild.TokenEvents);
        Assert.Same(firstOwned, result.TrimmedChild.TokenEvents[0]);
        Assert.Same(secondOwned, result.TrimmedChild.TokenEvents[1]);
    }

    [Fact]
    public void Trim_CodexForkFixturePattern_TrimsEightReplayedStatesBeforeDivergence()
    {
        var replayStates = new[]
        {
            State(19_828, 2_432, 279, 85, 20_107, 19_828, 2_432, 279, 85, 20_107),
            State(45_414, 21_760, 704, 292, 46_118, 25_586, 19_328, 425, 207, 26_011),
            State(71_724, 47_232, 1_014, 383, 72_738, 26_310, 25_472, 310, 91, 26_620),
            State(98_578, 73_216, 1_243, 513, 99_821, 26_854, 25_984, 229, 130, 27_083),
            State(144_233, 99_712, 1_591, 641, 145_824, 45_655, 26_496, 348, 128, 46_003),
            State(193_657, 145_152, 1_839, 673, 195_496, 49_424, 45_440, 248, 32, 49_672),
            State(251_724, 194_176, 2_180, 725, 253_904, 58_067, 49_024, 341, 52, 58_408),
            State(310_188, 251_904, 2_626, 854, 312_814, 58_464, 57_728, 446, 129, 58_910),
        };
        var parentEvents = replayStates
            .Select((state, index) => Event(state.Cumulative, state.Last, second: index + 1))
            .ToArray();
        var childReplay = replayStates
            .Select((state, index) => Event(state.Cumulative, state.Last, second: index + 20))
            .ToList();
        var divergent = Event(
            replayStates[^1].Cumulative,
            last: Vector(total: 6_742),
            second: 40);
        var childOwned = Event(
            Vector(input: 338_321, cached: 251_904, output: 2_631, reasoning: 854, total: 340_952),
            last: Vector(input: 28_133, output: 5, total: 28_138),
            second: 41);
        childReplay.Add(divergent);
        childReplay.Add(childOwned);

        var result = CodexForkReplayTrimmer.Trim(
            Parent(parentEvents),
            Child(childReplay.ToArray()));

        Assert.Equal(8, result.ReplayCount);
        Assert.Equal([divergent, childOwned], result.TrimmedChild.TokenEvents);
    }

    private static CodexEpochRollout Parent(params CodexEpochTokenEvent[] events) =>
        Rollout(
            "parent.jsonl",
            sessionId: "parent-session",
            parentSessionId: null,
            events);

    private static CodexEpochRollout Child(params CodexEpochTokenEvent[] events) =>
        Rollout(
            "child.jsonl",
            sessionId: "child-session",
            parentSessionId: "parent-session",
            events);

    private static CodexEpochRollout Rollout(
        string path,
        string sessionId,
        string? parentSessionId,
        params CodexEpochTokenEvent[] events) =>
        new(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult(sessionId, parentSessionId, IsSubagent: false),
            events);

    private static CodexEpochTokenEvent EventWithoutCumulative(int second = 1) =>
        Event(cumulative: null, last: Vector(input: 10, total: 10), second: second);

    private static CodexEpochTokenEvent Event(
        CodexUsageVector? cumulative,
        CodexUsageVector? last = null,
        int? epoch = 0,
        CodexUsageEntry? entry = null,
        string? eventParentSessionId = null,
        bool isSubagent = false,
        int second = 1)
    {
        last ??= cumulative ?? Vector(input: 10, total: 10);
        entry ??= new CodexUsageEntry(8, 3, 2, 0);

        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
            entry.Value,
            last.Value,
            cumulative);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            SessionId: "parent-session",
            eventParentSessionId,
            isSubagent);

        return new CodexEpochTokenEvent(tokenEvent, epoch);
    }

    private static (CodexUsageVector Cumulative, CodexUsageVector Last) State(
        long cumulativeInput,
        long cumulativeCached,
        long cumulativeOutput,
        long cumulativeReasoning,
        long cumulativeTotal,
        long lastInput,
        long lastCached,
        long lastOutput,
        long lastReasoning,
        long lastTotal) =>
        (
            Vector(
                cumulativeInput,
                cumulativeCached,
                output: cumulativeOutput,
                reasoning: cumulativeReasoning,
                total: cumulativeTotal),
            Vector(
                lastInput,
                lastCached,
                output: lastOutput,
                reasoning: lastReasoning,
                total: lastTotal));

    private static CodexUsageVector Vector(
        long input = 0,
        long cached = 0,
        long cacheWrite = 0,
        long output = 0,
        long reasoning = 0,
        long total = 0) =>
        new(input, cached, cacheWrite, output, reasoning, total);
}
