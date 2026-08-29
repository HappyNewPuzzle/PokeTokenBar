namespace PokeTokenBar.Windows.Core;

public sealed record DailyUsage(
    string Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    double TotalCost);
