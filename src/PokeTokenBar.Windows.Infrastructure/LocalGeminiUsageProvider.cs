using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalGeminiUsageProvider : IUsageProvider
{
    private const long MaxParsedTokenValue = 1_000_000_000_000_000;
    private readonly IReadOnlyList<string> _roots;

    public LocalGeminiUsageProvider()
        : this(GetDefaultRoots())
    {
    }

    public LocalGeminiUsageProvider(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = LocalUsageSupport.NormalizeRoots(roots);
    }

    public string Id => "gemini";
    public string DisplayName => "Gemini";
    public bool ReportsCost => true;

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
        FetchDailyAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    public async Task<DailyUsage?> FetchDailyAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var today = LocalUsageSupport.LocalDate(now, timeZone);
        var entries = await LoadEntriesAsync(
            LocalUsageSupport.StartOfLocalDay(today, timeZone),
            timeZone,
            cancellationToken).ConfigureAwait(false);
        return LocalUsageSupport.Daily(entries, today);
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken = default) =>
        FetchEnrichmentAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    public async Task<ProviderEnrichment> FetchEnrichmentAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await LoadEntriesAsync(
                LocalUsageSupport.EnrichmentScanStart(now, timeZone, firstDayOfWeek),
                timeZone,
                cancellationToken).ConfigureAwait(false);
            return LocalUsageSupport.Enrichment(entries, now, timeZone, firstDayOfWeek);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ProviderEnrichment();
        }
    }

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return LocalUsageSupport.NormalizeRoots([Path.Combine(userProfile, ".gemini", "tmp")]);
    }

    public static double CalculateCost(
        string model,
        long input,
        long output,
        long cacheWrite,
        long cacheRead)
    {
        var lower = model.ToLowerInvariant();
        var rates = lower switch
        {
            "gemini-2.5-pro" => (1.25d, 10d, 0d, 0.3125d),
            "gemini-2.5-flash" => (0.30d, 2.5d, 0d, 0.075d),
            "gemini-2.0-flash" => (0.10d, 0.4d, 0d, 0.025d),
            _ when lower.StartsWith("gemini", StringComparison.Ordinal) &&
                       lower.Contains("pro", StringComparison.Ordinal) => (1.25d, 10d, 0d, 0.3125d),
            _ when lower.StartsWith("gemini", StringComparison.Ordinal) &&
                       lower.Contains("flash", StringComparison.Ordinal) => (0.30d, 2.5d, 0d, 0.075d),
            _ => (0d, 0d, 0d, 0d),
        };
        return ((input * rates.Item1) +
                (output * rates.Item2) +
                (cacheWrite * rates.Item3) +
                (cacheRead * rates.Item4)) / 1_000_000d;
    }

    private Task<IReadOnlyList<LocalUsageEntry>> LoadEntriesAsync(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken) =>
        Task.Run(() => LoadEntries(modifiedSince, timeZone, cancellationToken), cancellationToken);

    private IReadOnlyList<LocalUsageEntry> LoadEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            try
            {
                files = Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(path =>
                            Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            try
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < modifiedSince.UtcDateTime)
                        {
                            continue;
                        }

                        entries.AddRange(ParseFile(file, timeZone, cancellationToken));
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or JsonException)
                    {
                        // One malformed/unreadable session must not hide other sessions.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Recursive enumeration can fail after yielding some files.
            }
        }

        return LocalUsageSupport.Deduplicate(entries);
    }

    private static IReadOnlyList<LocalUsageEntry> ParseFile(
        string path,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(path);
        return Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonLines(bytes, Path.GetFileName(path), timeZone, cancellationToken)
            : ParseLegacyJson(bytes, Path.GetFileName(path), timeZone);
    }

    private static IReadOnlyList<LocalUsageEntry> ParseJsonLines(
        byte[] bytes,
        string fileName,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, LocalUsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        DateTimeOffset? lastTimestamp = null;
        using var reader = new StringReader(System.Text.Encoding.UTF8.GetString(bytes));
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.Contains("\"tokens\"", StringComparison.Ordinal) &&
                !line.Contains("\"timestamp\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (Timestamp(root, "timestamp") is DateTimeOffset timestamp)
                {
                    lastTimestamp = timestamp;
                }

                Absorb(root, fileName, Guid.NewGuid().ToString("N"), lastTimestamp, timeZone, byId, order);
            }
            catch (JsonException)
            {
                // Malformed lines are isolated within the session.
            }
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static IReadOnlyList<LocalUsageEntry> ParseLegacyJson(
        byte[] bytes,
        string fileName,
        TimeZoneInfo timeZone)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var byId = new Dictionary<string, LocalUsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        var sessionStart = Timestamp(root, "startTime");
        foreach (var message in messages.EnumerateArray())
        {
            Absorb(message, fileName, Guid.NewGuid().ToString("N"), sessionStart, timeZone, byId, order);
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static void Absorb(
        JsonElement value,
        string fileName,
        string missingId,
        DateTimeOffset? fallbackTimestamp,
        TimeZoneInfo timeZone,
        Dictionary<string, LocalUsageEntry> byId,
        List<string> order)
    {
        if (!value.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var timestamp = Timestamp(value, "timestamp") ?? fallbackTimestamp;
        if (timestamp is null)
        {
            return;
        }

        var id = String(value, "id") ?? missingId;
        var input = Token(tokens, "input");
        var cached = Token(tokens, "cached");
        var output = Token(tokens, "output") + Token(tokens, "thoughts");
        var nonCachedInput = Math.Max(0, input - cached) + Token(tokens, "tool");
        var model = String(value, "model") ?? "gemini";
        var entry = new LocalUsageEntry(
            $"gemini|{fileName}|{id}",
            timestamp.Value,
            LocalUsageSupport.LocalDate(timestamp.Value, timeZone),
            nonCachedInput,
            output,
            0,
            cached,
            CalculateCost(model, nonCachedInput, output, 0, cached));
        if (!byId.ContainsKey(id))
        {
            order.Add(id);
        }

        byId[id] = entry;
    }

    private static long Token(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) ||
            !double.IsFinite(number) ||
            number <= 0)
        {
            return 0;
        }

        return number >= MaxParsedTokenValue ? MaxParsedTokenValue : (long)number;
    }

    private static DateTimeOffset? Timestamp(JsonElement parent, string property) =>
        String(parent, property) is { } raw &&
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;

    private static string? String(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
