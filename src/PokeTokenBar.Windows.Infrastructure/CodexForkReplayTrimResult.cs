namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexForkReplayTrimResult(
    CodexEpochRollout TrimmedChild,
    int ReplayCount);
