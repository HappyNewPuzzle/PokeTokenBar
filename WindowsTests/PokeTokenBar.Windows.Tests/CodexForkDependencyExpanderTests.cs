using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexForkDependencyExpanderTests
{
    private static readonly UsageState StateA = State(100, 10);
    private static readonly UsageState StateB = State(300, 30);
    private static readonly UsageState StateC = State(600, 60);

    [Fact]
    public void Expand_PrimaryWithoutParent_HasNoDependencies()
    {
        var primary = Rollout("primary.jsonl", "primary", null, Event(StateA));

        var result = CodexForkDependencyExpander.Expand([primary], []);

        Assert.Equal([primary], result.PrimaryRollouts);
        Assert.Empty(result.DependencyRollouts);
        Assert.Equal([primary], result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_PrimaryChild_AddsAvailableParentAsDependency()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([child], [parent]);

        Assert.Equal([child], result.PrimaryRollouts);
        Assert.Equal([parent], result.DependencyRollouts);
        Assert.Equal(
            OrderByPath(parent, child),
            result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_ParentAlreadyInPrimary_DoesNotAddDependency()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [child, parent],
            [parent]);

        Assert.Empty(result.DependencyRollouts);
        Assert.Equal(OrderByPath(parent, child), result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_ForkOfFork_LoadsParentAndAncestorRecursively()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA));
        var parent = Rollout("b.jsonl", "b", "a", Event(StateA), Event(StateB));
        var child = Rollout("c.jsonl", "c", "b", Event(StateA), Event(StateB), Event(StateC));

        var result = CodexForkDependencyExpander.Expand(
            [child],
            [child, parent, ancestor]);

        Assert.Equal([child], result.PrimaryRollouts);
        Assert.Equal(OrderByPath(ancestor, parent), result.DependencyRollouts);
        Assert.Equal(OrderByPath(ancestor, parent, child), result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_DependencyParentIsAlsoExpanded()
    {
        var root = Rollout("root.jsonl", "root", null, Event(StateA));
        var middle = Rollout("middle.jsonl", "middle", "root", Event(StateA));
        var child = Rollout("child.jsonl", "child", "middle", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [child],
            [root, middle]);

        Assert.Contains(root, result.DependencyRollouts);
        Assert.Contains(middle, result.DependencyRollouts);
    }

    [Fact]
    public void Expand_MultipleAvailableCandidatesForMissingSession_PreservesAllFiles()
    {
        var first = Rollout("parent-1.jsonl", "parent", null, Event(StateA));
        var second = Rollout("parent-2.jsonl", "parent", null, Event(StateB));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [child],
            [second, first]);

        Assert.Equal(OrderByPath(first, second), result.DependencyRollouts);
    }

    [Fact]
    public void Expand_SameSessionDifferentPaths_AreNotCollapsed()
    {
        var first = Rollout("first.jsonl", "parent", null, Event(StateA));
        var second = Rollout("second.jsonl", "parent", null, Event(StateA));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([child], [first, second]);

        Assert.Equal(2, result.DependencyRollouts.Count);
        Assert.Equal(2, result.DependencyRollouts.Select(item => item.FilePath).Distinct().Count());
    }

    [Fact]
    public void Expand_PrimaryCandidateForSession_PreventsAdditionalAvailableCandidates()
    {
        var primaryParent = Rollout("primary-parent.jsonl", "parent", null, Event(StateA));
        var oldAlternative = Rollout("old-parent.jsonl", "parent", null, Event(StateB));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [primaryParent, child],
            [oldAlternative]);

        Assert.Empty(result.DependencyRollouts);
        Assert.DoesNotContain(oldAlternative, result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_IdenticalPathInPrimaryAndAvailable_IsIncludedOnceAsPrimary()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [parent, child],
            [parent]);

        Assert.Empty(result.DependencyRollouts);
        Assert.Single(result.ResolutionRollouts, item => item.FilePath == parent.FilePath);
        Assert.Same(parent, result.ResolutionRollouts.Single(item => item.FilePath == parent.FilePath));
    }

    [Fact]
    public void Expand_MissingParentInAvailable_RemainsUnresolvedWithoutError()
    {
        var child = Rollout("child.jsonl", "child", "missing", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([child], []);

        Assert.Empty(result.DependencyRollouts);
        Assert.Equal([child], result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_DependencyCycle_TerminatesAndKeepsBothRollouts()
    {
        var a = Rollout("a.jsonl", "a", "b", Event(StateA));
        var b = Rollout("b.jsonl", "b", "a", Event(StateB));

        var result = CodexForkDependencyExpander.Expand([a], [a, b]);

        Assert.Equal([b], result.DependencyRollouts);
        Assert.Equal(OrderByPath(a, b), result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_SelfParent_TerminatesWithoutAddingDependency()
    {
        var self = Rollout("self.jsonl", "self", "self", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([self], [self]);

        Assert.Empty(result.DependencyRollouts);
        Assert.Equal([self], result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_SubagentParent_IsLoadedAsDependency()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var subagent = Rollout(
            "subagent.jsonl",
            "subagent",
            "parent",
            isSubagent: true,
            Event(StateB));

        var result = CodexForkDependencyExpander.Expand([subagent], [parent]);

        Assert.Equal([parent], result.DependencyRollouts);
    }

    [Fact]
    public void Expand_OrdinaryAvailableRolloutIsNotLoadedWithoutReference()
    {
        var primary = Rollout("primary.jsonl", "primary", null, Event(StateA));
        var unrelated = Rollout("unrelated.jsonl", "unrelated", null, Event(StateB));

        var result = CodexForkDependencyExpander.Expand([primary], [unrelated]);

        Assert.Empty(result.DependencyRollouts);
        Assert.DoesNotContain(unrelated, result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_MultiplePrimaryChildrenSharingParent_AddsParentOnce()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var firstChild = Rollout("child-1.jsonl", "child-1", "parent", Event(StateA));
        var secondChild = Rollout("child-2.jsonl", "child-2", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [firstChild, secondChild],
            [parent]);

        Assert.Equal([parent], result.DependencyRollouts);
    }

    [Fact]
    public void Expand_PrimaryChildrenWithDifferentParents_AddsEveryParent()
    {
        var firstParent = Rollout("parent-1.jsonl", "parent-1", null, Event(StateA));
        var secondParent = Rollout("parent-2.jsonl", "parent-2", null, Event(StateB));
        var firstChild = Rollout("child-1.jsonl", "child-1", "parent-1", Event(StateA));
        var secondChild = Rollout("child-2.jsonl", "child-2", "parent-2", Event(StateB));

        var result = CodexForkDependencyExpander.Expand(
            [firstChild, secondChild],
            [firstParent, secondParent]);

        Assert.Equal(OrderByPath(firstParent, secondParent), result.DependencyRollouts);
    }

    [Fact]
    public void Expand_InputOrderDoesNotChangeSelectedPaths()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA));
        var parent = Rollout("b.jsonl", "b", "a", Event(StateB));
        var firstChild = Rollout("c.jsonl", "c", "b", Event(StateC));
        var secondChild = Rollout("d.jsonl", "d", "b", Event(StateC));

        var forward = CodexForkDependencyExpander.Expand(
            [firstChild, secondChild],
            [ancestor, parent]);
        var reverse = CodexForkDependencyExpander.Expand(
            [secondChild, firstChild],
            [parent, ancestor]);

        Assert.Equal(Paths(forward.PrimaryRollouts), Paths(reverse.PrimaryRollouts));
        Assert.Equal(Paths(forward.DependencyRollouts), Paths(reverse.DependencyRollouts));
        Assert.Equal(Paths(forward.ResolutionRollouts), Paths(reverse.ResolutionRollouts));
    }

    [Fact]
    public void Expand_PrimaryRolloutsAreOrderedByOrdinalFilePath()
    {
        var upper = Rollout("A.jsonl", "upper", null, Event(StateA));
        var lower = Rollout("a.jsonl", "lower", null, Event(StateB));
        var middle = Rollout("b.jsonl", "middle", null, Event(StateC));

        var result = CodexForkDependencyExpander.Expand([middle, lower, upper], []);

        Assert.Equal(OrderByPath(upper, lower, middle), result.PrimaryRollouts);
    }

    [Fact]
    public void Expand_DependencyRolloutsAreOrderedByOrdinalFilePath()
    {
        var parentA = Rollout("A.jsonl", "parent", null, Event(StateA));
        var parentLowerA = Rollout("a.jsonl", "parent", null, Event(StateB));
        var parentB = Rollout("b.jsonl", "parent", null, Event(StateC));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand(
            [child],
            [parentB, parentLowerA, parentA]);

        Assert.Equal(OrderByPath(parentA, parentLowerA, parentB), result.DependencyRollouts);
    }

    [Fact]
    public void Expand_ResolutionRolloutsAreOrderedByOrdinalFilePath()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA));
        var parent = Rollout("c.jsonl", "c", "a", Event(StateB));
        var child = Rollout("b.jsonl", "b", "c", Event(StateC));

        var result = CodexForkDependencyExpander.Expand([child], [parent, ancestor]);

        Assert.Equal(OrderByPath(ancestor, child, parent), result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_DependencyOnlyParent_CanBeUsedForStructuralResolution()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var owned = Event(StateB, second: 3);
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA), owned);
        var expansion = CodexForkDependencyExpander.Expand([child], [parent]);

        var resolved = CodexInMemoryForkResolver.Resolve(expansion.ResolutionRollouts);
        var childResult = ByPath(resolved, child.FilePath);

        Assert.Same(parent, childResult.SelectedParent);
        Assert.Equal(1, childResult.ReplayCount);
        Assert.Same(owned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Expand_DependencyOnlyParent_IsNotPromotedToPrimary()
    {
        var parent = Rollout("parent.jsonl", "parent", null, Event(StateA));
        var child = Rollout("child.jsonl", "child", "parent", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([child], [parent]);

        Assert.Equal([child], result.PrimaryRollouts);
        Assert.DoesNotContain(parent, result.PrimaryRollouts);
        Assert.Contains(parent, result.DependencyRollouts);
    }

    [Fact]
    public void Expand_ForkOfForkDependencies_EnableRecursiveStructuralResolution()
    {
        var ancestor = Rollout("a.jsonl", "a", null, Event(StateA));
        var parentOwned = Event(StateB, second: 3);
        var parent = Rollout("b.jsonl", "b", "a", Event(StateA), parentOwned);
        var childOwned = Event(StateC, second: 5);
        var child = Rollout(
            "c.jsonl",
            "c",
            "b",
            Event(StateA),
            Event(StateB, second: 3),
            childOwned);
        var expansion = CodexForkDependencyExpander.Expand(
            [child],
            [parent, ancestor]);

        var resolved = CodexInMemoryForkResolver.Resolve(expansion.ResolutionRollouts);
        var parentResult = ByPath(resolved, parent.FilePath);
        var childResult = ByPath(resolved, child.FilePath);

        Assert.Equal(1, parentResult.ReplayCount);
        Assert.Equal(2, childResult.ReplayCount);
        Assert.Same(childOwned, Assert.Single(childResult.ResolvedRollout.TokenEvents));
    }

    [Fact]
    public void Expand_EmptyPrimary_ReturnsThreeEmptyCollections()
    {
        var available = Rollout("available.jsonl", "available", null, Event(StateA));

        var result = CodexForkDependencyExpander.Expand([], [available]);

        Assert.Empty(result.PrimaryRollouts);
        Assert.Empty(result.DependencyRollouts);
        Assert.Empty(result.ResolutionRollouts);
    }

    [Fact]
    public void Expand_EmptyAvailable_KeepsOnlyPrimaryRollouts()
    {
        var child = Rollout("child.jsonl", "child", "missing", Event(StateA));

        var result = CodexForkDependencyExpander.Expand([child], []);

        Assert.Equal([child], result.PrimaryRollouts);
        Assert.Empty(result.DependencyRollouts);
        Assert.Equal([child], result.ResolutionRollouts);
    }

    private static CodexInMemoryResolvedRollout ByPath(
        IReadOnlyList<CodexInMemoryResolvedRollout> results,
        string path) =>
        Assert.Single(results, result =>
            string.Equals(result.FilePath, path, StringComparison.Ordinal));

    private static IReadOnlyList<CodexEpochRollout> OrderByPath(
        params CodexEpochRollout[] rollouts) =>
        rollouts.OrderBy(item => item.FilePath, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> Paths(IEnumerable<CodexEpochRollout> rollouts) =>
        rollouts.Select(item => item.FilePath).ToArray();

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

    private static CodexEpochTokenEvent Event(UsageState state, int second = 1)
    {
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
            new CodexUsageEntry(8, 3, 2, 0),
            state.Last,
            state.Cumulative);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            SessionId: "session",
            ParentSessionId: null,
            IsSubagent: false);

        return new CodexEpochTokenEvent(tokenEvent, Epoch: 0);
    }

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
