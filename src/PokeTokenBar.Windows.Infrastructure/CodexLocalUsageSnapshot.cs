using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

internal sealed record CodexLocalUsageSnapshot(
    CodexUsagePeriods UsagePeriods,
    DateTimeOffset? FirstRecentTimestamp);
