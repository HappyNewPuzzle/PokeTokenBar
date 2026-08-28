namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexCanonicalPrimaryFinalizer
{
    public static IReadOnlyList<CodexCanonicalEvent> CreateCanonicalEvents(
        CodexLocalRolloutPipelineResult pipelineResult)
    {
        ArgumentNullException.ThrowIfNull(pipelineResult);

        return CreateCanonicalEvents(pipelineResult.ResolvedPrimaryRollouts);
    }

    public static IReadOnlyList<CodexCanonicalEvent> CreateCanonicalEvents(
        IEnumerable<CodexInMemoryResolvedRollout> resolvedPrimaryRollouts)
    {
        ArgumentNullException.ThrowIfNull(resolvedPrimaryRollouts);

        var resolvedRollouts = resolvedPrimaryRollouts
            .Select(static result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return CodexCumulativeEpochAssigner.ReassignOwnedEvents(
                    result.ResolvedRollout);
            });

        return CodexCrossFileCanonicalDeduplicator.Deduplicate(resolvedRollouts);
    }
}
