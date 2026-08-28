namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexForkDependencyExpander
{
    public static CodexForkDependencyExpansion Expand(
        IEnumerable<CodexEpochRollout> primaryRollouts,
        IEnumerable<CodexEpochRollout> availableRollouts)
    {
        ArgumentNullException.ThrowIfNull(primaryRollouts);
        ArgumentNullException.ThrowIfNull(availableRollouts);

        var primaryByPath = DistinctByPath(primaryRollouts);
        var availableByPath = DistinctByPath(availableRollouts);
        var selectedByPath = new Dictionary<string, CodexEpochRollout>(
            primaryByPath,
            StringComparer.Ordinal);
        var dependenciesByPath = new Dictionary<string, CodexEpochRollout>(
            StringComparer.Ordinal);

        var availableBySession = availableByPath.Values
            .Where(static rollout => rollout.RolloutMetadata?.SessionId is not null)
            .GroupBy(
                static rollout => rollout.RolloutMetadata!.SessionId!,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static rollout => rollout.FilePath, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var pendingParentIds = new SortedSet<string>(StringComparer.Ordinal);
        AddParentIds(primaryByPath.Values, pendingParentIds);
        var searchedParentIds = new HashSet<string>(StringComparer.Ordinal);

        while (pendingParentIds.Count > 0)
        {
            var parentSessionId = pendingParentIds.Min!;
            pendingParentIds.Remove(parentSessionId);

            if (!searchedParentIds.Add(parentSessionId))
            {
                continue;
            }

            if (selectedByPath.Values.Any(rollout =>
                    string.Equals(
                        rollout.RolloutMetadata?.SessionId,
                        parentSessionId,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            if (!availableBySession.TryGetValue(parentSessionId, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (selectedByPath.ContainsKey(candidate.FilePath))
                {
                    continue;
                }

                selectedByPath.Add(candidate.FilePath, candidate);
                dependenciesByPath.Add(candidate.FilePath, candidate);

                if (candidate.RolloutMetadata?.ParentSessionId is { } ancestorSessionId)
                {
                    pendingParentIds.Add(ancestorSessionId);
                }
            }
        }

        return new CodexForkDependencyExpansion(
            OrderByPath(primaryByPath.Values),
            OrderByPath(dependenciesByPath.Values),
            OrderByPath(selectedByPath.Values));
    }

    private static Dictionary<string, CodexEpochRollout> DistinctByPath(
        IEnumerable<CodexEpochRollout> rollouts)
    {
        var result = new Dictionary<string, CodexEpochRollout>(StringComparer.Ordinal);
        foreach (var rollout in rollouts)
        {
            ArgumentNullException.ThrowIfNull(rollout);
            result.TryAdd(rollout.FilePath, rollout);
        }

        return result;
    }

    private static void AddParentIds(
        IEnumerable<CodexEpochRollout> rollouts,
        ISet<string> parentIds)
    {
        foreach (var rollout in rollouts)
        {
            if (rollout.RolloutMetadata?.ParentSessionId is { } parentSessionId)
            {
                parentIds.Add(parentSessionId);
            }
        }
    }

    private static IReadOnlyList<CodexEpochRollout> OrderByPath(
        IEnumerable<CodexEpochRollout> rollouts) =>
        rollouts
            .OrderBy(static rollout => rollout.FilePath, StringComparer.Ordinal)
            .ToArray();
}
