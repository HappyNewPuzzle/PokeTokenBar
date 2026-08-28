using System.Globalization;

namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexUsagePeriodAggregator
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(5);

    public static CodexUsagePeriods Calculate(
        IEnumerable<CodexCanonicalEvent> events) =>
        Calculate(
            events,
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);

    public static CodexUsagePeriods Calculate(
        IEnumerable<CodexCanonicalEvent> events,
        DateTimeOffset now,
        TimeZoneInfo timeZone) =>
        Calculate(
            events,
            now,
            timeZone,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);

    public static CodexUsagePeriods Calculate(
        IEnumerable<CodexCanonicalEvent> events,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (firstDayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDayOfWeek));
        }

        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var weekStart = localToday.AddDays(
            -DaysSinceWeekStart(localToday.DayOfWeek, firstDayOfWeek));
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
        var recentStart = now - RecentWindow;

        var todayEvents = new List<CodexCanonicalEvent>();
        var weekEvents = new List<CodexCanonicalEvent>();
        var monthEvents = new List<CodexCanonicalEvent>();
        var recentEvents = new List<CodexCanonicalEvent>();

        foreach (var canonicalEvent in events)
        {
            ArgumentNullException.ThrowIfNull(canonicalEvent);

            var timestamp = canonicalEvent.TokenEvent.TokenEvent.TokenCount.Timestamp;
            var localDay = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);

            if (localDay == localToday)
            {
                todayEvents.Add(canonicalEvent);
            }

            if (localDay >= weekStart && localDay <= localToday)
            {
                weekEvents.Add(canonicalEvent);
            }

            if (localDay >= monthStart && localDay <= localToday)
            {
                monthEvents.Add(canonicalEvent);
            }

            if (timestamp >= recentStart)
            {
                recentEvents.Add(canonicalEvent);
            }
        }

        return new CodexUsagePeriods(
            CodexUsageAggregator.Sum(todayEvents),
            CodexUsageAggregator.Sum(weekEvents),
            CodexUsageAggregator.Sum(monthEvents),
            CodexUsageAggregator.Sum(recentEvents));
    }

    private static int DaysSinceWeekStart(
        DayOfWeek day,
        DayOfWeek firstDayOfWeek) =>
        ((int)day - (int)firstDayOfWeek + 7) % 7;
}
