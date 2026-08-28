namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexEpochRollout(
    string FilePath,
    CodexSessionMetaParseResult? RolloutMetadata,
    IReadOnlyList<CodexEpochTokenEvent> TokenEvents);
