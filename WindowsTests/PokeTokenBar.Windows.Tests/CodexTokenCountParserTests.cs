using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public class CodexTokenCountParserTests
{
    [Fact]
    public void TryParse_ValidTokenCount_MapsEntryWithoutAddingReasoningTokens()
    {
        var line = CreateLine(
            lastUsage:
                """{"input_tokens":120,"cached_input_tokens":40,"cache_write_input_tokens":7,"output_tokens":10,"reasoning_output_tokens":2,"total_tokens":130}""");

        var result = Parse(line);

        Assert.Equal(DateTimeOffset.Parse("2026-07-29T01:00:00.000Z"), result.Timestamp);
        Assert.Equal(80, result.Entry.InputTokens);
        Assert.Equal(40, result.Entry.CacheReadTokens);
        Assert.Equal(10, result.Entry.OutputTokens);
        Assert.Equal(0, result.Entry.CacheWriteTokens);
        Assert.Equal(130, result.Entry.TotalTokens);
        Assert.Equal(2, result.LastUsageVector.ReasoningOutputTokens);
        Assert.Equal(7, result.LastUsageVector.CacheWriteInputTokens);
        Assert.Null(result.CumulativeUsageVector);
    }

    [Fact]
    public void TryParse_TotalTokenUsage_MapsAllCumulativeVectorFields()
    {
        var line = CreateLine(
            lastUsage: "{}",
            cumulativeUsage:
                """{"input_tokens":300,"cached_input_tokens":100,"cache_write_input_tokens":4,"output_tokens":20,"reasoning_output_tokens":5,"total_tokens":320}""");

        var result = Parse(line);

        Assert.Equal(
            new CodexUsageVector(300, 100, 4, 20, 5, 320),
            result.CumulativeUsageVector);
    }

    [Fact]
    public void TryParse_NonTokenCountPayload_ReturnsFalse()
    {
        var line = CreateLine(lastUsage: "{}", payloadType: "agent_message");

        AssertParseFails(line);
    }

    [Fact]
    public void TryParse_MissingLastTokenUsage_ReturnsFalse()
    {
        const string line =
            """{"timestamp":"2026-07-29T01:00:00.000Z","payload":{"type":"token_count","info":{}}}""";

        AssertParseFails(line);
    }

    [Fact]
    public void TryParse_InvalidTimestamp_ReturnsFalse()
    {
        var line = CreateLine(lastUsage: "{}", timestamp: "not-a-timestamp");

        AssertParseFails(line);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalseWithoutThrowing()
    {
        const string line = "{not-json";

        AssertParseFails(line);
    }

    [Fact]
    public void TryParse_NegativeValues_NormalizesThemToZero()
    {
        var line = CreateLine(
            lastUsage:
                """{"input_tokens":-1,"cached_input_tokens":-2,"cache_write_input_tokens":-3,"output_tokens":-4,"reasoning_output_tokens":-5,"total_tokens":-6}""");

        var result = Parse(line);

        Assert.Equal(new CodexUsageVector(0, 0, 0, 0, 0, 0), result.LastUsageVector);
        Assert.Equal(0, result.Entry.TotalTokens);
    }

    [Fact]
    public void TryParse_VeryLargeValues_ClampsThemToMaximumTokenValue()
    {
        var line = CreateLine(
            lastUsage:
                """{"input_tokens":1e30,"cached_input_tokens":0,"output_tokens":0,"total_tokens":1e30}""");

        var result = Parse(line);

        Assert.Equal(CodexTokenCountParser.MaximumTokenValue, result.LastUsageVector.InputTokens);
        Assert.Equal(CodexTokenCountParser.MaximumTokenValue, result.LastUsageVector.TotalTokens);
        Assert.Equal(CodexTokenCountParser.MaximumTokenValue, result.Entry.TotalTokens);
    }

    [Fact]
    public void TryParse_CachedInputGreaterThanInput_ClampsEntryInputToZero()
    {
        var line = CreateLine(
            lastUsage:
                """{"input_tokens":40,"cached_input_tokens":80,"output_tokens":10,"total_tokens":50}""");

        var result = Parse(line);

        Assert.Equal(0, result.Entry.InputTokens);
        Assert.Equal(80, result.Entry.CacheReadTokens);
        Assert.Equal(90, result.Entry.TotalTokens);
    }

    [Fact]
    public void TryParse_ReasoningOnlyChange_IsPreservedInLastUsageVector()
    {
        var first = Parse(CreateLine(lastUsage: """{"reasoning_output_tokens":2}"""));
        var second = Parse(CreateLine(lastUsage: """{"reasoning_output_tokens":3}"""));

        Assert.Equal(2, first.LastUsageVector.ReasoningOutputTokens);
        Assert.Equal(3, second.LastUsageVector.ReasoningOutputTokens);
        Assert.NotEqual(first.LastUsageVector, second.LastUsageVector);
        Assert.Equal(0, first.Entry.TotalTokens);
        Assert.Equal(0, second.Entry.TotalTokens);
    }

    [Fact]
    public void TryParse_TopLevelTypeDoesNotHaveToBeEventMsg()
    {
        var line = CreateLine(lastUsage: "{}", topLevelType: "other");

        Assert.True(CodexTokenCountParser.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_MissingNullAndNonNumericValues_NormalizesThemToZero()
    {
        var line = CreateLine(
            lastUsage:
                """{"cached_input_tokens":null,"output_tokens":"10","reasoning_output_tokens":true}""");

        var result = Parse(line);

        Assert.Equal(new CodexUsageVector(0, 0, 0, 0, 0, 0), result.LastUsageVector);
        Assert.Equal(0, result.Entry.TotalTokens);
    }

    private static CodexTokenCountParseResult Parse(string line)
    {
        Assert.True(CodexTokenCountParser.TryParse(line, out var result));
        return Assert.IsType<CodexTokenCountParseResult>(result);
    }

    private static void AssertParseFails(string line)
    {
        Assert.False(CodexTokenCountParser.TryParse(line, out var result));
        Assert.Null(result);
    }

    private static string CreateLine(
        string lastUsage,
        string? cumulativeUsage = null,
        string timestamp = "2026-07-29T01:00:00.000Z",
        string payloadType = "token_count",
        string topLevelType = "event_msg")
    {
        var cumulativeProperty = cumulativeUsage is null
            ? string.Empty
            : $",\"total_token_usage\":{cumulativeUsage}";

        return "{\"type\":\"" + topLevelType
            + "\",\"timestamp\":\"" + timestamp
            + "\",\"payload\":{\"type\":\"" + payloadType
            + "\",\"info\":{\"last_token_usage\":" + lastUsage
            + cumulativeProperty
            + "}}}";
    }
}
