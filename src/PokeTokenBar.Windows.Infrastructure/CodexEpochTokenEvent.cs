namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexEpochTokenEvent(
    CodexRolloutTokenEvent TokenEvent,
    int? Epoch);
