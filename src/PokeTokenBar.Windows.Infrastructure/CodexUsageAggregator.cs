using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexUsageAggregator
{
    public static CodexUsageEntry Sum(IEnumerable<CodexCanonicalEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        long inputTokens = 0;
        long outputTokens = 0;
        long cacheReadTokens = 0;
        long cacheWriteTokens = 0;

        foreach (var canonicalEvent in events)
        {
            ArgumentNullException.ThrowIfNull(canonicalEvent);
            var entry = canonicalEvent.TokenEvent.TokenEvent.TokenCount.Entry;

            inputTokens = checked(inputTokens + entry.InputTokens);
            outputTokens = checked(outputTokens + entry.OutputTokens);
            cacheReadTokens = checked(cacheReadTokens + entry.CacheReadTokens);
            cacheWriteTokens = checked(cacheWriteTokens + entry.CacheWriteTokens);
        }

        return new CodexUsageEntry(
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheWriteTokens);
    }
}
