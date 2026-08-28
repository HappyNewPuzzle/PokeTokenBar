using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexCumulativeEpochAssignerTests
{
    private static readonly CodexUsageVector Baseline = Vector(100, 100, 100, 100, 100, 100);

    [Fact]
    public void Assign_PartiallyIncreasingCumulative_KeepsEpochZero()
    {
        var result = Assign(
            Event("session-a", Baseline),
            Event("session-a", Baseline with { InputTokens = 101 }, second: 2));

        AssertEpochs(result, 0, 0);
    }

    [Fact]
    public void Assign_AllIncreasingCumulative_KeepsEpochZero()
    {
        var result = Assign(
            Event("session-a", Baseline),
            Event("session-a", Vector(101, 101, 101, 101, 101, 101), second: 2));

        AssertEpochs(result, 0, 0);
    }

    [Fact]
    public void Assign_IdenticalCumulative_KeepsEpochZero()
    {
        var result = Assign(
            Event("session-a", Baseline),
            Event("session-a", Baseline, second: 2));

        AssertEpochs(result, 0, 0);
    }

    [Fact]
    public void Assign_DecreasedInputTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { InputTokens = 99 });
    }

    [Fact]
    public void Assign_DecreasedCachedInputTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { CachedInputTokens = 99 });
    }

    [Fact]
    public void Assign_DecreasedCacheWriteInputTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { CacheWriteInputTokens = 99 });
    }

    [Fact]
    public void Assign_DecreasedOutputTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { OutputTokens = 99 });
    }

    [Fact]
    public void Assign_DecreasedReasoningOutputTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { ReasoningOutputTokens = 99 });
    }

    [Fact]
    public void Assign_DecreasedTotalTokens_IncrementsEpoch()
    {
        AssertSingleDecrease(Baseline with { TotalTokens = 99 });
    }

    [Fact]
    public void Assign_TwoCumulativeResets_ProducesEpochsZeroOneTwo()
    {
        var result = Assign(
            Event("session-a", Vector(input: 100)),
            Event("session-a", Vector(input: 50), second: 2),
            Event("session-a", Vector(input: 80), second: 3),
            Event("session-a", Vector(input: 5), second: 4));

        AssertEpochs(result, 0, 1, 1, 2);
    }

    [Fact]
    public void Assign_LowerFirstCumulativeInNewSession_StartsAtEpochZero()
    {
        var result = Assign(
            Event("session-a", Vector(input: 500)),
            Event("session-b", Vector(input: 10), second: 2));

        AssertEpochs(result, 0, 0);
    }

    [Fact]
    public void Assign_SessionChangesFromAToBToA_RestartsEachSequenceAtEpochZero()
    {
        var result = Assign(
            Event("session-a", Vector(input: 500)),
            Event("session-b", Vector(input: 10), second: 2),
            Event("session-a", Vector(input: 100), second: 3));

        AssertEpochs(result, 0, 0, 0);
    }

    [Fact]
    public void Assign_MissingCumulative_HasNoEpoch()
    {
        var result = Assign(EventWithoutCumulative("session-a"));

        AssertEpochs(result, (int?)null);
    }

    [Fact]
    public void Assign_MissingCumulative_ClearsComparisonButPreservesCurrentEpochNumber()
    {
        var result = Assign(
            Event("session-a", Vector(input: 100)),
            Event("session-a", Vector(input: 50), second: 2),
            EventWithoutCumulative("session-a", second: 3),
            Event("session-a", Vector(input: 10), second: 4));

        AssertEpochs(result, 0, 1, null, 1);
    }

    [Fact]
    public void Assign_NullSessionId_HasNoEpoch()
    {
        var result = Assign(Event(sessionId: null, cumulative: Baseline));

        AssertEpochs(result, (int?)null);
    }

    [Fact]
    public void Assign_NullSessionId_ResetsStateBeforeLaterSessionEvent()
    {
        var result = Assign(
            Event("session-a", Vector(input: 500)),
            Event(sessionId: null, cumulative: Vector(input: 10), second: 2),
            Event("session-a", Vector(input: 100), second: 3));

        AssertEpochs(result, 0, null, 0);
    }

    [Fact]
    public void Assign_EmptyRollout_ReturnsEmptyEvents()
    {
        var result = CodexCumulativeEpochAssigner.Assign(Rollout());

        Assert.Empty(result.TokenEvents);
    }

    [Fact]
    public void Assign_SingleComparableEvent_StartsAtEpochZero()
    {
        var result = Assign(Event("session-a", Baseline));

        AssertEpochs(result, 0);
    }

    [Fact]
    public void Assign_PreservesEventOrder()
    {
        var first = Event("session-a", Vector(input: 10));
        var second = Event("session-a", Vector(input: 20), second: 2);
        var third = Event("session-a", Vector(input: 30), second: 3);

        var result = Assign(first, second, third);

        Assert.Same(first, result.TokenEvents[0].TokenEvent);
        Assert.Same(second, result.TokenEvents[1].TokenEvent);
        Assert.Same(third, result.TokenEvents[2].TokenEvent);
    }

    [Fact]
    public void Assign_PreservesOriginalTokenEventAndAllData()
    {
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 2, 3, TimeSpan.Zero),
            new CodexUsageEntry(80, 10, 40, 0),
            Vector(input: 120, cached: 40, output: 10, reasoning: 2, total: 130),
            Baseline);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            "session-a",
            "parent-a",
            IsSubagent: true);

        var result = Assign(tokenEvent);

        var assigned = Assert.Single(result.TokenEvents);
        Assert.Same(tokenEvent, assigned.TokenEvent);
        Assert.Same(tokenCount, assigned.TokenEvent.TokenCount);
        Assert.Equal("parent-a", assigned.TokenEvent.ParentSessionId);
        Assert.True(assigned.TokenEvent.IsSubagent);
    }

    [Fact]
    public void Assign_PreservesRolloutFilePathAndMetadata()
    {
        var metadata = new CodexSessionMetaParseResult("session-a", "parent-a", true);
        var rollout = new CodexParsedRollout(
            Path.GetFullPath("rollout.jsonl"),
            metadata,
            new[] { Event("session-a", Baseline) });

        var result = CodexCumulativeEpochAssigner.Assign(rollout);

        Assert.Equal(rollout.FilePath, result.FilePath);
        Assert.Same(metadata, result.RolloutMetadata);
    }

    private static void AssertSingleDecrease(CodexUsageVector current)
    {
        var result = Assign(
            Event("session-a", Baseline),
            Event("session-a", current, second: 2));

        AssertEpochs(result, 0, 1);
    }

    private static void AssertEpochs(CodexEpochRollout rollout, params int?[] expected) =>
        Assert.Equal(expected, rollout.TokenEvents.Select(tokenEvent => tokenEvent.Epoch));

    private static CodexEpochRollout Assign(params CodexRolloutTokenEvent[] events) =>
        CodexCumulativeEpochAssigner.Assign(Rollout(events));

    private static CodexParsedRollout Rollout(params CodexRolloutTokenEvent[] events) =>
        new(Path.GetFullPath("rollout.jsonl"), RolloutMetadata: null, events);

    private static CodexRolloutTokenEvent EventWithoutCumulative(string? sessionId, int second = 1) =>
        Event(sessionId, cumulative: null, second: second);

    private static CodexRolloutTokenEvent Event(
        string? sessionId,
        CodexUsageVector? cumulative,
        int second = 1)
    {
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
            new CodexUsageEntry(8, 3, 2, 0),
            Vector(input: 10, cached: 2, output: 3, reasoning: 1, total: 13),
            cumulative);

        return new CodexRolloutTokenEvent(
            tokenCount,
            sessionId,
            ParentSessionId: "parent-a",
            IsSubagent: true);
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
