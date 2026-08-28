using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexTokenCountParser
{
    public const long MaximumTokenValue = 1_000_000_000_000_000;

    public static bool TryParse(
        string? line,
        [NotNullWhen(true)] out CodexTokenCountParseResult? result)
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
                || !TryGetObject(root, "payload", out var payload)
                || !payload.TryGetProperty("type", out var payloadType)
                || payloadType.ValueKind != JsonValueKind.String
                || payloadType.GetString() != "token_count"
                || !TryGetObject(payload, "info", out var info)
                || !TryGetObject(info, "last_token_usage", out var lastUsage)
                || !root.TryGetProperty("timestamp", out var timestampElement)
                || timestampElement.ValueKind != JsonValueKind.String
                || !timestampElement.TryGetDateTimeOffset(out var timestamp))
            {
                return false;
            }

            var lastUsageVector = ParseUsageVector(lastUsage);
            var entry = new CodexUsageEntry(
                InputTokens: Math.Max(
                    0,
                    lastUsageVector.InputTokens - lastUsageVector.CachedInputTokens),
                OutputTokens: lastUsageVector.OutputTokens,
                CacheReadTokens: lastUsageVector.CachedInputTokens,
                CacheWriteTokens: 0);

            CodexUsageVector? cumulativeUsageVector = null;
            if (TryGetObject(info, "total_token_usage", out var cumulativeUsage))
            {
                cumulativeUsageVector = ParseUsageVector(cumulativeUsage);
            }

            result = new CodexTokenCountParseResult(
                timestamp,
                entry,
                lastUsageVector,
                cumulativeUsageVector);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CodexUsageVector ParseUsageVector(JsonElement usage) =>
        new(
            InputTokens: ReadTokenValue(usage, "input_tokens"),
            CachedInputTokens: ReadTokenValue(usage, "cached_input_tokens"),
            CacheWriteInputTokens: ReadTokenValue(usage, "cache_write_input_tokens"),
            OutputTokens: ReadTokenValue(usage, "output_tokens"),
            ReasoningOutputTokens: ReadTokenValue(usage, "reasoning_output_tokens"),
            TotalTokens: ReadTokenValue(usage, "total_tokens"));

    private static long ReadTokenValue(JsonElement usage, string propertyName)
    {
        if (!usage.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number <= 0)
        {
            return 0;
        }

        return number >= MaximumTokenValue ? MaximumTokenValue : (long)number;
    }

    private static bool TryGetObject(
        JsonElement parent,
        string propertyName,
        out JsonElement value)
    {
        if (parent.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }
}
