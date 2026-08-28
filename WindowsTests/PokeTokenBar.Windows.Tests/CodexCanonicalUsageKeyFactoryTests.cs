using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexCanonicalUsageKeyFactoryTests
{
    private static readonly CodexUsageVector Cumulative =
        Vector(input: 100, cached: 20, cacheWrite: 3, output: 30, reasoning: 5, total: 130);

    private static readonly CodexUsageVector Last =
        Vector(input: 10, cached: 2, cacheWrite: 1, output: 3, reasoning: 1, total: 13);

    [Fact]
    public void TryCreate_ComparableEvent_CreatesCanonicalKey()
    {
        var tokenEvent = EpochEvent("session-a", epoch: 2);
        var rollout = Rollout(tokenEvent, rolloutSessionId: "session-a");

        var success = CodexCanonicalUsageKeyFactory.TryCreate(rollout, tokenEvent, out var key);

        Assert.True(success);
        Assert.NotNull(key);
        Assert.Equal("codex", key.Value.Provider);
        Assert.Equal("session-a", key.Value.OwnerSessionId);
        Assert.Equal(2, key.Value.Epoch);
        Assert.Equal(Cumulative, key.Value.CumulativeUsageVector);
        Assert.Equal(Last, key.Value.LastUsageVector);
    }

    [Fact]
    public void TryCreate_SameCanonicalInputs_ProducesEqualKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent("session-a", epoch: 0, second: 2));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreate_DifferentEventSessionId_ProducesDifferentKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent("session-b", epoch: 0));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryCreate_DifferentEpoch_ProducesDifferentKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent("session-a", epoch: 1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryCreate_DifferentCumulativeInput_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { InputTokens = 101 });
    }

    [Fact]
    public void TryCreate_DifferentCumulativeCachedInput_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { CachedInputTokens = 21 });
    }

    [Fact]
    public void TryCreate_DifferentCumulativeCacheWrite_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { CacheWriteInputTokens = 4 });
    }

    [Fact]
    public void TryCreate_DifferentCumulativeOutput_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { OutputTokens = 31 });
    }

    [Fact]
    public void TryCreate_DifferentCumulativeReasoning_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { ReasoningOutputTokens = 6 });
    }

    [Fact]
    public void TryCreate_DifferentCumulativeTotal_ProducesDifferentKeys()
    {
        AssertCumulativeDifference(Cumulative with { TotalTokens = 131 });
    }

    [Fact]
    public void TryCreate_SameScalarTotalButDifferentCumulativeFields_ProducesDifferentKeys()
    {
        var changed = Cumulative with { InputTokens = 90, OutputTokens = 40 };

        Assert.Equal(Cumulative.TotalTokens, changed.TotalTokens);
        AssertCumulativeDifference(changed);
    }

    [Fact]
    public void TryCreate_DifferentTimestamp_ProducesEqualKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0, second: 1));
        var second = Key(EpochEvent("session-a", epoch: 0, second: 59));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreate_DifferentEntryValues_ProducesEqualKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent(
            "session-a",
            epoch: 0,
            entry: new CodexUsageEntry(999, 888, 777, 666)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreate_DifferentLastUsageVector_ProducesDifferentKeys()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent(
            "session-a",
            epoch: 0,
            last: Last with { ReasoningOutputTokens = 2 }));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryCreate_DifferentParentSessionIdOnEvent_DoesNotChangeKey()
    {
        var first = Key(EpochEvent("session-a", epoch: 0, parentSessionId: "parent-a"));
        var second = Key(EpochEvent("session-a", epoch: 0, parentSessionId: "parent-b"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreate_DifferentSubagentFlag_DoesNotChangeKey()
    {
        var first = Key(EpochEvent("session-a", epoch: 0, isSubagent: false));
        var second = Key(EpochEvent("session-a", epoch: 0, isSubagent: true));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreate_NonForkRollout_PrefersCurrentEventSessionId()
    {
        var tokenEvent = EpochEvent("embedded-session", epoch: 0);
        var rollout = Rollout(tokenEvent, rolloutSessionId: "rollout-session");

        var key = Create(rollout, tokenEvent);

        Assert.Equal("embedded-session", key.OwnerSessionId);
    }

    [Fact]
    public void TryCreate_NullEventSessionId_FallsBackToNonForkRolloutSessionId()
    {
        var tokenEvent = EpochEvent(sessionId: null, epoch: 0);
        var rollout = Rollout(tokenEvent, rolloutSessionId: "rollout-session");

        var key = Create(rollout, tokenEvent);

        Assert.Equal("rollout-session", key.OwnerSessionId);
    }

    [Fact]
    public void TryCreate_ForkRollout_UsesRolloutSessionIdAsOwner()
    {
        var tokenEvent = EpochEvent("embedded-parent-session", epoch: 0);
        var rollout = Rollout(
            tokenEvent,
            rolloutSessionId: "child-session",
            rolloutParentSessionId: "parent-session");

        var key = Create(rollout, tokenEvent);

        Assert.Equal("child-session", key.OwnerSessionId);
    }

    [Fact]
    public void TryCreate_ParentMetadataDoesNotChangeKeyWhenResolvedOwnerIsSame()
    {
        var tokenEvent = EpochEvent("session-a", epoch: 0);
        var nonFork = Rollout(tokenEvent, rolloutSessionId: "session-a");
        var fork = Rollout(
            tokenEvent,
            rolloutSessionId: "session-a",
            rolloutParentSessionId: "parent-a");

        Assert.Equal(Create(nonFork, tokenEvent), Create(fork, tokenEvent));
    }

    [Fact]
    public void TryCreate_NoEventOrRolloutSessionId_ReturnsFalse()
    {
        var tokenEvent = EpochEvent(sessionId: null, epoch: 0);
        var rollout = Rollout(tokenEvent, rolloutSessionId: null);

        var success = CodexCanonicalUsageKeyFactory.TryCreate(rollout, tokenEvent, out var key);

        Assert.False(success);
        Assert.Null(key);
    }

    [Fact]
    public void TryCreate_NullEpoch_ReturnsFalse()
    {
        var tokenEvent = EpochEvent("session-a", epoch: null);
        var rollout = Rollout(tokenEvent, rolloutSessionId: "session-a");

        var success = CodexCanonicalUsageKeyFactory.TryCreate(rollout, tokenEvent, out var key);

        Assert.False(success);
        Assert.Null(key);
    }

    [Fact]
    public void TryCreate_NullCumulativeUsage_ReturnsFalse()
    {
        var tokenEvent = EpochEvent("session-a", epoch: 0, hasCumulative: false);
        var rollout = Rollout(tokenEvent, rolloutSessionId: "session-a");

        var success = CodexCanonicalUsageKeyFactory.TryCreate(rollout, tokenEvent, out var key);

        Assert.False(success);
        Assert.Null(key);
    }

    [Fact]
    public void CanonicalKey_EqualValuesCollapseInHashSet()
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent("session-a", epoch: 0, second: 2));

        var keys = new HashSet<CodexCanonicalUsageKey> { first, second };

        Assert.Single(keys);
    }

    private static void AssertCumulativeDifference(CodexUsageVector changedCumulative)
    {
        var first = Key(EpochEvent("session-a", epoch: 0));
        var second = Key(EpochEvent("session-a", epoch: 0, cumulative: changedCumulative));

        Assert.NotEqual(first, second);
    }

    private static CodexCanonicalUsageKey Key(CodexEpochTokenEvent tokenEvent)
    {
        var rollout = Rollout(tokenEvent, rolloutSessionId: tokenEvent.TokenEvent.SessionId);
        return Create(rollout, tokenEvent);
    }

    private static CodexCanonicalUsageKey Create(
        CodexEpochRollout rollout,
        CodexEpochTokenEvent tokenEvent)
    {
        var success = CodexCanonicalUsageKeyFactory.TryCreate(rollout, tokenEvent, out var key);
        Assert.True(success);
        return Assert.IsType<CodexCanonicalUsageKey>(key);
    }

    private static CodexEpochRollout Rollout(
        CodexEpochTokenEvent tokenEvent,
        string? rolloutSessionId,
        string? rolloutParentSessionId = null)
    {
        CodexSessionMetaParseResult? metadata = rolloutSessionId is null
            ? null
            : new CodexSessionMetaParseResult(
                rolloutSessionId,
                rolloutParentSessionId,
                IsSubagent: false);

        return new CodexEpochRollout(
            Path.GetFullPath("rollout.jsonl"),
            metadata,
            new[] { tokenEvent });
    }

    private static CodexEpochTokenEvent EpochEvent(
        string? sessionId,
        int? epoch,
        CodexUsageVector? cumulative = default,
        CodexUsageVector? last = null,
        CodexUsageEntry? entry = null,
        string? parentSessionId = null,
        bool isSubagent = false,
        bool hasCumulative = true,
        int second = 1)
    {
        last ??= Last;
        entry ??= new CodexUsageEntry(8, 3, 2, 0);

        var tokenCount = new CodexTokenCountParseResult(
            new DateTimeOffset(2026, 7, 29, 1, 0, second, TimeSpan.Zero),
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
