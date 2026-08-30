using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalClaudeUsageProvider : IUsageProvider
{
    private const long MaxParsedTokenValue = 1_000_000_000_000_000;
    private static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);
    private readonly IReadOnlyList<string> _roots;

    public LocalClaudeUsageProvider()
        : this(GetDefaultRoots())
    {
    }

    public LocalClaudeUsageProvider(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = NormalizeRoots(roots);
    }

    public string Id => "claude_code";

    public string DisplayName => "Claude Code";

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
        var localToday = LocalDate(now, timeZone);
        var entries = await LoadEntriesAsync(
            StartOfLocalDay(localToday, timeZone),
            timeZone,
            cancellationToken).ConfigureAwait(false);
        var total = Sum(entries.Where(entry => entry.LocalDay == localToday));
        return total.TotalTokens == 0
            ? null
            : new DailyUsage(
                localToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                total.Input,
                total.Output,
                total.CacheWrite,
                total.CacheRead,
                total.TotalTokens,
                total.Cost);
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
            var localToday = LocalDate(now, timeZone);
            var weekStart = localToday.AddDays(
                -DaysSinceWeekStart(localToday.DayOfWeek, firstDayOfWeek));
            var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
            var scanStart = new[]
            {
                StartOfLocalDay(weekStart, timeZone),
                StartOfLocalDay(monthStart, timeZone),
                now - BlockWindow,
            }.Min();
            var entries = await LoadEntriesAsync(scanStart, timeZone, cancellationToken)
                .ConfigureAwait(false);
            var recent = entries
                .Where(entry => entry.Timestamp >= now - BlockWindow)
                .OrderBy(entry => entry.Timestamp)
                .ToArray();
            var week = Sum(entries.Where(entry =>
                entry.LocalDay >= weekStart && entry.LocalDay <= localToday));
            var month = Sum(entries.Where(entry =>
                entry.LocalDay >= monthStart && entry.LocalDay <= localToday));

            return new ProviderEnrichment(
                ActiveBlock: CreateActiveBlock(recent, now),
                BlocksOK: true,
                WeekTotal: new PeriodUsage(
                    weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    week.TotalTokens,
                    week.Cost),
                MonthTotal: new PeriodUsage(
                    localToday.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    month.TotalTokens,
                    month.Cost),
                PeriodsOK: true);
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

    public static IReadOnlyList<string> GetDefaultRoots(
        string? userProfile = null,
        string? configDirectoryValue = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        configDirectoryValue ??= Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(configDirectoryValue))
        {
            foreach (var part in configDirectoryValue.Split(','))
            {
                var path = ExpandHome(part.Trim(), userProfile);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    roots.Add(Path.Combine(path, "projects"));
                }
            }
        }

        roots.Add(Path.Combine(userProfile, ".config", "claude", "projects"));
        roots.Add(Path.Combine(userProfile, ".claude", "projects"));
        return NormalizeRoots(roots);
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
            "claude-opus-4-8" or "claude-opus-4-7" => (5d, 25d, 6.25d, 0.5d),
            "claude-sonnet-4-6" => (3d, 15d, 3.75d, 0.3d),
            "claude-haiku-4-5-20251001" => (1d, 5d, 1.25d, 0.1d),
            "claude-fable-5" => (10d, 50d, 12.5d, 1d),
            _ when lower.Contains("fable", StringComparison.Ordinal) => (10d, 50d, 12.5d, 1d),
            _ when lower.Contains("opus", StringComparison.Ordinal) => (5d, 25d, 6.25d, 0.5d),
            _ when lower.Contains("sonnet", StringComparison.Ordinal) => (3d, 15d, 3.75d, 0.3d),
            _ when lower.Contains("haiku", StringComparison.Ordinal) => (1d, 5d, 1.25d, 0.1d),
            _ => (0d, 0d, 0d, 0d),
        };
        return ((input * rates.Item1) +
                (output * rates.Item2) +
                (cacheWrite * rates.Item3) +
                (cacheRead * rates.Item4)) / 1_000_000d;
    }

    private Task<IReadOnlyList<Entry>> LoadEntriesAsync(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => LoadEntries(modifiedSince, timeZone, cancellationToken),
            cancellationToken);

    private IReadOnlyList<Entry> LoadEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            try
            {
                files = Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
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

                        ParseFile(file, timeZone, byId, cancellationToken);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // One unreadable transcript must not hide other sessions.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Recursive enumeration can fail after yielding some files; keep parsed sessions.
            }
        }

        return byId.Values.ToArray();
    }

    private static void ParseFile(
        string path,
        TimeZoneInfo timeZone,
        Dictionary<string, Entry> byId,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.Contains("\"usage\"", StringComparison.Ordinal) ||
                !line.Contains("\"assistant\"", StringComparison.Ordinal) ||
                !TryParseLine(line, timeZone, out var entry))
            {
                continue;
            }

            if (!byId.TryGetValue(entry.Id, out var current) || entry.TotalTokens > current.TotalTokens)
            {
                byId[entry.Id] = entry;
            }
        }
    }

    private static bool TryParseLine(string line, TimeZoneInfo timeZone, out Entry entry)
    {
        entry = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryString(root, "type", out var type) || type != "assistant" ||
                !root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object ||
                !TryString(root, "timestamp", out var rawTimestamp) ||
                !DateTimeOffset.TryParse(
                    rawTimestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                return false;
            }

            var input = Token(usage, "input_tokens");
            var output = Token(usage, "output_tokens");
            var cacheWrite = Token(usage, "cache_creation_input_tokens");
            var cacheRead = Token(usage, "cache_read_input_tokens");
            var model = TryString(message, "model", out var parsedModel) ? parsedModel : "unknown";
            var messageId = TryString(message, "id", out var parsedMessageId) ? parsedMessageId : string.Empty;
            var requestId = TryString(root, "requestId", out var parsedRequestId) ? parsedRequestId : string.Empty;
            entry = new Entry(
                $"{messageId}|{requestId}",
                timestamp,
                LocalDate(timestamp, timeZone),
                input,
                output,
                cacheWrite,
                cacheRead,
                CalculateCost(model, input, output, cacheWrite, cacheRead));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long Token(JsonElement usage, string property)
    {
        if (!usage.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) || !double.IsFinite(number) || number <= 0)
        {
            return 0;
        }

        return number >= MaxParsedTokenValue ? MaxParsedTokenValue : (long)number;
    }

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.String &&
            (value = child.GetString() ?? string.Empty).Length > 0;
    }

    private static Totals Sum(IEnumerable<Entry> entries)
    {
        var total = new Totals();
        foreach (var entry in entries)
        {
            total.Input += entry.Input;
            total.Output += entry.Output;
            total.CacheWrite += entry.CacheWrite;
            total.CacheRead += entry.CacheRead;
            total.Cost += entry.Cost;
        }

        return total;
    }

    private static BlockUsage? CreateActiveBlock(IReadOnlyList<Entry> recent, DateTimeOffset now)
    {
        if (recent.Count == 0)
        {
            return null;
        }

        var first = recent[0].Timestamp;
        var total = Sum(recent);
        var minutes = Math.Max(1, (now - first).TotalMinutes);
        return new BlockUsage(
            $"block-{first.ToUnixTimeSeconds()}",
            first.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            (first + BlockWindow).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            IsActive: true,
            total.TotalTokens,
            total.Cost,
            total.TotalTokens / minutes);
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var result = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                var full = Path.GetFullPath(root);
                if (!result.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(full);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Invalid injected/configured roots are unavailable, not fatal.
            }
        }

        return result.AsReadOnly();
    }

    private static string ExpandHome(string path, string userProfile) =>
        path == "~"
            ? userProfile
            : path.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
              path.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(userProfile, path[2..])
                : path;

    private static DateOnly LocalDate(DateTimeOffset timestamp, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);

    private static DateTimeOffset StartOfLocalDay(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static int DaysSinceWeekStart(DayOfWeek day, DayOfWeek firstDayOfWeek) =>
        ((int)day - (int)firstDayOfWeek + 7) % 7;

    private sealed record Entry(
        string Id,
        DateTimeOffset Timestamp,
        DateOnly LocalDay,
        long Input,
        long Output,
        long CacheWrite,
        long CacheRead,
        double Cost)
    {
        public long TotalTokens => Input + Output + CacheWrite + CacheRead;
    }

    private sealed class Totals
    {
        public long Input { get; set; }
        public long Output { get; set; }
        public long CacheWrite { get; set; }
        public long CacheRead { get; set; }
        public double Cost { get; set; }
        public long TotalTokens => Input + Output + CacheWrite + CacheRead;
    }
}
