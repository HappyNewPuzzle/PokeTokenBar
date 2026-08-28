using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexInMemoryForkResolverTests
{
    private static readonly UsageState StateA = State(100, 10);
    private static readonly UsageState StateB = State(300, 30);
    private static readonly UsageState StateC = State(600, 60);
    private static readonly UsageState StateD = State(1_000, 100);
    private static readonly UsageState StateE = State(1_500, 150);

    [Fact]
    public void Resolve_OrdinaryRollout_PreservesItUnchanged()
    {
        var rollout = Rollout("ordinary.jsonl", "ordinary", parentId: null, Event(StateA));

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([rollout]));

        Assert.Same(rollout, result.OriginalRollout);
        Assert.Same(rollout, result.ResolvedRollout);
        Assert.Null(result.SelectedParent);
        Assert.Equal(0, result.ReplayCount);
    }

    [Fact]
    public void Resolve_ParentAndForkChild_FindsParentAndTrimsReplay()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var owned = Event(StateB, second: 2);
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA), owned);

        var childResult = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Same(parent, childResult.SelectedParent);
        Assert.Equal(1, childResult.ReplayCount);
        Assert.Same(owned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_DifferentSessionIdRollout_IsNotAParentCandidate()
    {
        var wrongParent = Rollout("wrong.jsonl", "other", null, Event(StateA));
        var owned = Event(StateB, second: 3);
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA), owned);

        var childResult = ByPath(
            CodexInMemoryForkResolver.Resolve([wrongParent, child]),
            child.FilePath);

        Assert.Null(childResult.SelectedParent);
        Assert.Equal(1, childResult.ReplayCount);
        Assert.Same(owned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_MissingParent_UsesTimestampFallbackForManualFork()
    {
        var owned = Event(StateB, second: 3);
        var child = Rollout("child.jsonl", "child", "missing", Event(StateA), owned);

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([child]));

        Assert.Null(result.SelectedParent);
        Assert.Equal(1, result.ReplayCount);
        Assert.Same(owned, Assert.Single(result.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_MissingParentSubagent_PreservesRawUsageWithoutFallback()
    {
        var child = Rollout(
            "subagent.jsonl",
            "subagent",
            "missing",
            isSubagent: true,
            Event(StateA),
            Event(StateB));

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([child]));

        Assert.Equal(0, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Same(child, result.ResolvedRollout);
        Assert.Equal(2, result.ResolvedRollout.TokenEvents.Count);
    }

    [Fact]
    public void Resolve_ParentCandidateWithZeroPrefix_UsesFallbackForManualFork()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateD));
        var owned = Event(StateB, second: 3);
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA), owned);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Same(owned, Assert.Single(result.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_ParentCandidateWithZeroPrefix_SubagentPreservesRawUsage()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateD));
        var child = Rollout(
            "subagent.jsonl",
            "subagent",
            "parent",
            isSubagent: true,
            Event(StateA),
            Event(StateB));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(0, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Same(child, result.ResolvedRollout);
    }

    [Fact]
    public void Resolve_PositiveStructuralMatch_TakesPriorityOverLongerTimestampBurst()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var second = Event(StateB);
        var third = Event(StateC);
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            second,
            third);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(1, result.ReplayCount);
        Assert.Same(parent, result.SelectedParent);
        Assert.Equal([second, third], result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_MissingParentSingleEventManualFork_FallbackRemovesEvent()
    {
        var child = Rollout("child.jsonl", "child", "missing", Event(StateA));

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([child]));

        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Empty(result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_FallbackResult_PreservesOriginalAndUsesOwnedSuffixAsHistory()
    {
        var owned = Event(StateB, second: 3);
        var child = Rollout("child.jsonl", "child", "missing", Event(StateA), owned);

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([child]));

        Assert.Same(child, result.OriginalRollout);
        Assert.NotSame(child, result.ResolvedRollout);
        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Equal([owned], result.ResolvedRollout.TokenEvents);
        Assert.Equal([owned], result.ResolvedHistory);
        Assert.Same(owned, result.ResolvedHistory[0]);
    }

    [Fact]
    public void Resolve_ForkOfFallbackParent_UsesOnlyFallbackOwnedHistory()
    {
        var unmatchedAncestor = Rollout("a.jsonl", "a", null, Event(StateD));
        var bOwned = Event(StateB, second: 3);
        var fallbackParent = Rollout(
            "b.jsonl",
            "b",
            "a",
            Event(StateA),
            bOwned);
        var cOwned = Event(StateC, second: 4);
        var child = Rollout(
            "c.jsonl",
            "c",
            "b",
            Event(StateB),
            cOwned);

        var results = CodexInMemoryForkResolver.Resolve(
            [child, fallbackParent, unmatchedAncestor]);
        var parentResult = ByPath(results, fallbackParent.FilePath);
        var childResult = ByPath(results, child.FilePath);

        Assert.Equal(1, parentResult.ReplayCount);
        Assert.Null(parentResult.SelectedParent);
        Assert.Equal([bOwned], parentResult.ResolvedHistory);
        Assert.Equal(1, childResult.ReplayCount);
        Assert.Same(fallbackParent, childResult.SelectedParent);
        Assert.Same(cOwned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_FallbackSibling_DoesNotAffectStructuralSibling()
    {
        var parent = Rollout("a.jsonl", "a", null, Event(StateD));
        var fallbackOwned = Event(StateB, second: 3);
        var fallbackSibling = Rollout(
            "b.jsonl",
            "b",
            "a",
            Event(StateA),
            fallbackOwned);
        var structuralOwned = Event(StateC, second: 3);
        var structuralSibling = Rollout(
            "c.jsonl",
            "c",
            "a",
            Event(StateD),
            structuralOwned);

        var results = CodexInMemoryForkResolver.Resolve(
            [structuralSibling, fallbackSibling, parent]);
        var fallbackResult = ByPath(results, fallbackSibling.FilePath);
        var structuralResult = ByPath(results, structuralSibling.FilePath);

        Assert.Null(fallbackResult.SelectedParent);
        Assert.Same(fallbackOwned, Assert.Single(fallbackResult.ResolvedRollout.TokenEvents));
        Assert.Same(parent, structuralResult.SelectedParent);
        Assert.Same(structuralOwned, Assert.Single(structuralResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_MultipleParentCandidates_SelectsLongestPositivePrefix()
    {
        var shortMatch = Rollout(
            "a-parent.jsonl",
            "parent",
            null,
            Event(StateA),
            Event(StateD));
        var longMatch = Rollout(
            "z-parent.jsonl",
            "parent",
            null,
            Event(StateA),
            Event(StateB),
            Event(StateC));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            Event(StateB),
            Event(StateC),
            Event(StateD));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([child, shortMatch, longMatch]),
            child.FilePath);

        Assert.Same(longMatch, result.SelectedParent);
        Assert.Equal(3, result.ReplayCount);
    }

    [Fact]
    public void Resolve_EqualLongestMatches_SelectsFirstCandidateByOrdinalPath()
    {
        var firstByPath = Rollout(
            "a-parent.jsonl",
            "parent",
            null,
            Event(StateA),
            Event(StateB));
        var secondByPath = Rollout(
            "z-parent.jsonl",
            "parent",
            null,
            Event(StateA),
            Event(StateB));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            Event(StateB),
            Event(StateC));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([secondByPath, child, firstByPath]),
            child.FilePath);

        Assert.Same(firstByPath, result.SelectedParent);
        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Resolve_ReplayFollowedByNewEvent_KeepsNewEvent()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA), Event(StateB));
        var owned = Event(StateC, second: 3);
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            Event(StateB),
            owned);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Same(owned, Assert.Single(result.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_EntireChildReplay_AllowsEmptyResolvedEvents()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA), Event(StateB));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            Event(StateB));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(2, result.ReplayCount);
        Assert.Empty(result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_ForkOfFork_UsesParentsResolvedHistory()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA), Event(StateB));
        var bOwned = Event(StateC, second: 3);
        var middle = Rollout(
            "b.jsonl",
            "b",
            "a",
            Event(StateA),
            Event(StateB),
            bOwned);
        var cOwned = Event(StateD, second: 4);
        var child = Rollout(
            "c.jsonl",
            "c",
            "b",
            Event(StateA),
            Event(StateB),
            Event(StateC),
            cOwned);

        var results = CodexInMemoryForkResolver.Resolve([child, middle, ancestor]);
        var middleResult = ByPath(results, middle.FilePath);
        var childResult = ByPath(results, child.FilePath);

        Assert.Equal(2, middleResult.ReplayCount);
        Assert.Equal(3, childResult.ReplayCount);
        Assert.Same(middle, childResult.SelectedParent);
        Assert.Same(cOwned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_ForkOfFork_DoesNotCountAncestorReplayAsOwnedChildUsage()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA));
        var bOwned = Event(StateB, second: 2);
        var middle = Rollout("b.jsonl", "b", "a", Event(StateA), bOwned);
        var cOwned = Event(StateC, second: 3);
        var child = Rollout(
            "c.jsonl",
            "c",
            "b",
            Event(StateA),
            Event(StateB),
            cOwned);

        var results = CodexInMemoryForkResolver.Resolve([ancestor, middle, child]);
        var middleResult = ByPath(results, middle.FilePath);
        var childResult = ByPath(results, child.FilePath);

        Assert.Equal([bOwned], middleResult.ResolvedRollout.TokenEvents);
        Assert.Equal([cOwned], childResult.ResolvedRollout.TokenEvents);
        Assert.Equal(3, childResult.ResolvedHistory.Count);
    }

    [Fact]
    public void Resolve_SiblingForks_ResolveIndependentlyAgainstSameParent()
    {
        var parent = Rollout("a.jsonl", "a", null, Event(StateA));
        var bOwned = Event(StateB, second: 2);
        var siblingB = Rollout("b.jsonl", "b", "a", Event(StateA), bOwned);
        var cOwned = Event(StateC, second: 3);
        var siblingC = Rollout("c.jsonl", "c", "a", Event(StateA), cOwned);

        var results = CodexInMemoryForkResolver.Resolve([siblingC, parent, siblingB]);

        Assert.Equal([bOwned], ByPath(results, siblingB.FilePath).ResolvedRollout.TokenEvents);
        Assert.Equal([cOwned], ByPath(results, siblingC.FilePath).ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_SiblingHistory_DoesNotIncludeOtherSiblingsOwnedEvents()
    {
        var parent = Rollout("a.jsonl", "a", null, Event(StateA));
        var bOwned = Event(StateB, second: 2);
        var siblingB = Rollout("b.jsonl", "b", "a", Event(StateA), bOwned);
        var cOwned = Event(StateC, second: 3);
        var siblingC = Rollout("c.jsonl", "c", "a", Event(StateA), cOwned);

        var results = CodexInMemoryForkResolver.Resolve([parent, siblingB, siblingC]);
        var cHistory = ByPath(results, siblingC.FilePath).ResolvedHistory;

        Assert.Equal(2, cHistory.Count);
        Assert.DoesNotContain(bOwned, cHistory);
        Assert.Same(cOwned, cHistory[1]);
    }

    [Fact]
    public void Resolve_ChildBeforeParentInput_ProducesSameResolution()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var owned = Event(StateB, second: 2);
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA), owned);

        var parentFirst = CodexInMemoryForkResolver.Resolve([parent, child]);
        var childFirst = CodexInMemoryForkResolver.Resolve([child, parent]);

        Assert.Equal(
            ByPath(parentFirst, child.FilePath).ReplayCount,
            ByPath(childFirst, child.FilePath).ReplayCount);
        Assert.Same(
            owned,
            Assert.Single(ByPath(childFirst, child.FilePath).ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_Cycle_DoesNotRecurseForeverAndUsesFallback()
    {
        var rolloutA = Rollout("a.jsonl", "a", "b", Event(StateA));
        var rolloutB = Rollout("b.jsonl", "b", "a", Event(StateB));

        var results = CodexInMemoryForkResolver.Resolve([rolloutA, rolloutB]);

        Assert.All(results, result =>
        {
            Assert.Equal(1, result.ReplayCount);
            Assert.Null(result.SelectedParent);
            Assert.Empty(result.ResolvedRollout.TokenEvents);
            Assert.Empty(result.ResolvedHistory);
        });
    }

    [Fact]
    public void Resolve_SelfReference_ExcludesSameFileAndUsesFallback()
    {
        var rollout = Rollout("self.jsonl", "self", "self", Event(StateA));

        var result = Assert.Single(CodexInMemoryForkResolver.Resolve([rollout]));

        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Empty(result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_ParentHistoryWithMissingCumulative_IsNotSelected()
    {
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            Event(StateA),
            EventWithoutCumulative(second: 2));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            Event(StateB, second: 3));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Single(result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_ChildPrefixWithMissingCumulative_IsNotTrimmed()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA), Event(StateB));
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            EventWithoutCumulative(second: 3));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(1, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Single(result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_CodexForkFixturePattern_SelectsEightEventReplay()
    {
        var replayStates = ForkFixtureReplayStates();
        var parent = Rollout(
            "parent.jsonl",
            "parent",
            null,
            replayStates.Select(Event).ToArray());
        var firstOwned = Event(
            new UsageState(replayStates[^1].Cumulative, Vector(total: 6_742)),
            second: 40);
        var secondOwned = Event(State(338_321, 2_631), second: 41);
        var childEvents = replayStates.Select(Event).Concat([firstOwned, secondOwned]).ToArray();
        var child = Rollout("child.jsonl", "child", "parent", childEvents);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([child, parent]),
            child.FilePath);

        Assert.Equal(8, result.ReplayCount);
        Assert.Equal([firstOwned, secondOwned], result.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void Resolve_CodexSubagentFixturePattern_PreservesFirstOwnUsage()
    {
        var parentFirst = new UsageState(
            Vector(input: 22_858, output: 134, total: 22_992),
            Vector(input: 22_858, output: 134, total: 22_992));
        var childFirst = new UsageState(
            Vector(input: 22_939, cached: 21_248, output: 123, reasoning: 20, total: 23_062),
            Vector(input: 22_939, cached: 21_248, output: 123, reasoning: 20, total: 23_062));
        var parent = Rollout("parent.jsonl", "parent", null, Event(parentFirst));
        var firstChildUsage = Event(childFirst);
        var child = Rollout(
            "subagent.jsonl",
            "subagent",
            "parent",
            isSubagent: true,
            firstChildUsage,
            Event(State(46_155, 198), second: 2));

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(0, result.ReplayCount);
        Assert.Null(result.SelectedParent);
        Assert.Same(firstChildUsage, result.ResolvedRollout.TokenEvents[0]);
        Assert.Equal(2, result.ResolvedRollout.TokenEvents.Count);
    }

    [Fact]
    public void Resolve_SubagentWithExactReplay_UsesSamePositiveMatchRule()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var owned = Event(StateB, second: 2);
        var subagent = Rollout(
            "subagent.jsonl",
            "subagent",
            "parent",
            isSubagent: true,
            Event(StateA),
            owned);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, subagent]),
            subagent.FilePath);

        Assert.Equal(1, result.ReplayCount);
        Assert.Same(owned, Assert.Single(result.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Resolve_PreservesResolvedFilePathMetadataAndRemainingObjects()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var firstOwned = Event(StateB, second: 2);
        var secondOwned = Event(StateC, second: 3);
        var child = Rollout(
            "child.jsonl",
            "child",
            "parent",
            Event(StateA),
            firstOwned,
            secondOwned);

        var result = ByPath(
            CodexInMemoryForkResolver.Resolve([parent, child]),
            child.FilePath);

        Assert.Equal(child.FilePath, result.ResolvedRollout.FilePath);
        Assert.Same(child.RolloutMetadata, result.ResolvedRollout.RolloutMetadata);
        Assert.Equal([firstOwned, secondOwned], result.ResolvedRollout.TokenEvents);
        Assert.Same(firstOwned, result.ResolvedRollout.TokenEvents[0]);
        Assert.Same(secondOwned, result.ResolvedRollout.TokenEvents[1]);
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsEmptyResult()
    {
        Assert.Empty(CodexInMemoryForkResolver.Resolve([]));
    }

    [Fact]
    public void Resolve_ResultsAreOrderedByOrdinalFilePath()
    {
        var rolloutC = Rollout("c.jsonl", "c", null, Event(StateC));
        var rolloutA = Rollout("a.jsonl", "a", null, Event(StateA));
        var rolloutB = Rollout("b.jsonl", "b", null, Event(StateB));

        var result = CodexInMemoryForkResolver.Resolve([rolloutC, rolloutA, rolloutB]);

        Assert.Equal(
            new[] { "a.jsonl", "b.jsonl", "c.jsonl" }.Select(Path.GetFullPath),
            result.Select(item => item.FilePath));
    }

    private static CodexInMemoryResolvedRollout ByPath(
        IReadOnlyList<CodexInMemoryResolvedRollout> results,
        string path) =>
        Assert.Single(results, result =>
            string.Equals(result.FilePath, path, StringComparison.Ordinal));

    private static CodexEpochRollout Rollout(
        string path,
        string sessionId,
        string? parentId,
        params CodexEpochTokenEvent[] events) =>
        Rollout(path, sessionId, parentId, isSubagent: false, events);

    private static CodexEpochRollout Rollout(
        string path,
        string sessionId,
        string? parentId,
        bool isSubagent,
        params CodexEpochTokenEvent[] events) =>
        new(
            Path.GetFullPath(path),
            new CodexSessionMetaParseResult(sessionId, parentId, isSubagent),
            events);

    private static CodexEpochTokenEvent EventWithoutCumulative(int second) =>
        Event(new UsageState(Cumulative: null, Last: Vector(input: 10, total: 10)), second);

    private static CodexEpochTokenEvent Event(UsageState state, int second = 1)
    {
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
            new CodexUsageEntry(8, 3, 2, 0),
            state.Last,
            state.Cumulative);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            SessionId: "parent-session",
            ParentSessionId: null,
            IsSubagent: false);

        return new CodexEpochTokenEvent(tokenEvent, Epoch: 0);
    }

    private static UsageState[] ForkFixtureReplayStates() =>
    [
        FixtureState(19_828, 2_432, 279, 85, 20_107, 19_828, 2_432, 279, 85, 20_107),
        FixtureState(45_414, 21_760, 704, 292, 46_118, 25_586, 19_328, 425, 207, 26_011),
        FixtureState(71_724, 47_232, 1_014, 383, 72_738, 26_310, 25_472, 310, 91, 26_620),
        FixtureState(98_578, 73_216, 1_243, 513, 99_821, 26_854, 25_984, 229, 130, 27_083),
        FixtureState(144_233, 99_712, 1_591, 641, 145_824, 45_655, 26_496, 348, 128, 46_003),
        FixtureState(193_657, 145_152, 1_839, 673, 195_496, 49_424, 45_440, 248, 32, 49_672),
        FixtureState(251_724, 194_176, 2_180, 725, 253_904, 58_067, 49_024, 341, 52, 58_408),
        FixtureState(310_188, 251_904, 2_626, 854, 312_814, 58_464, 57_728, 446, 129, 58_910),
    ];

    private static UsageState FixtureState(
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
        new(
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

    private static UsageState State(long input, long output) =>
        new(
            Vector(input: input, output: output, total: input + output),
            Vector(input: input, output: output, total: input + output));

    private static CodexUsageVector Vector(
        long input = 0,
        long cached = 0,
        long cacheWrite = 0,
        long output = 0,
        long reasoning = 0,
        long total = 0) =>
        new(input, cached, cacheWrite, output, reasoning, total);

    private readonly record struct UsageState(
        CodexUsageVector? Cumulative,
        CodexUsageVector Last);
}
