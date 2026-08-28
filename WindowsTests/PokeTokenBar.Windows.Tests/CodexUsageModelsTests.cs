using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public class CodexUsageModelsTests
{
    [Fact]
    public void Entry_TotalTokens_SumsAllStoredTokenCategories()
    {
        var entry = new CodexUsageEntry(
            InputTokens: 80,
            OutputTokens: 10,
            CacheReadTokens: 40,
            CacheWriteTokens: 0);

        Assert.Equal(130, entry.TotalTokens);
    }

    [Fact]
    public void Vector_Equality_UsesAllSixFields()
    {
        var first = CreateVector();
        var identical = CreateVector();
        var differentReasoning = CreateVector(reasoningOutputTokens: 6);

        Assert.Equal(first, identical);
        Assert.NotEqual(first, differentReasoning);
    }

    [Fact]
    public void Vector_HasDecreasedFrom_DetectsAnyLowerField()
    {
        var previous = CreateVector();
        var current = CreateVector(inputTokens: 90);

        Assert.True(current.HasDecreasedFrom(previous));
    }

    [Fact]
    public void Vector_HasDecreasedFrom_ReturnsFalseWhenAllFieldsAreEqual()
    {
        var previous = CreateVector();
        var current = CreateVector();

        Assert.False(current.HasDecreasedFrom(previous));
    }

    [Fact]
    public void Vector_HasDecreasedFrom_ReturnsFalseWhenAllFieldsIncreaseOrStayEqual()
    {
        var previous = CreateVector();
        var current = new CodexUsageVector(
            InputTokens: 110,
            CachedInputTokens: 60,
            CacheWriteInputTokens: 0,
            OutputTokens: 30,
            ReasoningOutputTokens: 6,
            TotalTokens: 140);

        Assert.False(current.HasDecreasedFrom(previous));
    }

    private static CodexUsageVector CreateVector(
        long inputTokens = 100,
        long cachedInputTokens = 50,
        long cacheWriteInputTokens = 0,
        long outputTokens = 20,
        long reasoningOutputTokens = 5,
        long totalTokens = 120) =>
        new(
            inputTokens,
            cachedInputTokens,
            cacheWriteInputTokens,
            outputTokens,
            reasoningOutputTokens,
            totalTokens);
}
