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
            static (_, tokenEvent) => tokenEvent.SessionId);
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
                    tokenEvent));
    }

    private static CodexEpochRollout Assign(
        string filePath,
        CodexSessionMetaParseResult? rolloutMetadata,
        IEnumerable<CodexRolloutTokenEvent> tokenEvents,
        Func<CodexEpochRollout, CodexRolloutTokenEvent, string?> ownerSelector)
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

            if (previousCumulative is CodexUsageVector previous
                && cumulative.Value.HasDecreasedFrom(previous))
            {
                epoch++;
            }

            previousCumulative = cumulative;
            assignedEvents.Add(new CodexEpochTokenEvent(tokenEvent, epoch));
        }

        return new CodexEpochRollout(
            filePath,
            rolloutMetadata,
            assignedEvents);
    }
}
