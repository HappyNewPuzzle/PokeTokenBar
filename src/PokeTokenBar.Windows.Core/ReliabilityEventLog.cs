using System.Collections.Concurrent;

namespace PokeTokenBar.Windows.Core;

public enum ReliabilityEventKind
{
    Recovery,
    Error,
}

public sealed record ReliabilityEvent(
    DateTimeOffset Timestamp,
    ReliabilityEventKind Kind,
    string Component,
    string Summary);

public static class ReliabilityEventLog
{
    private const int Capacity = 20;
    private static readonly ConcurrentQueue<ReliabilityEvent> Events = new();
    private static readonly string[] SensitiveTerms =
        ["authorization", "bearer", "api-key", "api_key", "api key", "apikey", "token", "cookie", "credential", "secret", "oauth", "password"];

    public static IReadOnlyList<ReliabilityEvent> Snapshot() => Events.ToArray();

    public static void RecordRecovery(string component, string summary) =>
        Record(ReliabilityEventKind.Recovery, component, summary);

    public static void RecordError(string component, Exception exception) =>
        Record(ReliabilityEventKind.Error, component, exception.GetType().Name);

    private static void Record(ReliabilityEventKind kind, string component, string summary)
    {
        try
        {
            Events.Enqueue(new(DateTimeOffset.UtcNow, kind, Safe(component), Safe(summary)));
            while (Events.Count > Capacity) Events.TryDequeue(out _);
        }
        catch
        {
            // Reliability reporting must never become a second failure.
        }
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            SensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return "[redacted]";
        }

        var sanitized = new string(value
            .Take(64)
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
