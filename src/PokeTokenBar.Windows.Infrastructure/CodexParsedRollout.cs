namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexParsedRollout(
    string FilePath,
    CodexSessionMetaParseResult? RolloutMetadata,
    IReadOnlyList<CodexRolloutTokenEvent> TokenEvents);
