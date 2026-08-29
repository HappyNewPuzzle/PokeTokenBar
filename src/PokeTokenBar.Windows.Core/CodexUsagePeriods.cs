namespace PokeTokenBar.Windows.Core;

public sealed record CodexUsagePeriods(
    CodexUsageEntry Today,
    CodexUsageEntry ThisWeek,
    CodexUsageEntry ThisMonth,
    CodexUsageEntry RecentFiveHours);
