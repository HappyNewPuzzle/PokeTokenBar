using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexConsecutiveDuplicateFilterTests
{
    private static readonly CodexUsageVector DefaultCumulative =
        Vector(input: 100, cached: 20, output: 30, reasoning: 5, total: 130);

    private static readonly CodexUsageVector DefaultLast =
        Vector(input: 10, cached: 2, output: 3, reasoning: 1, total: 13);

    [Fact]
    public void Filter_SameSessionAndIdenticalConsecutiveStates_RemovesSecondEvent()
    {
        var first = Event("session-a");
        var second = Event("session-a", second: 2);

        var result = Filter(first, second);

        Assert.Collection(result.TokenEvents, tokenEvent => Assert.Same(first, tokenEvent));
    }

    [Fact]
    public void Filter_ThreeIdenticalConsecutiveStates_KeepsOnlyFirstEvent()
    {
        var first = Event("session-a");

        var result = Filter(first, Event("session-a", second: 2), Event("session-a", second: 3));

        Assert.Collection(result.TokenEvents, tokenEvent => Assert.Same(first, tokenEvent));
    }

    [Fact]
    public void Filter_SameCumulativeButDifferentLastInput_KeepsBothEvents()
    {
        var first = Event("session-a");
        var second = Event("session-a", last: DefaultLast with { InputTokens = 11 }, second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_SameCumulativeButDifferentLastReasoning_KeepsBothEvents()
    {
        var first = Event("session-a");
        var second = Event(
            "session-a",
            last: DefaultLast with { ReasoningOutputTokens = 2 },
            second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_SameLastButDifferentCumulativeField_KeepsBothEvents()
    {
        var first = Event("session-a");
        var second = Event(
            "session-a",
            cumulative: DefaultCumulative with { CacheWriteInputTokens = 1 },
            second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_SameScalarTotalsButDifferentVectorFields_KeepsBothEvents()
    {
        var first = Event("session-a");
        var second = Event(
            "session-a",
            cumulative: DefaultCumulative with { InputTokens = 90, OutputTokens = 40 },
            last: DefaultLast with { InputTokens = 9, OutputTokens = 4 },
            second: 2);

        var result = Filter(first, second);

        Assert.Equal(DefaultCumulative.TotalTokens, second.TokenCount.CumulativeUsageVector?.TotalTokens);
        Assert.Equal(DefaultLast.TotalTokens, second.TokenCount.LastUsageVector.TotalTokens);
        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_MissingCumulativeWithSameLast_KeepsBothEvents()
    {
        var first = Event("session-a", hasCumulative: false);
        var second = Event("session-a", hasCumulative: false, second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_MissingCumulative_ResetsPreviousComparableState()
    {
        var first = Event("session-a");
        var withoutCumulative = Event("session-a", hasCumulative: false, second: 2);
        var laterSameState = Event("session-a", second: 3);

        var result = Filter(first, withoutCumulative, laterSameState);

        Assert.Equal([first, withoutCumulative, laterSameState], result.TokenEvents);
    }

    [Fact]
    public void Filter_SameStateAcrossDifferentSessions_KeepsBothEvents()
    {
        var first = Event("session-a");
        var second = Event("session-b", second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_DuplicateInFirstSessionThenSameStateInSecondSession_KeepsFirstPerSession()
    {
        var firstA = Event("session-a");
        var firstB = Event("session-b", second: 3);

        var result = Filter(firstA, Event("session-a", second: 2), firstB);

        Assert.Equal([firstA, firstB], result.TokenEvents);
    }

    [Fact]
    public void Filter_SessionChangesFromAToBToA_DoesNotReuseEarlierAState()
    {
        var firstA = Event("session-a");
        var eventB = Event("session-b", second: 2);
        var secondA = Event("session-a", second: 3);

        var result = Filter(firstA, eventB, secondA);

        Assert.Equal([firstA, eventB, secondA], result.TokenEvents);
    }

    [Fact]
    public void Filter_ConsecutiveNullSessionEvents_AreNotDeduplicated()
    {
        var first = Event(sessionId: null);
        var second = Event(sessionId: null, second: 2);

        var result = Filter(first, second);

        Assert.Equal([first, second], result.TokenEvents);
    }

    [Fact]
    public void Filter_NullSessionEvent_ResetsPreviousComparableState()
    {
        var first = Event("session-a");
        var withoutSession = Event(sessionId: null, second: 2);
        var laterA = Event("session-a", second: 3);

        var result = Filter(first, withoutSession, laterA);

        Assert.Equal([first, withoutSession, laterA], result.TokenEvents);
    }

    [Fact]
    public void Filter_ParentAndSubagentDifferences_DoNotAffectDuplicateDecision()
    {
        var first = Event("session-a", parentSessionId: "parent-a", isSubagent: false);
        var second = Event(
            "session-a",
            parentSessionId: "parent-b",
            isSubagent: true,
            second: 2);

        var result = Filter(first, second);

        var retained = Assert.Single(result.TokenEvents);
        Assert.Same(first, retained);
        Assert.Equal("parent-a", retained.ParentSessionId);
        Assert.False(retained.IsSubagent);
    }

    [Fact]
    public void Filter_RemovingDuplicate_PreservesRolloutMetadataAndFilePath()
    {
        var metadata = new CodexSessionMetaParseResult("session-a", "parent-a", true);
        var rollout = new CodexParsedRollout(
            Path.GetFullPath("rollout.jsonl"),
            metadata,
            new[] { Event("session-a"), Event("session-a", second: 2) });

        var result = CodexConsecutiveDuplicateFilter.Filter(rollout);

        Assert.Equal(rollout.FilePath, result.FilePath);
        Assert.Same(metadata, result.RolloutMetadata);
        Assert.Single(result.TokenEvents);
    }

    [Fact]
    public void Filter_EmptyRollout_ReturnsEmptyResult()
    {
        var rollout = Rollout();

        var result = CodexConsecutiveDuplicateFilter.Filter(rollout);

        Assert.Same(rollout, result);
        Assert.Empty(result.TokenEvents);
    }

    [Fact]
    public void Filter_SingleEvent_PreservesIt()
    {
        var tokenEvent = Event("session-a");
        var rollout = Rollout(tokenEvent);

        var result = CodexConsecutiveDuplicateFilter.Filter(rollout);

        Assert.Same(rollout, result);
        Assert.Same(tokenEvent, Assert.Single(result.TokenEvents));
    }

    [Fact]
    public void Filter_NoDuplicates_PreservesOrderAndEventObjects()
    {
        var first = Event("session-a");
        var second = Event(
            "session-a",
            cumulative: DefaultCumulative with { InputTokens = 200 },
            second: 2);
        var third = Event(
            "session-a",
            cumulative: DefaultCumulative with { InputTokens = 300 },
            second: 3);

        var result = Filter(first, second, third);

        Assert.Equal([first, second, third], result.TokenEvents);
        Assert.Same(first, result.TokenEvents[0]);
        Assert.Same(second, result.TokenEvents[1]);
        Assert.Same(third, result.TokenEvents[2]);
    }

    private static CodexParsedRollout Filter(params CodexRolloutTokenEvent[] events) =>
        CodexConsecutiveDuplicateFilter.Filter(Rollout(events));

    private static CodexParsedRollout Rollout(params CodexRolloutTokenEvent[] events) =>
        new(Path.GetFullPath("rollout.jsonl"), RolloutMetadata: null, events);

    private static CodexRolloutTokenEvent Event(
        string? sessionId,
        CodexUsageVector? cumulative = null,
        CodexUsageVector? last = null,
        string? parentSessionId = null,
        bool isSubagent = false,
        bool hasCumulative = true,
        int second = 1)
    {
        last ??= DefaultLast;

        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
            new CodexUsageEntry(8, 3, 2, 0),
            last.Value,
            hasCumulative ? cumulative ?? DefaultCumulative : null);

        return new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            parentSessionId,
            isSubagent);
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
