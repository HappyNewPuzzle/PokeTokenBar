using System.Globalization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

internal sealed record LocalUsageEntry(
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

internal static class LocalUsageSupport
{
    private static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);

    public static DailyUsage? Daily(
        IEnumerable<LocalUsageEntry> entries,
        DateOnly day)
    {
        var total = Sum(entries.Where(entry => entry.LocalDay == day));
        return total.TotalTokens == 0
            ? null
            : new DailyUsage(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                total.Input,
                total.Output,
                total.CacheWrite,
                total.CacheRead,
                total.TotalTokens,
                total.Cost);
    }

    public static ProviderEnrichment Enrichment(
        IEnumerable<LocalUsageEntry> entries,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek)
    {
        var all = entries.ToArray();
        var today = LocalDate(now, timeZone);
        var weekStart = today.AddDays(-DaysSinceWeekStart(today.DayOfWeek, firstDayOfWeek));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var recent = all
            .Where(entry => entry.Timestamp >= now - BlockWindow)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var week = Sum(all.Where(entry => entry.LocalDay >= weekStart && entry.LocalDay <= today));
        var month = Sum(all.Where(entry => entry.LocalDay >= monthStart && entry.LocalDay <= today));

        return new ProviderEnrichment(
            ActiveBlock: ActiveBlock(recent, now),
            BlocksOK: true,
            WeekTotal: new PeriodUsage(
                weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                week.TotalTokens,
                week.Cost),
            MonthTotal: new PeriodUsage(
                today.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                month.TotalTokens,
                month.Cost),
            PeriodsOK: true);
    }

    public static DateTimeOffset EnrichmentScanStart(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek)
    {
        var today = LocalDate(now, timeZone);
        var weekStart = today.AddDays(-DaysSinceWeekStart(today.DayOfWeek, firstDayOfWeek));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return new[]
        {
            StartOfLocalDay(weekStart, timeZone),
            StartOfLocalDay(monthStart, timeZone),
            now - BlockWindow,
        }.Min();
    }

    public static DateOnly LocalDate(DateTimeOffset timestamp, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);

    public static DateTimeOffset StartOfLocalDay(DateOnly day, TimeZoneInfo timeZone)
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

    public static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
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

    public static IReadOnlyList<LocalUsageEntry> Deduplicate(IEnumerable<LocalUsageEntry> entries)
    {
        var byId = new Dictionary<string, LocalUsageEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byId.TryGetValue(entry.Id, out var current) || entry.TotalTokens > current.TotalTokens)
            {
                byId[entry.Id] = entry;
            }
        }

        return byId.Values.ToArray();
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
            "gpt-5.5" => (5d, 30d, 0d, 0.5d),
            "gemini-2.5-pro" => (1.25d, 10d, 0d, 0.3125d),
            "gemini-2.5-flash" => (0.30d, 2.5d, 0d, 0.075d),
            "gemini-2.0-flash" => (0.10d, 0.4d, 0d, 0.025d),
            _ when lower.StartsWith("grok", StringComparison.Ordinal) => (0d, 0d, 0d, 0d),
            _ when lower.Contains("fable", StringComparison.Ordinal) => (10d, 50d, 12.5d, 1d),
            _ when lower.Contains("opus", StringComparison.Ordinal) => (5d, 25d, 6.25d, 0.5d),
            _ when lower.Contains("sonnet", StringComparison.Ordinal) => (3d, 15d, 3.75d, 0.3d),
            _ when lower.Contains("haiku", StringComparison.Ordinal) => (1d, 5d, 1.25d, 0.1d),
            _ when lower.Contains("gpt", StringComparison.Ordinal) ||
                   lower.Contains("codex", StringComparison.Ordinal) ||
                   lower.Contains("o4", StringComparison.Ordinal) ||
                   lower.Contains("o3", StringComparison.Ordinal) => (5d, 30d, 0d, 0.5d),
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

    private static BlockUsage? ActiveBlock(IReadOnlyList<LocalUsageEntry> recent, DateTimeOffset now)
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

    private static Totals Sum(IEnumerable<LocalUsageEntry> entries)
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

    private static int DaysSinceWeekStart(DayOfWeek day, DayOfWeek firstDayOfWeek) =>
        ((int)day - (int)firstDayOfWeek + 7) % 7;

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
