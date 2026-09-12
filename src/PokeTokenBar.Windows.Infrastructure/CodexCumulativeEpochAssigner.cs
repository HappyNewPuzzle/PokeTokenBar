using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexCumulativeEpochAssigner
{
    public static CodexEpochRollout Assign(CodexParsedRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);

        return Assign(
            rollout.FilePath,
            rollout.RolloutMetadata,
            rollout.TokenEvents,
            static (_, tokenEvent) => tokenEvent.SessionId,
            trustGrowingTotalOnlyLast: false);
    }

    public static CodexEpochRollout ReassignOwnedEvents(CodexEpochRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);

        return Assign(
            rollout.FilePath,
            rollout.RolloutMetadata,
            rollout.TokenEvents.Select(static tokenEvent => tokenEvent.TokenEvent),
            (sourceRollout, tokenEvent) =>
                CodexCanonicalUsageKeyFactory.ResolveOwnerSessionId(
                    sourceRollout,
                    tokenEvent),
            trustGrowingTotalOnlyLast: true);
    }

    private static CodexEpochRollout Assign(
        string filePath,
        CodexSessionMetaParseResult? rolloutMetadata,
        IEnumerable<CodexRolloutTokenEvent> tokenEvents,
        Func<CodexEpochRollout, CodexRolloutTokenEvent, string?> ownerSelector,
        bool trustGrowingTotalOnlyLast)
    {
        var sourceRollout = new CodexEpochRollout(
            filePath,
            rolloutMetadata,
            Array.Empty<CodexEpochTokenEvent>());

        var assignedEvents = new List<CodexEpochTokenEvent>();
        string? currentSessionId = null;
        CodexUsageVector? previousCumulative = null;
        var hasCurrentSession = false;
        var epoch = 0;

        foreach (var tokenEvent in tokenEvents)
        {
            var sessionId = ownerSelector(sourceRollout, tokenEvent);
            if (sessionId is null)
            {
                currentSessionId = null;
                previousCumulative = null;
                hasCurrentSession = false;
                epoch = 0;
                assignedEvents.Add(new CodexEpochTokenEvent(tokenEvent, Epoch: null));
                continue;
            }

            if (!hasCurrentSession
                || !string.Equals(sessionId, currentSessionId, StringComparison.Ordinal))
            {
                currentSessionId = sessionId;
                previousCumulative = null;
                hasCurrentSession = true;
                epoch = 0;
            }

            var cumulative = tokenEvent.TokenCount.CumulativeUsageVector;
            if (cumulative is null)
            {
                previousCumulative = null;
                assignedEvents.Add(new CodexEpochTokenEvent(tokenEvent, Epoch: null));
                continue;
            }

            var assignedTokenEvent = trustGrowingTotalOnlyLast
                ? TrustGrowingTotalOnlyLast(tokenEvent, previousCumulative)
                : tokenEvent;

            if (previousCumulative is CodexUsageVector previous
                && cumulative.Value.HasDecreasedFrom(previous))
            {
                epoch++;
            }

            previousCumulative = cumulative;
            assignedEvents.Add(new CodexEpochTokenEvent(assignedTokenEvent, epoch));
        }

        return new CodexEpochRollout(
            filePath,
            rolloutMetadata,
            assignedEvents);
    }

    private static CodexRolloutTokenEvent TrustGrowingTotalOnlyLast(
        CodexRolloutTokenEvent tokenEvent,
        CodexUsageVector? previousCumulative)
    {
        var tokenCount = tokenEvent.TokenCount;
        var last = tokenCount.LastUsageVector;
        var cumulative = tokenCount.CumulativeUsageVector;
        if (tokenCount.Entry.TotalTokens != 0
            || last.BillableComponentTokens != 0
            || last.TotalTokens <= 0
            || previousCumulative is not CodexUsageVector previous
            || cumulative is not CodexUsageVector current
            || current.TotalTokens <= previous.TotalTokens)
        {
            return tokenEvent;
        }

        return tokenEvent with
        {
            TokenCount = tokenCount with
            {
                Entry = new CodexUsageEntry(last.TotalTokens, 0, 0, 0),
            },
        };
    }
}
