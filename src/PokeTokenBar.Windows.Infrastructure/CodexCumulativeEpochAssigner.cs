using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexCumulativeEpochAssigner
{
    public static CodexEpochRollout Assign(CodexParsedRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);

        var assignedEvents = new List<CodexEpochTokenEvent>(rollout.TokenEvents.Count);
        string? currentSessionId = null;
        CodexUsageVector? previousCumulative = null;
        var hasCurrentSession = false;
        var epoch = 0;

        foreach (var tokenEvent in rollout.TokenEvents)
        {
            var sessionId = tokenEvent.SessionId;
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
            rollout.FilePath,
            rollout.RolloutMetadata,
            assignedEvents);
    }
}
