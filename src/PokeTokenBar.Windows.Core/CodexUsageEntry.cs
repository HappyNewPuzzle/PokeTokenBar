namespace PokeTokenBar.Windows.Core;

public readonly record struct CodexUsageEntry(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens)
{
    public long TotalTokens =>
        checked(InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens);
}
