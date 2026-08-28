using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexConsecutiveDuplicateFilter
{
    public static CodexParsedRollout Filter(CodexParsedRollout rollout)
    {
        ArgumentNullException.ThrowIfNull(rollout);

        var filteredEvents = new List<CodexRolloutTokenEvent>(rollout.TokenEvents.Count);
        string? previousSessionId = null;
        CodexUsageVector? previousCumulative = null;
        CodexUsageVector previousLast = default;
        var hasPreviousComparableState = false;
        var removedDuplicate = false;

        foreach (var tokenEvent in rollout.TokenEvents)
        {
            var sessionId = tokenEvent.SessionId;
            var cumulative = tokenEvent.TokenCount.CumulativeUsageVector;

            if (sessionId is not null
                && cumulative is CodexUsageVector currentCumulative
                && hasPreviousComparableState
                && string.Equals(sessionId, previousSessionId, StringComparison.Ordinal)
                && currentCumulative == previousCumulative
                && tokenEvent.TokenCount.LastUsageVector == previousLast)
            {
                removedDuplicate = true;
                continue;
            }

            filteredEvents.Add(tokenEvent);

            if (sessionId is not null && cumulative is CodexUsageVector comparableCumulative)
            {
                previousSessionId = sessionId;
                previousCumulative = comparableCumulative;
                previousLast = tokenEvent.TokenCount.LastUsageVector;
                hasPreviousComparableState = true;
            }
            else
            {
                previousSessionId = null;
                previousCumulative = null;
                previousLast = default;
                hasPreviousComparableState = false;
            }
        }

        return removedDuplicate
            ? rollout with { TokenEvents = filteredEvents }
            : rollout;
    }
}
