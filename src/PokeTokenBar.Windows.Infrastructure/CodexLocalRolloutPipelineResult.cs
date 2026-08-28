namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexLocalRolloutPipelineResult(
    CodexForkDependencyExpansion Expansion,
    IReadOnlyList<CodexInMemoryResolvedRollout> ResolutionResults,
    IReadOnlyList<CodexInMemoryResolvedRollout> ResolvedPrimaryRollouts)
{
    public IReadOnlyList<CodexEpochRollout> PrimaryRollouts =>
        Expansion.PrimaryRollouts;

    public IReadOnlyList<CodexEpochRollout> DependencyRollouts =>
        Expansion.DependencyRollouts;

    public IReadOnlyList<CodexEpochRollout> ResolutionRollouts =>
        Expansion.ResolutionRollouts;
}
