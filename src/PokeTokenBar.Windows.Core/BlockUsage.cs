namespace PokeTokenBar.Windows.Core;

public sealed record BlockUsage(
    string Id,
    string StartTime,
    string EndTime,
    bool IsActive,
    long TotalTokens,
    double CostUSD,
    double? TokensPerMinute);
