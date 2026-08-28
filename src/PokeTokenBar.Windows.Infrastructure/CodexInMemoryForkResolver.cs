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

        ResolutionOutcome ResolveOne(
            CodexEpochRollout rollout,
            HashSet<string> visiting)
        {
            if (memo.TryGetValue(rollout.FilePath, out var cached))
            {
                return new ResolutionOutcome(cached, IsCycleBlocked: false);
            }

            if (!visiting.Add(rollout.FilePath))
            {
                return new ResolutionOutcome(Unresolved(rollout), IsCycleBlocked: true);
            }

            try
            {
                CandidateMatch? bestMatch = null;
                var cycleBlockedCandidate = false;
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

                        var parentOutcome = ResolveOne(candidate, visiting);
                        if (parentOutcome.IsCycleBlocked)
                        {
                            cycleBlockedCandidate = true;
                            continue;
                        }

                        var parentHistoryView = parentOutcome.Result.ResolvedRollout with
                        {
                            TokenEvents = parentOutcome.Result.ResolvedHistory,
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
                                parentOutcome.Result,
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
                    result = Unresolved(rollout);
                }

                var isCycleBlocked = bestMatch is null && cycleBlockedCandidate;
                if (!isCycleBlocked)
                {
                    memo[rollout.FilePath] = result;
                }

                return new ResolutionOutcome(result, isCycleBlocked);
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
                new HashSet<string>(StringComparer.Ordinal)).Result);
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

    private sealed record CandidateMatch(
        CodexEpochRollout ParentRollout,
        CodexInMemoryResolvedRollout ParentResult,
        CodexForkReplayTrimResult TrimResult);

    private sealed record ResolutionOutcome(
        CodexInMemoryResolvedRollout Result,
        bool IsCycleBlocked);
}
