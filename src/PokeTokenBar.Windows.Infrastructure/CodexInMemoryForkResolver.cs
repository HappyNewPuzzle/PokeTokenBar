namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexInMemoryForkResolver
{
    public static IReadOnlyList<CodexInMemoryResolvedRollout> Resolve(
        IEnumerable<CodexEpochRollout> rollouts)
    {
        ArgumentNullException.ThrowIfNull(rollouts);

        var orderedRollouts = rollouts
            .OrderBy(static rollout => rollout.FilePath, StringComparer.Ordinal)
            .ToArray();
        var candidatesBySession = orderedRollouts
            .Where(static rollout => rollout.RolloutMetadata?.SessionId is not null)
            .GroupBy(static rollout => rollout.RolloutMetadata!.SessionId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static rollout => rollout.FilePath, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var memo = new Dictionary<string, CodexInMemoryResolvedRollout>(StringComparer.Ordinal);

        CodexInMemoryResolvedRollout ResolveOne(
            CodexEpochRollout rollout,
            HashSet<string> visiting)
        {
            if (memo.TryGetValue(rollout.FilePath, out var cached))
            {
                return cached;
            }

            if (!visiting.Add(rollout.FilePath))
            {
                return FallbackOrUnresolved(rollout);
            }

            try
            {
                CandidateMatch? bestMatch = null;
                var parentSessionId = rollout.RolloutMetadata?.ParentSessionId;

                if (parentSessionId is not null
                    && candidatesBySession.TryGetValue(parentSessionId, out var candidates))
                {
                    foreach (var candidate in candidates)
                    {
                        if (string.Equals(
                                candidate.FilePath,
                                rollout.FilePath,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var parentResult = ResolveOne(candidate, visiting);
                        var parentHistoryView = parentResult.ResolvedRollout with
                        {
                            TokenEvents = parentResult.ResolvedHistory,
                        };
                        var trimResult = CodexForkReplayTrimmer.Trim(
                            parentHistoryView,
                            rollout);

                        if (trimResult.ReplayCount <= 0)
                        {
                            continue;
                        }

                        if (bestMatch is null
                            || trimResult.ReplayCount > bestMatch.TrimResult.ReplayCount)
                        {
                            bestMatch = new CandidateMatch(
                                candidate,
                                parentResult,
                                trimResult);
                        }
                    }
                }

                CodexInMemoryResolvedRollout result;
                if (bestMatch is not null)
                {
                    var inheritedHistory = bestMatch.ParentResult.ResolvedHistory
                        .Take(bestMatch.TrimResult.ReplayCount);
                    var resolvedHistory = inheritedHistory
                        .Concat(bestMatch.TrimResult.TrimmedChild.TokenEvents)
                        .ToArray();

                    result = new CodexInMemoryResolvedRollout(
                        rollout,
                        bestMatch.TrimResult.TrimmedChild,
                        resolvedHistory,
                        bestMatch.ParentRollout,
                        bestMatch.TrimResult.ReplayCount);
                }
                else
                {
                    result = FallbackOrUnresolved(rollout);
                }

                memo[rollout.FilePath] = result;
                return result;
            }
            finally
            {
                visiting.Remove(rollout.FilePath);
            }
        }

        var results = new List<CodexInMemoryResolvedRollout>(orderedRollouts.Length);
        foreach (var rollout in orderedRollouts)
        {
            results.Add(ResolveOne(
                rollout,
                new HashSet<string>(StringComparer.Ordinal)));
        }

        return results;
    }

    private static CodexInMemoryResolvedRollout Unresolved(CodexEpochRollout rollout) =>
        new(
            rollout,
            rollout,
            rollout.TokenEvents,
            SelectedParent: null,
            ReplayCount: 0);

    private static CodexInMemoryResolvedRollout FallbackOrUnresolved(
        CodexEpochRollout rollout)
    {
        if (rollout.RolloutMetadata?.ParentSessionId is null)
        {
            return Unresolved(rollout);
        }

        var fallback = CodexForkReplayFallbackHeuristic.Trim(rollout);
        return new CodexInMemoryResolvedRollout(
            rollout,
            fallback.TrimmedChild,
            fallback.TrimmedChild.TokenEvents,
            SelectedParent: null,
            fallback.ReplayCount);
    }

    private sealed record CandidateMatch(
        CodexEpochRollout ParentRollout,
        CodexInMemoryResolvedRollout ParentResult,
        CodexForkReplayTrimResult TrimResult);

}
