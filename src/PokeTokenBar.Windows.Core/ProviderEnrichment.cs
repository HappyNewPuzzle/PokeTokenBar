namespace PokeTokenBar.Windows.Core;

public sealed record ProviderEnrichment(
    BlockUsage? ActiveBlock = null,
    bool BlocksOK = false,
    PeriodUsage? WeekTotal = null,
    PeriodUsage? MonthTotal = null,
    bool PeriodsOK = false);
