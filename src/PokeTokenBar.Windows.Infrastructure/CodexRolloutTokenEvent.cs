namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexRolloutTokenEvent(
    CodexTokenCountParseResult TokenCount,
    string? SessionId,
    string? ParentSessionId,
    bool IsSubagent);
