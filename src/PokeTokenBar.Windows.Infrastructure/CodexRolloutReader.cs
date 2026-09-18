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
        var currentModel = "unknown";
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
                    currentModel = "unknown";
                }
            }

            if (line.Contains("\"model\"", StringComparison.Ordinal) &&
                TryParseModel(line) is { } model)
            {
                currentModel = model;
            }

            if (CodexTokenCountParser.TryParse(line, out var tokenCount))
            {
                tokenEvents.Add(new CodexRolloutTokenEvent(
                    tokenCount with { Model = currentModel },
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

    private static string? TryParseModel(string line)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            if (payload.TryGetProperty("model", out var model) &&
                model.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return model.GetString();
            }

            return payload.TryGetProperty("turn_context", out var context) &&
                   context.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   context.TryGetProperty("model", out model) &&
                   model.ValueKind == System.Text.Json.JsonValueKind.String
                ? model.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
