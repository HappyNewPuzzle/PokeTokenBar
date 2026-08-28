using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexRolloutReaderTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public CodexRolloutReaderTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexRolloutReaderTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void Read_SessionMetaThenToken_AssociatesTokenWithSession()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount());

        var rollout = CodexRolloutReader.Read(path);

        var tokenEvent = Assert.Single(rollout.TokenEvents);
        Assert.Equal("session-a", tokenEvent.SessionId);
        Assert.Equal(Path.GetFullPath(path), rollout.FilePath);
    }

    [Fact]
    public void Read_OneSessionAndTwoTokens_AssociatesBothWithSession()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:01.000Z"),
            TokenCount(timestamp: "2026-07-29T01:00:02.000Z"));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal(2, rollout.TokenEvents.Count);
        Assert.All(rollout.TokenEvents, tokenEvent => Assert.Equal("session-a", tokenEvent.SessionId));
    }

    [Fact]
    public void Read_SessionChanges_AssociatesTokensWithSessionAtEventTime()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:01.000Z"),
            SessionMeta("""{"id":"session-b"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:02.000Z"));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", rollout.TokenEvents[0].SessionId);
        Assert.Equal("session-b", rollout.TokenEvents[1].SessionId);
    }

    [Fact]
    public void Read_FirstSessionMeta_IsPreservedAsRolloutMetadata()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a","forked_from_id":"parent-a"}"""),
            SessionMeta("""{"id":"session-b"}"""));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", rollout.RolloutMetadata?.SessionId);
        Assert.Equal("parent-a", rollout.RolloutMetadata?.ParentSessionId);
    }

    [Fact]
    public void Read_LaterSessionMeta_ChangesCurrentSessionButNotRolloutMetadata()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:01.000Z"),
            SessionMeta("""{"id":"session-b"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:02.000Z"));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", rollout.RolloutMetadata?.SessionId);
        Assert.Equal("session-b", rollout.TokenEvents[1].SessionId);
    }

    [Fact]
    public void Read_ParentSessionId_IsPreservedOnTokenEvent()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"child","forked_from_id":"parent"}"""),
            TokenCount());

        var tokenEvent = Assert.Single(CodexRolloutReader.Read(path).TokenEvents);

        Assert.Equal("parent", tokenEvent.ParentSessionId);
    }

    [Fact]
    public void Read_SubagentFlag_IsPreservedOnTokenEvent()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"child","thread_source":"subagent"}"""),
            TokenCount());

        var tokenEvent = Assert.Single(CodexRolloutReader.Read(path).TokenEvents);

        Assert.True(tokenEvent.IsSubagent);
    }

    [Fact]
    public void Read_IdlessSessionMeta_DoesNotClearCurrentSession()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            SessionMeta("""{"thread_source":"subagent"}"""),
            TokenCount());

        var tokenEvent = Assert.Single(CodexRolloutReader.Read(path).TokenEvents);

        Assert.Equal("session-a", tokenEvent.SessionId);
        Assert.False(tokenEvent.IsSubagent);
    }

    [Fact]
    public void Read_IdlessFirstMeta_IsReplacedAsRolloutMetadataWhenIdAppears()
    {
        var path = WriteLines(
            SessionMeta("""{"parent_thread_id":"orphan-parent"}"""),
            SessionMeta("""{"id":"session-a"}"""));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", rollout.RolloutMetadata?.SessionId);
        Assert.Null(rollout.RolloutMetadata?.ParentSessionId);
    }

    [Fact]
    public void Read_TokenBeforeSessionMeta_PreservesNullSessionThenUsesLaterSession()
    {
        var path = WriteLines(
            TokenCount(timestamp: "2026-07-29T01:00:01.000Z"),
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:02.000Z"));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Null(rollout.TokenEvents[0].SessionId);
        Assert.Null(rollout.TokenEvents[0].ParentSessionId);
        Assert.False(rollout.TokenEvents[0].IsSubagent);
        Assert.Equal("session-a", rollout.TokenEvents[1].SessionId);
    }

    [Fact]
    public void Read_SessionMetaOnly_PreservesMetadataWithNoTokenEvents()
    {
        var path = WriteLines(SessionMeta("""{"id":"session-a"}"""));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", rollout.RolloutMetadata?.SessionId);
        Assert.Empty(rollout.TokenEvents);
    }

    [Fact]
    public void Read_TokenCountOnly_PreservesEventWithoutMetadata()
    {
        var path = WriteLines(TokenCount());

        var rollout = CodexRolloutReader.Read(path);

        Assert.Null(rollout.RolloutMetadata);
        Assert.Null(Assert.Single(rollout.TokenEvents).SessionId);
    }

    [Fact]
    public void Read_MalformedLines_DoNotStopLaterMetaAndTokenProcessing()
    {
        var path = WriteLines(
            "{not-json",
            SessionMeta("""{"id":"session-a"}"""),
            "still-not-json",
            TokenCount());

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal("session-a", Assert.Single(rollout.TokenEvents).SessionId);
    }

    [Fact]
    public void Read_BlankLines_AreIgnored()
    {
        var path = WriteLines(
            string.Empty,
            "   ",
            SessionMeta("""{"id":"session-a"}"""),
            string.Empty,
            TokenCount());

        var rollout = CodexRolloutReader.Read(path);

        Assert.Single(rollout.TokenEvents);
    }

    [Fact]
    public void Read_CompleteLastLineWithoutNewline_ProcessesIt()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount());

        var rollout = CodexRolloutReader.Read(path);

        Assert.Single(rollout.TokenEvents);
    }

    [Fact]
    public void Read_IncompleteLastJson_IgnoresIt()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(),
            "{\"type\":\"event_msg\"");

        var rollout = CodexRolloutReader.Read(path);

        Assert.Single(rollout.TokenEvents);
    }

    [Fact]
    public void Read_OtherEventTypes_AreIgnored()
    {
        const string otherEvent =
            """{"type":"event_msg","payload":{"type":"agent_message"}}""";
        var path = WriteLines(
            otherEvent,
            SessionMeta("""{"id":"session-a"}"""),
            otherEvent,
            TokenCount(),
            otherEvent);

        var rollout = CodexRolloutReader.Read(path);

        Assert.Single(rollout.TokenEvents);
    }

    [Fact]
    public void Read_TokenEvents_PreserveFileOrder()
    {
        var path = WriteLines(
            SessionMeta("""{"id":"session-a"}"""),
            TokenCount(timestamp: "2026-07-29T01:00:03.000Z", outputTokens: 30),
            TokenCount(timestamp: "2026-07-29T01:00:01.000Z", outputTokens: 10),
            TokenCount(timestamp: "2026-07-29T01:00:02.000Z", outputTokens: 20));

        var rollout = CodexRolloutReader.Read(path);

        Assert.Equal(
            new long[] { 30, 10, 20 },
            rollout.TokenEvents.Select(tokenEvent => tokenEvent.TokenCount.Entry.OutputTokens));
    }

    [Fact]
    public void Read_MissingFile_PropagatesFileNotFoundException()
    {
        var path = Path.Combine(_temporaryDirectory, "missing.jsonl");

        Assert.Throws<FileNotFoundException>(() => CodexRolloutReader.Read(path));
    }

    private string WriteLines(params string[] lines)
    {
        var path = Path.Combine(_temporaryDirectory, "rollout.jsonl");
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        return Path.GetFullPath(path);
    }

    private static string SessionMeta(string payload) =>
        "{\"type\":\"session_meta\",\"payload\":" + payload + "}";

    private static string TokenCount(
        string timestamp = "2026-07-29T01:00:00.000Z",
        long outputTokens = 10) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{"
        + "\"input_tokens\":120,\"cached_input_tokens\":40"
        + ",\"cache_write_input_tokens\":0"
        + ",\"output_tokens\":" + outputTokens
        + ",\"reasoning_output_tokens\":2"
        + ",\"total_tokens\":" + (120 + outputTokens)
        + "}}}}";
}
