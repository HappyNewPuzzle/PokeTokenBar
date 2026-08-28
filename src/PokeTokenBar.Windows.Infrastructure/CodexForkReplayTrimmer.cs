using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexForkReplayTrimmer
{
    public static CodexForkReplayTrimResult Trim(
        CodexEpochRollout parent,
        CodexEpochRollout child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);

        ValidateRelationship(parent, child);

        var comparablePrefixCount = ComparableUsagePrefixCount(
            child.TokenEvents,
            parent.TokenEvents);
        var replayCount = comparablePrefixCount is > 0
            ? comparablePrefixCount.Value
            : 0;

        if (replayCount == 0)
        {
            return new CodexForkReplayTrimResult(child, ReplayCount: 0);
        }

        var remainingEvents = child.TokenEvents.Skip(replayCount).ToArray();
        var trimmedChild = child with { TokenEvents = remainingEvents };

        return new CodexForkReplayTrimResult(trimmedChild, replayCount);
    }

    private static int? ComparableUsagePrefixCount(
        IReadOnlyList<CodexEpochTokenEvent> childEvents,
        IReadOnlyList<CodexEpochTokenEvent> parentEvents)
    {
        if (childEvents.Count == 0)
        {
            return 0;
        }

        if (parentEvents.Count == 0)
        {
            return null;
        }

        var count = 0;
        while (count < childEvents.Count && count < parentEvents.Count)
        {
            if (!TryGetUsageState(childEvents[count], out var childState)
                || !TryGetUsageState(parentEvents[count], out var parentState))
            {
                return null;
            }

            if (childState != parentState)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool TryGetUsageState(
        CodexEpochTokenEvent tokenEvent,
        out CodexReplayUsageState state)
    {
        var tokenCount = tokenEvent.TokenEvent.TokenCount;
        if (tokenCount.CumulativeUsageVector is not CodexUsageVector cumulative)
        {
            state = default;
            return false;
        }

        state = new CodexReplayUsageState(cumulative, tokenCount.LastUsageVector);
        return true;
    }

    private static void ValidateRelationship(
        CodexEpochRollout parent,
        CodexEpochRollout child)
    {
        var expectedParentSessionId = child.RolloutMetadata?.ParentSessionId;
        if (expectedParentSessionId is null)
        {
            throw new ArgumentException(
                "The child rollout does not identify a parent session.",
                nameof(child));
        }

        if (!string.Equals(
                parent.RolloutMetadata?.SessionId,
                expectedParentSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The parent rollout session does not match the child's parent session.",
                nameof(parent));
        }

        if (string.Equals(parent.FilePath, child.FilePath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The parent and child rollouts must be different files.",
                nameof(child));
        }
    }

    private readonly record struct CodexReplayUsageState(
        CodexUsageVector Cumulative,
        CodexUsageVector Last);
}
