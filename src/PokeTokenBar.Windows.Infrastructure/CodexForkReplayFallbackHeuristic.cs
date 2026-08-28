namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexForkReplayFallbackHeuristic
{
    private static readonly TimeSpan ReplayMaximumGap = TimeSpan.FromSeconds(1);

    public static CodexForkReplayTrimResult Trim(CodexEpochRollout child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child.RolloutMetadata?.ParentSessionId is null
            || child.RolloutMetadata.IsSubagent)
        {
            return new CodexForkReplayTrimResult(child, ReplayCount: 0);
        }

        var events = child.TokenEvents;
        if (events.Count == 0)
        {
            return new CodexForkReplayTrimResult(child, ReplayCount: 0);
        }

        var replayCount = 1;
        while (replayCount < events.Count)
        {
            var currentTimestamp =
                events[replayCount].TokenEvent.TokenCount.Timestamp;
            var previousTimestamp =
                events[replayCount - 1].TokenEvent.TokenCount.Timestamp;
            var gap = currentTimestamp - previousTimestamp;

            if (gap >= ReplayMaximumGap)
            {
                break;
            }

            replayCount++;
        }

        var remainingEvents = events.Skip(replayCount).ToArray();
        var trimmedChild = child with { TokenEvents = remainingEvents };

        return new CodexForkReplayTrimResult(trimmedChild, replayCount);
    }
}
