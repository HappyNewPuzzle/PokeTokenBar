namespace PokeTokenBar.Windows.Core;

public enum UsageCacheLoadStatus
{
    Missing,
    Available,
    Corrupt,
    Unsupported,
}

public sealed record CachedProviderUsage(
    string ProviderId,
    DailyUsage? Today,
    BlockUsage? ActiveBlock,
    PeriodUsage? WeekTotal,
    PeriodUsage? MonthTotal,
    DateTimeOffset FetchedAt);

public sealed record UsageSnapshotCache(
    DateTimeOffset SavedAt,
    IReadOnlyList<CachedProviderUsage> Providers);

public sealed record UsageSnapshotCacheLoadResult(
    UsageCacheLoadStatus Status,
    UsageSnapshotCache? Cache = null);

public interface IUsageSnapshotPersistence
{
    UsageSnapshotCacheLoadResult Load();

    void Save(UsageSnapshotCache cache);
}
