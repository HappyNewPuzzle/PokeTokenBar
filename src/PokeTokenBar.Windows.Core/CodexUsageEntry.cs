namespace PokeTokenBar.Windows.Core;

public readonly record struct CodexUsageEntry(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens)
{
    public long TotalTokens =>
        InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
}
