using System.Collections.ObjectModel;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexRateLimitJsonParser
{
    public static CodexRateLimitStatus Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("rateLimits", out var rateLimits) ||
            rateLimits.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The response does not contain a rateLimits object.");
        }

        IReadOnlyDictionary<string, CodexRateLimitSnapshot>? byLimitId = null;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
            byId.ValueKind != JsonValueKind.Null)
        {
            if (byId.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("rateLimitsByLimitId must be an object or null.");
            }

            var parsed = new Dictionary<string, CodexRateLimitSnapshot>(StringComparer.Ordinal);
            foreach (var property in byId.EnumerateObject())
            {
                parsed[property.Name] = ParseSnapshot(property.Value);
            }

            byLimitId = new ReadOnlyDictionary<string, CodexRateLimitSnapshot>(parsed);
        }

        return new CodexRateLimitStatus(ParseSnapshot(rateLimits), byLimitId);
    }

    private static CodexRateLimitSnapshot ParseSnapshot(JsonElement element)
    {
        RequireObject(element, "rate limit snapshot");
        return new CodexRateLimitSnapshot(
            OptionalString(element, "limitId"),
            OptionalString(element, "limitName"),
            OptionalObject(element, "primary", ParseWindow),
            OptionalObject(element, "secondary", ParseWindow),
            OptionalObject(element, "credits", ParseCredits),
            OptionalObject(element, "individualLimit", ParseSpendLimit),
            OptionalString(element, "planType"),
            OptionalString(element, "rateLimitReachedType"));
    }

    private static CodexRateLimitWindow ParseWindow(JsonElement element)
    {
        var usedPercent = RequiredInt(element, "usedPercent");
        var duration = OptionalInt(element, "windowDurationMins");
        var resetsAt = OptionalUnixTimestamp(element, "resetsAt");
        return new CodexRateLimitWindow(usedPercent, duration, resetsAt);
    }

    private static CodexCreditsSnapshot ParseCredits(JsonElement element) =>
        new(
            OptionalString(element, "balance"),
            RequiredBoolean(element, "hasCredits"),
            RequiredBoolean(element, "unlimited"));

    private static CodexSpendControlLimit ParseSpendLimit(JsonElement element)
    {
        var resetsAt = RequiredUnixTimestamp(element, "resetsAt");
        return new CodexSpendControlLimit(
            RequiredString(element, "limit"),
            RequiredInt(element, "remainingPercent"),
            resetsAt,
            RequiredString(element, "used"));
    }

    private static T? OptionalObject<T>(
        JsonElement element,
        string propertyName,
        Func<JsonElement, T> parser)
        where T : class
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(value, propertyName);
        return parser(value);
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) ??
        throw new JsonException($"{propertyName} must be a string.");

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{propertyName} must be a string or null.");
        }

        return value.GetString();
    }

    private static int RequiredInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
        {
            throw new JsonException($"{propertyName} must be an integer.");
        }

        return parsed;
    }

    private static int? OptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new JsonException($"{propertyName} must be an integer or null.");
    }

    private static bool RequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException($"{propertyName} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static DateTimeOffset RequiredUnixTimestamp(
        JsonElement element,
        string propertyName) =>
        OptionalUnixTimestamp(element, propertyName) ??
        throw new JsonException($"{propertyName} must be a Unix timestamp.");

    private static DateTimeOffset? OptionalUnixTimestamp(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds))
        {
            throw new JsonException($"{propertyName} must be an integer Unix timestamp or null.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException($"{propertyName} is outside the supported timestamp range.", exception);
        }
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{name} must be an object.");
        }
    }
}
