namespace PokeTokenBar.Windows.Core;

public readonly record struct CodexUsageVector(
    long InputTokens,
    long CachedInputTokens,
    long CacheWriteInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long TotalTokens)
{
    public bool HasDecreasedFrom(CodexUsageVector previous) =>
        InputTokens < previous.InputTokens
        || CachedInputTokens < previous.CachedInputTokens
        || CacheWriteInputTokens < previous.CacheWriteInputTokens
        || OutputTokens < previous.OutputTokens
        || ReasoningOutputTokens < previous.ReasoningOutputTokens
        || TotalTokens < previous.TotalTokens;
}
