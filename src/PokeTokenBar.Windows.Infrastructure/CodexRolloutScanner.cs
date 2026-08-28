namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexRolloutScanner
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<CodexParsedFile> ScanDefault() =>
        Scan(CodexSessionLocator.FindDefaultJsonlFiles());

    public static IReadOnlyList<CodexParsedFile> Scan(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var normalizedPaths = new HashSet<string>(PathComparer);
        foreach (var filePath in filePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            normalizedPaths.Add(Path.GetFullPath(filePath));
        }

        var parsedFiles = new List<CodexParsedFile>(normalizedPaths.Count);
        foreach (var filePath in normalizedPaths.Order(PathComparer))
        {
            IReadOnlyList<CodexTokenCountParseResult> tokenCountResults;
            try
            {
                tokenCountResults = CodexJsonlReader.Read(filePath);
            }
            catch (IOException)
            {
                tokenCountResults = Array.Empty<CodexTokenCountParseResult>();
            }
            catch (UnauthorizedAccessException)
            {
                tokenCountResults = Array.Empty<CodexTokenCountParseResult>();
            }

            parsedFiles.Add(new CodexParsedFile(filePath, tokenCountResults));
        }

        return parsedFiles;
    }
}
