using System.Globalization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexLocalUsageService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(5);

    public static CodexUsagePeriods LoadDefault()
    {
        var now = DateTimeOffset.Now;
        return LoadDefault(
            now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
    }

    public static CodexUsagePeriods LoadDefault(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(
            CodexSessionLocator.GetDefaultRoots(),
            now,
            timeZone,
            firstDayOfWeek).UsagePeriods;

    public static CodexUsagePeriods LoadFromRoots(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(roots, now, timeZone, firstDayOfWeek).UsagePeriods;

    internal static CodexLocalUsageSnapshot LoadDefaultSnapshot(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(
            CodexSessionLocator.GetDefaultRoots(),
            now,
            timeZone,
            firstDayOfWeek);

    internal static CodexLocalUsageSnapshot LoadDefaultDailySnapshot(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(
            CodexSessionLocator.GetDefaultRoots(),
            now,
            timeZone,
            firstDayOfWeek,
            modifiedSince: StartOfLocalDay(
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime),
                timeZone));

    internal static CodexLocalUsageSnapshot LoadSnapshotFromRoots(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(roots, now, timeZone, firstDayOfWeek);

    internal static CodexLocalUsageSnapshot LoadDailySnapshotFromRoots(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        LoadSnapshot(
            roots,
            now,
            timeZone,
            firstDayOfWeek,
            modifiedSince: StartOfLocalDay(
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime),
                timeZone));

    private static CodexLocalUsageSnapshot LoadSnapshot(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        DateTimeOffset? modifiedSince = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (firstDayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDayOfWeek));
        }

        var scanStart = modifiedSince
            ?? CalculateModifiedSince(now, timeZone, firstDayOfWeek);
        var pipelineResult = CodexLocalRolloutPipeline.LoadFromRoots(
            roots,
            scanStart);
        var canonicalEvents = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            pipelineResult);
        var usagePeriods = CodexUsagePeriodAggregator.Calculate(
            canonicalEvents,
            now,
            timeZone,
            firstDayOfWeek);
        var recentStart = now - RecentWindow;
        DateTimeOffset? firstRecentTimestamp = null;
        foreach (var canonicalEvent in canonicalEvents)
        {
            var timestamp = canonicalEvent.TokenEvent.TokenEvent.TokenCount.Timestamp;
            if (timestamp >= recentStart
                && (firstRecentTimestamp is null || timestamp < firstRecentTimestamp.Value))
            {
                firstRecentTimestamp = timestamp;
            }
        }

        return new CodexLocalUsageSnapshot(usagePeriods, firstRecentTimestamp);
    }

    private static DateTimeOffset CalculateModifiedSince(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek)
    {
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var weekStart = localToday.AddDays(
            -DaysSinceWeekStart(localToday.DayOfWeek, firstDayOfWeek));
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);

        var monthStartInstant = StartOfLocalDay(monthStart, timeZone);
        var weekStartInstant = StartOfLocalDay(weekStart, timeZone);
        var recentStart = now - RecentWindow;

        return new[] { monthStartInstant, weekStartInstant, recentStart }.Min();
    }

    private static DateTimeOffset StartOfLocalDay(
        DateOnly day,
        TimeZoneInfo timeZone)
    {
        var localTime = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        while (timeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(localTime)
            ? timeZone.GetAmbiguousTimeOffsets(localTime).Max()
            : timeZone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset);
    }

    private static int DaysSinceWeekStart(
        DayOfWeek day,
        DayOfWeek firstDayOfWeek) =>
        ((int)day - (int)firstDayOfWeek + 7) % 7;
}
