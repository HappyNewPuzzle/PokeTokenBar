using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexForkReplayFallbackHeuristicTests
{
    [Fact]
    public void Trim_EmptyManualFork_ReturnsZeroAndPreservesChild()
    {
        var child = ManualFork();

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_OneEvent_TrimsTheSingleEvent()
    {
        var child = ManualFork(Event(TimeSpan.Zero));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(1, result.ReplayCount);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_TwoRapidEvents_TrimsBothEvents()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(300)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_MultipleRapidEvents_TrimsEntireLeadingBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(300)),
            Event(TimeSpan.FromMilliseconds(700)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(3, result.ReplayCount);
        Assert.Empty(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_RapidBurstFollowedByLargeGap_KeepsFirstEventAfterGap()
    {
        var owned = Event(TimeSpan.FromMilliseconds(2_500));
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(300)),
            Event(TimeSpan.FromMilliseconds(700)),
            owned);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(3, result.ReplayCount);
        Assert.Same(owned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_LargeGapFromFirstEvent_TrimsOnlyFirstEvent()
    {
        var owned = Event(TimeSpan.FromSeconds(2));
        var child = ManualFork(Event(TimeSpan.Zero), owned);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(1, result.ReplayCount);
        Assert.Same(owned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_GapImmediatelyBeforeThreshold_RemainsInBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(999)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Trim_GapExactlyAtThreshold_EndsBurst()
    {
        var boundary = Event(TimeSpan.FromSeconds(1));
        var child = ManualFork(Event(TimeSpan.Zero), boundary);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(1, result.ReplayCount);
        Assert.Same(boundary, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_GapImmediatelyAfterThreshold_EndsBurst()
    {
        var afterThreshold = Event(TimeSpan.FromMilliseconds(1_001));
        var child = ManualFork(Event(TimeSpan.Zero), afterThreshold);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(1, result.ReplayCount);
        Assert.Same(afterThreshold, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_IdenticalTimestamps_AreInSameBurst()
    {
        var child = ManualFork(Event(TimeSpan.Zero), Event(TimeSpan.Zero));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Trim_DecreasingTimestamp_IsInSameBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.FromSeconds(2)),
            Event(TimeSpan.FromSeconds(1)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Trim_BurstEndingEventIsNotTrimmed()
    {
        var burstEnd = Event(TimeSpan.FromMilliseconds(1_400));
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(400)),
            burstEnd);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
        Assert.Same(burstEnd, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    [Fact]
    public void Trim_LaterRapidBurstAfterFirstLargeGap_IsNotScanned()
    {
        var firstOwned = Event(TimeSpan.FromSeconds(2));
        var secondOwned = Event(TimeSpan.FromMilliseconds(2_100));
        var child = ManualFork(
            Event(TimeSpan.Zero),
            firstOwned,
            secondOwned);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(1, result.ReplayCount);
        Assert.Equal([firstOwned, secondOwned], result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_PreservesFilePath()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromSeconds(2)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(child.FilePath, result.TrimmedChild.FilePath);
    }

    [Fact]
    public void Trim_PreservesRolloutMetadata()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromSeconds(2)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Same(child.RolloutMetadata, result.TrimmedChild.RolloutMetadata);
    }

    [Fact]
    public void Trim_PreservesRemainingEventObjectsAndOrder()
    {
        var firstOwned = Event(TimeSpan.FromSeconds(2));
        var secondOwned = Event(TimeSpan.FromSeconds(4));
        var child = ManualFork(
            Event(TimeSpan.Zero),
            firstOwned,
            secondOwned);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal([firstOwned, secondOwned], result.TrimmedChild.TokenEvents);
        Assert.Same(firstOwned, result.TrimmedChild.TokenEvents[0]);
        Assert.Same(secondOwned, result.TrimmedChild.TokenEvents[1]);
    }

    [Fact]
    public void Trim_MissingCumulative_DoesNotAffectTimestampBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero, hasCumulative: false),
            Event(TimeSpan.FromMilliseconds(10), hasCumulative: false),
            Event(TimeSpan.FromSeconds(3), hasCumulative: false));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
        Assert.Single(result.TrimmedChild.TokenEvents);
    }

    [Fact]
    public void Trim_DifferentEpochs_DoNotAffectTimestampBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero, epoch: 0),
            Event(TimeSpan.FromMilliseconds(10), epoch: 99));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Trim_DifferentEntries_DoNotAffectTimestampBurst()
    {
        var child = ManualFork(
            Event(TimeSpan.Zero, entry: new CodexUsageEntry(1, 2, 3, 4)),
            Event(
                TimeSpan.FromMilliseconds(10),
                entry: new CodexUsageEntry(100, 200, 300, 400)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
    }

    [Fact]
    public void Trim_Subagent_DoesNotApplyFallback()
    {
        var child = Rollout(
            parentSessionId: "parent",
            isSubagent: true,
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(10)));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_NonForkRollout_DoesNotApplyFallback()
    {
        var child = Rollout(
            parentSessionId: null,
            isSubagent: false,
            Event(TimeSpan.Zero));

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(0, result.ReplayCount);
        Assert.Same(child, result.TrimmedChild);
    }

    [Fact]
    public void Trim_SwiftFallbackPattern_TrimsRapidPairAndKeepsLaterTurn()
    {
        var owned = Event(TimeSpan.FromSeconds(3));
        var child = ManualFork(
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMilliseconds(10)),
            owned);

        var result = CodexForkReplayFallbackHeuristic.Trim(child);

        Assert.Equal(2, result.ReplayCount);
        Assert.Same(owned, Assert.Single(result.TrimmedChild.TokenEvents));
    }

    private static CodexEpochRollout ManualFork(params CodexEpochTokenEvent[] events) =>
        Rollout(parentSessionId: "parent", isSubagent: false, events);

    private static CodexEpochRollout Rollout(
        string? parentSessionId,
        bool isSubagent,
        params CodexEpochTokenEvent[] events) =>
        new(
            Path.GetFullPath("child.jsonl"),
            new CodexSessionMetaParseResult("child", parentSessionId, isSubagent),
            events);

    private static CodexEpochTokenEvent Event(
        TimeSpan offset,
        bool hasCumulative = true,
        int? epoch = 0,
        CodexUsageEntry? entry = null)
    {
        var usageVector = new CodexUsageVector(
            InputTokens: 100,
            CachedInputTokens: 20,
            CacheWriteInputTokens: 0,
            OutputTokens: 10,
            ReasoningOutputTokens: 2,
            TotalTokens: 110);
        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero) + offset,
            entry ?? new CodexUsageEntry(80, 10, 20, 0),
            usageVector,
            hasCumulative ? usageVector : null);
        var tokenEvent = new CodexRolloutTokenEvent(
            tokenCount,
            SessionId: "parent",
            ParentSessionId: null,
            IsSubagent: false);

        return new CodexEpochTokenEvent(tokenEvent, epoch);
    }
}
