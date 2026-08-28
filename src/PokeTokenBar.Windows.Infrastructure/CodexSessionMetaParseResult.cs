namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexSessionMetaParseResult(
    string? SessionId,
    string? ParentSessionId,
    bool IsSubagent);
