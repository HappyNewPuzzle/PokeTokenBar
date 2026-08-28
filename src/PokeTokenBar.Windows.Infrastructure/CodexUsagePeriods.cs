using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexUsagePeriods(
    CodexUsageEntry Today,
    CodexUsageEntry ThisWeek,
    CodexUsageEntry ThisMonth,
    CodexUsageEntry RecentFiveHours);
