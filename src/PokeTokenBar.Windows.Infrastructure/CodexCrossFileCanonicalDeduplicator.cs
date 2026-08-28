namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexCrossFileCanonicalDeduplicator
{
    public static IReadOnlyList<CodexCanonicalEvent> Deduplicate(
        IEnumerable<CodexEpochRollout> rollouts)
    {
        ArgumentNullException.ThrowIfNull(rollouts);

        var results = new List<CodexCanonicalEvent>();
        var resultIndexByKey = new Dictionary<CodexCanonicalUsageKey, int>();

        foreach (var rollout in rollouts.OrderBy(
                     static rollout => rollout.FilePath,
                     StringComparer.Ordinal))
        {
            foreach (var tokenEvent in rollout.TokenEvents)
            {
                if (!CodexCanonicalUsageKeyFactory.TryCreate(
                        rollout,
                        tokenEvent,
                        out var nullableKey))
                {
                    results.Add(new CodexCanonicalEvent(
                        rollout,
                        tokenEvent,
                        CanonicalKey: null));
                    continue;
                }

                var key = nullableKey!.Value;
                var candidate = new CodexCanonicalEvent(rollout, tokenEvent, key);

                if (resultIndexByKey.TryGetValue(key, out var existingIndex))
                {
                    var existingTimestamp =
                        results[existingIndex].TokenEvent.TokenEvent.TokenCount.Timestamp;
                    var candidateTimestamp = tokenEvent.TokenEvent.TokenCount.Timestamp;

                    if (candidateTimestamp < existingTimestamp)
                    {
                        results[existingIndex] = candidate;
                    }
                }
                else
                {
                    resultIndexByKey.Add(key, results.Count);
                    results.Add(candidate);
                }
            }
        }

        return results;
    }
}
