using System.Globalization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalCodexUsageProvider : IUsageProvider
{
    private static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);

    private readonly IReadOnlyList<string>? _roots;

    public LocalCodexUsageProvider()
    {
    }

    public LocalCodexUsageProvider(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = roots.ToArray();
    }

    public string Id => "codex";

    public string DisplayName => "Codex";

    public bool ReportsCost => true;

    public Task<DailyUsage?> FetchDailyAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return FetchDailyAsync(
            now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);
    }

    public async Task<DailyUsage?> FetchDailyAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(
            now,
            timeZone,
            firstDayOfWeek,
            dailyOnly: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var today = snapshot.UsagePeriods.Today;
        if (today.TotalTokens <= 0)
        {
            return null;
        }

        return new DailyUsage(
            LocalDate(now, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            today.InputTokens,
            today.OutputTokens,
            today.CacheWriteTokens,
            today.CacheReadTokens,
            today.TotalTokens,
            TotalCost: 0);
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return FetchEnrichmentAsync(
            now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);
    }

    public async Task<ProviderEnrichment> FetchEnrichmentAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await LoadSnapshotAsync(
                now,
                timeZone,
                firstDayOfWeek,
                dailyOnly: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var periods = snapshot.UsagePeriods;
            var localToday = LocalDate(now, timeZone);
            var weekStart = localToday.AddDays(
                -DaysSinceWeekStart(localToday.DayOfWeek, firstDayOfWeek));

            return new ProviderEnrichment(
                ActiveBlock: CreateActiveBlock(snapshot, now),
                BlocksOK: true,
                WeekTotal: new PeriodUsage(
                    weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    periods.ThisWeek.TotalTokens,
                    TotalCost: 0),
                MonthTotal: new PeriodUsage(
                    localToday.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    periods.ThisMonth.TotalTokens,
                    TotalCost: 0),
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

    private Task<CodexLocalUsageSnapshot> LoadSnapshotAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        bool dailyOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Task.Run(
            () => (dailyOnly, _roots) switch
            {
                (true, null) => CodexLocalUsageService.LoadDefaultDailySnapshot(
                    now, timeZone, firstDayOfWeek),
                (true, not null) => CodexLocalUsageService.LoadDailySnapshotFromRoots(
                    _roots!, now, timeZone, firstDayOfWeek),
                (false, null) => CodexLocalUsageService.LoadDefaultSnapshot(
                    now, timeZone, firstDayOfWeek),
                _ => CodexLocalUsageService.LoadSnapshotFromRoots(
                    _roots!, now, timeZone, firstDayOfWeek),
            },
            cancellationToken);
    }

    private static BlockUsage? CreateActiveBlock(
        CodexLocalUsageSnapshot snapshot,
        DateTimeOffset now)
    {
        if (snapshot.FirstRecentTimestamp is not DateTimeOffset first)
        {
            return null;
        }

        var totalTokens = snapshot.UsagePeriods.RecentFiveHours.TotalTokens;
        var minutes = Math.Max(1, (now - first).TotalMinutes);
        return new BlockUsage(
            $"block-{first.ToUnixTimeSeconds()}",
            FormatInstant(first),
            FormatInstant(first + BlockWindow),
            IsActive: true,
            totalTokens,
            CostUSD: 0,
            TokensPerMinute: totalTokens / minutes);
    }

    private static DateOnly LocalDate(
        DateTimeOffset timestamp,
        TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);

    private static int DaysSinceWeekStart(
        DayOfWeek day,
        DayOfWeek firstDayOfWeek) =>
        ((int)day - (int)firstDayOfWeek + 7) % 7;

    private static string FormatInstant(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);
}
