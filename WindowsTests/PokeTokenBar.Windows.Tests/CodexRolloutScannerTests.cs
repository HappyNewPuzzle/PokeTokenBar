using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexRolloutScannerTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public CodexRolloutScannerTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexRolloutScannerTests-{Guid.NewGuid():N}");
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
    public void Scan_OneJsonlFile_ReturnsOneParsedFile()
    {
        var path = WriteFile("one.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([path]);

        var parsedFile = Assert.Single(parsedFiles);
        Assert.Equal(path, parsedFile.FilePath);
        Assert.Single(parsedFile.TokenCountResults);
    }

    [Fact]
    public void Scan_MultipleFiles_ReturnsEveryFile()
    {
        var first = WriteFile("first.jsonl", CreateTokenCountLine());
        var second = WriteFile("second.jsonl", CreateTokenCountLine());
        var third = WriteFile("third.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([first, second, third]);

        Assert.Equal(3, parsedFiles.Count);
        Assert.Equal(
            SortPaths(first, second, third),
            parsedFiles.Select(file => file.FilePath));
    }

    [Fact]
    public void Scan_PreservesTokenCountResultsWithinTheirSourceFile()
    {
        var first = WriteFile(
            "first.jsonl",
            CreateTokenCountLine(outputTokens: 10));
        var second = WriteFile(
            "second.jsonl",
            CreateTokenCountLine(outputTokens: 20));

        var parsedFiles = CodexRolloutScanner.Scan([first, second]);

        var firstResult = parsedFiles.Single(file => file.FilePath == first);
        var secondResult = parsedFiles.Single(file => file.FilePath == second);
        Assert.Equal(10, Assert.Single(firstResult.TokenCountResults).Entry.OutputTokens);
        Assert.Equal(20, Assert.Single(secondResult.TokenCountResults).Entry.OutputTokens);
    }

    [Fact]
    public void Scan_FilesWithTwoAndOneEvents_PreservesSeparateCounts()
    {
        var twoEvents = WriteFile(
            "two.jsonl",
            string.Join(
                Environment.NewLine,
                CreateTokenCountLine(timestamp: "2026-07-29T01:00:01.000Z"),
                CreateTokenCountLine(timestamp: "2026-07-29T01:00:02.000Z")));
        var oneEvent = WriteFile("one.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([twoEvents, oneEvent]);

        Assert.Equal(2, parsedFiles.Single(file => file.FilePath == twoEvents).TokenCountResults.Count);
        Assert.Single(parsedFiles.Single(file => file.FilePath == oneEvent).TokenCountResults);
    }

    [Fact]
    public void Scan_FileWithoutTokenCounts_PreservesFileWithEmptyResults()
    {
        var path = WriteFile(
            "no-token-count.jsonl",
            """{"type":"event_msg","payload":{"type":"agent_message"}}""");

        var parsedFile = Assert.Single(CodexRolloutScanner.Scan([path]));

        Assert.Equal(path, parsedFile.FilePath);
        Assert.Empty(parsedFile.TokenCountResults);
    }

    [Fact]
    public void Scan_ReturnsDeterministicOrdinalIgnoreCaseFileOrder()
    {
        var last = WriteFile("z.jsonl", CreateTokenCountLine());
        var first = WriteFile("A.jsonl", CreateTokenCountLine());
        var middle = WriteFile("m.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([last, middle, first]);

        Assert.Equal(
            new[] { first, middle, last },
            parsedFiles.Select(file => file.FilePath));
    }

    [Fact]
    public void Scan_DuplicateFilePath_ScansItOnlyOnce()
    {
        var path = WriteFile("duplicate.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([path, path]);

        Assert.Single(parsedFiles);
    }

    [Fact]
    public void Scan_MissingFile_PreservesEmptyFileAndContinuesOtherFiles()
    {
        var missing = Path.Combine(_temporaryDirectory, "missing.jsonl");
        var valid = WriteFile("valid.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([missing, valid]);

        Assert.Equal(2, parsedFiles.Count);
        Assert.Empty(parsedFiles.Single(file => file.FilePath == missing).TokenCountResults);
        Assert.Single(parsedFiles.Single(file => file.FilePath == valid).TokenCountResults);
    }

    [Fact]
    public void Scan_MalformedOnlyFile_DoesNotDiscardAnotherValidFile()
    {
        var malformed = WriteFile("malformed.jsonl", "{not-json");
        var valid = WriteFile("valid.jsonl", CreateTokenCountLine());

        var parsedFiles = CodexRolloutScanner.Scan([malformed, valid]);

        Assert.Equal(2, parsedFiles.Count);
        Assert.Empty(parsedFiles.Single(file => file.FilePath == malformed).TokenCountResults);
        Assert.Single(parsedFiles.Single(file => file.FilePath == valid).TokenCountResults);
    }

    [Fact]
    public void Scan_RelativeInputPath_PreservesAbsoluteFilePath()
    {
        var absolutePath = WriteFile("absolute.jsonl", CreateTokenCountLine());
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), absolutePath);

        var parsedFile = Assert.Single(CodexRolloutScanner.Scan([relativePath]));

        Assert.True(Path.IsPathFullyQualified(parsedFile.FilePath));
        Assert.Equal(absolutePath, parsedFile.FilePath);
    }

    private string WriteFile(string fileName, string contents)
    {
        var path = Path.GetFullPath(Path.Combine(_temporaryDirectory, fileName));
        File.WriteAllText(path, contents);
        return path;
    }

    private static string[] SortPaths(params string[] paths) =>
        paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string CreateTokenCountLine(
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
