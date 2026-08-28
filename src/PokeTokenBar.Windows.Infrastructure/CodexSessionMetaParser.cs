using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexSessionMetaParser
{
    public static bool TryParse(
        string? line,
        [NotNullWhen(true)] out CodexSessionMetaParseResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "session_meta"
                || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var sessionId = GetNonEmptyString(payload, "id")
                ?? GetNonEmptyString(payload, "session_id");
            var parentSessionId = GetNonEmptyString(payload, "forked_from_id")
                ?? GetNonEmptyString(payload, "parent_thread_id");

            var isSubagent = GetString(payload, "thread_source") == "subagent"
                || HasSubagentSourceKey(payload);

            result = new CodexSessionMetaParseResult(
                sessionId,
                parentSessionId,
                isSubagent);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetNonEmptyString(JsonElement payload, string propertyName)
    {
        var value = GetString(payload, propertyName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? GetString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool HasSubagentSourceKey(JsonElement payload)
    {
        return payload.TryGetProperty("source", out var source)
            && source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("subagent", out _);
    }
}
