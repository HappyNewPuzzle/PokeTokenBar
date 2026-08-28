namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexRolloutReader
{
    public static CodexParsedRollout Read(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        CodexSessionMetaParseResult? rolloutMetadata = null;
        CodexSessionMetaParseResult? currentSessionMetadata = null;
        var tokenEvents = new List<CodexRolloutTokenEvent>();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (CodexSessionMetaParser.TryParse(line, out var sessionMetadata))
            {
                if (rolloutMetadata?.SessionId is null)
                {
                    rolloutMetadata = sessionMetadata;
                }

                if (sessionMetadata.SessionId is not null
                    && !string.Equals(
                        sessionMetadata.SessionId,
                        currentSessionMetadata?.SessionId,
                        StringComparison.Ordinal))
                {
                    currentSessionMetadata = sessionMetadata;
                }
            }

            if (CodexTokenCountParser.TryParse(line, out var tokenCount))
            {
                tokenEvents.Add(new CodexRolloutTokenEvent(
                    tokenCount,
                    currentSessionMetadata?.SessionId,
                    currentSessionMetadata?.ParentSessionId,
                    currentSessionMetadata?.IsSubagent ?? false));
            }
        }

        return new CodexParsedRollout(
            absolutePath,
            rolloutMetadata,
            tokenEvents);
    }
}
