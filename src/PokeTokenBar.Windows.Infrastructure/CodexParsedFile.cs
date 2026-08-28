namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexParsedFile(
    string FilePath,
    IReadOnlyList<CodexTokenCountParseResult> TokenCountResults);
