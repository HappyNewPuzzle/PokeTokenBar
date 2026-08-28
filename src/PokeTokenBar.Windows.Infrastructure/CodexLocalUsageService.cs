using System.Globalization;

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
        Load(
            CodexSessionLocator.GetDefaultRoots(),
            now,
            timeZone,
            firstDayOfWeek);

    public static CodexUsagePeriods LoadFromRoots(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek) =>
        Load(roots, now, timeZone, firstDayOfWeek);

    private static CodexUsagePeriods Load(
        IEnumerable<string> roots,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (firstDayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDayOfWeek));
        }

        var modifiedSince = CalculateModifiedSince(now, timeZone, firstDayOfWeek);
        var pipelineResult = CodexLocalRolloutPipeline.LoadFromRoots(
            roots,
            modifiedSince);
        var canonicalEvents = CodexCanonicalPrimaryFinalizer.CreateCanonicalEvents(
            pipelineResult);
        return CodexUsagePeriodAggregator.Calculate(
            canonicalEvents,
            now,
            timeZone,
            firstDayOfWeek);
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
