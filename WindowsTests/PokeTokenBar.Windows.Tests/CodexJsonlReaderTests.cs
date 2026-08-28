using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexJsonlReaderTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public CodexJsonlReaderTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexJsonlReaderTests-{Guid.NewGuid():N}");
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
    public void Read_OneValidTokenCount_ReturnsOneResult()
    {
        var path = WriteFile(CreateTokenCountLine());

        var results = CodexJsonlReader.Read(path);

        Assert.Single(results);
    }

    [Fact]
    public void Read_MultipleValidTokenCounts_ReturnsAllInFileOrder()
    {
        var first = CreateTokenCountLine(
            timestamp: "2026-07-29T01:00:01.000Z",
            inputTokens: 100,
            cachedInputTokens: 20,
            outputTokens: 10);
        var second = CreateTokenCountLine(
            timestamp: "2026-07-29T01:00:02.000Z",
            inputTokens: 200,
            cachedInputTokens: 50,
            outputTokens: 20);
        var path = WriteFile(string.Join(Environment.NewLine, first, second));

        var results = CodexJsonlReader.Read(path);

        Assert.Equal(2, results.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T01:00:01.000Z"), results[0].Timestamp);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T01:00:02.000Z"), results[1].Timestamp);
        Assert.Equal(110, results[0].Entry.TotalTokens);
        Assert.Equal(220, results[1].Entry.TotalTokens);
    }

    [Fact]
    public void Read_NonTokenCountJson_IgnoresIt()
    {
        const string nonTokenCount =
            """{"type":"event_msg","timestamp":"2026-07-29T01:00:00.000Z","payload":{"type":"agent_message","info":{}}}""";
        var path = WriteFile(string.Join(
            Environment.NewLine,
            nonTokenCount,
            CreateTokenCountLine()));

        var results = CodexJsonlReader.Read(path);

        Assert.Single(results);
    }

    [Fact]
    public void Read_MalformedJsonInMiddle_IgnoresOnlyMalformedLine()
    {
        var first = CreateTokenCountLine(timestamp: "2026-07-29T01:00:01.000Z");
        var second = CreateTokenCountLine(timestamp: "2026-07-29T01:00:02.000Z");
        var path = WriteFile(string.Join(Environment.NewLine, first, "{not-json", second));

        var results = CodexJsonlReader.Read(path);

        Assert.Equal(2, results.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T01:00:02.000Z"), results[1].Timestamp);
    }

    [Fact]
    public void Read_BlankLines_IgnoresThem()
    {
        var path = WriteFile(
            Environment.NewLine
            + "   "
            + Environment.NewLine
            + CreateTokenCountLine()
            + Environment.NewLine);

        var results = CodexJsonlReader.Read(path);

        Assert.Single(results);
    }

    [Fact]
    public void Read_CompleteLastLineWithoutNewline_ProcessesIt()
    {
        var line = CreateTokenCountLine();
        var path = WriteFile(line);

        var results = CodexJsonlReader.Read(path);

        Assert.Single(results);
    }

    [Fact]
    public void Read_IncompleteLastJson_IgnoresIt()
    {
        var path = WriteFile(
            CreateTokenCountLine()
            + Environment.NewLine
            + "{\"timestamp\":\"2026-07-29T01:00:02.000Z\"");

        var results = CodexJsonlReader.Read(path);

        Assert.Single(results);
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmptyResult()
    {
        var path = WriteFile(string.Empty);

        var results = CodexJsonlReader.Read(path);

        Assert.Empty(results);
    }

    [Fact]
    public void Read_MissingFile_PropagatesFileNotFoundException()
    {
        var path = Path.Combine(_temporaryDirectory, "missing.jsonl");

        Assert.Throws<FileNotFoundException>(() => CodexJsonlReader.Read(path));
    }

    [Fact]
    public void Read_ResultMatchesDirectParserResult()
    {
        var line = CreateTokenCountLine(
            timestamp: "2026-07-29T09:08:07.654Z",
            inputTokens: 120,
            cachedInputTokens: 40,
            outputTokens: 10,
            reasoningOutputTokens: 2);
        Assert.True(CodexTokenCountParser.TryParse(line, out var expected));
        var path = WriteFile(line);

        var actual = Assert.Single(CodexJsonlReader.Read(path));

        Assert.Equal(expected, actual);
    }

    private string WriteFile(string contents)
    {
        var path = Path.Combine(_temporaryDirectory, "rollout.jsonl");
        File.WriteAllText(path, contents);
        return path;
    }

    private static string CreateTokenCountLine(
        string timestamp = "2026-07-29T01:00:00.000Z",
        long inputTokens = 120,
        long cachedInputTokens = 40,
        long outputTokens = 10,
        long reasoningOutputTokens = 2) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{"
        + "\"input_tokens\":" + inputTokens
        + ",\"cached_input_tokens\":" + cachedInputTokens
        + ",\"cache_write_input_tokens\":0"
        + ",\"output_tokens\":" + outputTokens
        + ",\"reasoning_output_tokens\":" + reasoningOutputTokens
        + ",\"total_tokens\":" + (inputTokens + outputTokens)
        + "}}}}";
}
