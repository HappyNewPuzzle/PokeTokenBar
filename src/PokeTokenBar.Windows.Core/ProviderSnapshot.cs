namespace PokeTokenBar.Windows.Core;

public sealed record ProviderSnapshot(
    string ProviderId,
    string DisplayName,
    DailyUsage? Today,
    BlockUsage? ActiveBlock,
    PeriodUsage? WeekTotal,
    PeriodUsage? MonthTotal,
    DateTimeOffset FetchedAt,
    bool ReportsCost = true)
{
    public string Id => ProviderId;

    public long TodayTotalTokens => Today?.TotalTokens ?? 0;
}
