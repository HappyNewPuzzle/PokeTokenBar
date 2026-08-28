namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexCanonicalUsageKeyFactory
{
    public static bool TryCreate(
        CodexEpochRollout rollout,
        CodexEpochTokenEvent tokenEvent,
        out CodexCanonicalUsageKey? key)
    {
        ArgumentNullException.ThrowIfNull(rollout);
        ArgumentNullException.ThrowIfNull(tokenEvent);

        var ownerSessionId = ResolveOwnerSessionId(rollout, tokenEvent.TokenEvent);
        var cumulative = tokenEvent.TokenEvent.TokenCount.CumulativeUsageVector;

        if (ownerSessionId is null
            || tokenEvent.Epoch is not int epoch
            || cumulative is null)
        {
            key = null;
            return false;
        }

        key = new CodexCanonicalUsageKey(
            ownerSessionId,
            epoch,
            cumulative.Value,
            tokenEvent.TokenEvent.TokenCount.LastUsageVector);
        return true;
    }

    internal static string? ResolveOwnerSessionId(
        CodexEpochRollout rollout,
        CodexRolloutTokenEvent tokenEvent)
    {
        var rolloutSessionId = rollout.RolloutMetadata?.SessionId;

        return rollout.RolloutMetadata?.ParentSessionId is null
            ? tokenEvent.SessionId ?? rolloutSessionId
            : rolloutSessionId;
    }
}
