using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public class CodexSessionMetaParserTests
{
    [Fact]
    public void TryParse_Id_UsesItAsSessionId()
    {
        var result = Parse(CreateLine("""{"id":"session-from-id"}"""));

        Assert.Equal("session-from-id", result.SessionId);
    }

    [Fact]
    public void TryParse_MissingId_FallsBackToSessionId()
    {
        var result = Parse(CreateLine("""{"session_id":"fallback-session"}"""));

        Assert.Equal("fallback-session", result.SessionId);
    }

    [Fact]
    public void TryParse_IdAndSessionId_PrefersId()
    {
        var result = Parse(CreateLine(
            """{"id":"preferred","session_id":"fallback"}"""));

        Assert.Equal("preferred", result.SessionId);
    }

    [Fact]
    public void TryParse_ForkedFromId_UsesItAsParentSessionId()
    {
        var result = Parse(CreateLine(
            """{"id":"child","forked_from_id":"fork-parent"}"""));

        Assert.Equal("fork-parent", result.ParentSessionId);
    }

    [Fact]
    public void TryParse_MissingForkedFromId_FallsBackToParentThreadId()
    {
        var result = Parse(CreateLine(
            """{"id":"child","parent_thread_id":"thread-parent"}"""));

        Assert.Equal("thread-parent", result.ParentSessionId);
    }

    [Fact]
    public void TryParse_BothParentFields_PrefersForkedFromId()
    {
        var result = Parse(CreateLine(
            """{"id":"child","forked_from_id":"preferred","parent_thread_id":"fallback"}"""));

        Assert.Equal("preferred", result.ParentSessionId);
    }

    [Fact]
    public void TryParse_MissingParentFields_ReturnsNullParentSessionId()
    {
        var result = Parse(CreateLine("""{"id":"session"}"""));

        Assert.Null(result.ParentSessionId);
    }

    [Fact]
    public void TryParse_SubagentThreadSource_SetsIsSubagent()
    {
        var result = Parse(CreateLine(
            """{"id":"session","thread_source":"subagent"}"""));

        Assert.True(result.IsSubagent);
    }

    [Fact]
    public void TryParse_SourceObjectWithSubagentKey_SetsIsSubagentEvenWhenValueIsNull()
    {
        var result = Parse(CreateLine(
            """{"id":"session","source":{"subagent":null}}"""));

        Assert.True(result.IsSubagent);
    }

    [Fact]
    public void TryParse_RegularSession_IsNotSubagent()
    {
        var result = Parse(CreateLine(
            """{"id":"session","thread_source":"user","source":"cli"}"""));

        Assert.False(result.IsSubagent);
    }

    [Fact]
    public void TryParse_SourceStringSubagent_IsNotSubagent()
    {
        var result = Parse(CreateLine(
            """{"id":"session","source":"subagent"}"""));

        Assert.False(result.IsSubagent);
    }

    [Fact]
    public void TryParse_DifferentEventType_ReturnsFalse()
    {
        AssertParseFails(CreateLine("""{"id":"session"}""", "event_msg"));
    }

    [Fact]
    public void TryParse_MissingSessionId_StillReturnsMetadataWithNullId()
    {
        var result = Parse(CreateLine("{}"));

        Assert.Null(result.SessionId);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalseWithoutThrowing()
    {
        AssertParseFails("{not-json");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrBlank_ReturnsFalse(string? line)
    {
        AssertParseFails(line);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"payload\"")]
    [InlineData("[]")]
    [InlineData("123")]
    public void TryParse_NonObjectJson_ReturnsFalse(string line)
    {
        AssertParseFails(line);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"payload\"")]
    [InlineData("[]")]
    [InlineData("123")]
    public void TryParse_NonObjectPayload_ReturnsFalse(string payload)
    {
        AssertParseFails(CreateLine(payload));
    }

    [Fact]
    public void TryParse_WrongFieldTypes_UseValidFallbacksAndIgnoreInvalidSubagentFields()
    {
        var result = Parse(CreateLine(
            """{"id":123,"session_id":"session-fallback","forked_from_id":false,"parent_thread_id":"parent-fallback","thread_source":7,"source":"subagent"}"""));

        Assert.Equal("session-fallback", result.SessionId);
        Assert.Equal("parent-fallback", result.ParentSessionId);
        Assert.False(result.IsSubagent);
    }

    [Fact]
    public void TryParse_EmptyPreferredFields_UseFallbacks()
    {
        var result = Parse(CreateLine(
            """{"id":"","session_id":"session-fallback","forked_from_id":"","parent_thread_id":"parent-fallback"}"""));

        Assert.Equal("session-fallback", result.SessionId);
        Assert.Equal("parent-fallback", result.ParentSessionId);
    }

    [Fact]
    public void TryParse_WhitespacePreferredFields_ArePreservedLikeSwiftStrings()
    {
        var result = Parse(CreateLine(
            """{"id":"   ","session_id":"fallback","forked_from_id":" ","parent_thread_id":"parent"}"""));

        Assert.Equal("   ", result.SessionId);
        Assert.Equal(" ", result.ParentSessionId);
    }

    [Fact]
    public void TryParse_MissingTimestamp_StillSucceeds()
    {
        var result = Parse(CreateLine("""{"id":"session"}"""));

        Assert.Equal("session", result.SessionId);
    }

    private static CodexSessionMetaParseResult Parse(string line)
    {
        Assert.True(CodexSessionMetaParser.TryParse(line, out var result));
        return Assert.IsType<CodexSessionMetaParseResult>(result);
    }

    private static void AssertParseFails(string? line)
    {
        Assert.False(CodexSessionMetaParser.TryParse(line, out var result));
        Assert.Null(result);
    }

    private static string CreateLine(string payload, string type = "session_meta") =>
        "{\"type\":\"" + type + "\",\"payload\":" + payload + "}";
}
