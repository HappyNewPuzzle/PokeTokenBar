namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexForkDependencyExpansion(
    IReadOnlyList<CodexEpochRollout> PrimaryRollouts,
    IReadOnlyList<CodexEpochRollout> DependencyRollouts,
    IReadOnlyList<CodexEpochRollout> ResolutionRollouts);
