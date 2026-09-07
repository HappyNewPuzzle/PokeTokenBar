using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class JsonUsageSnapshotPersistence : IUsageSnapshotPersistence
{
    internal const int FormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public JsonUsageSnapshotPersistence(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath() =>
        Path.Combine(PokeTokenBarDataPaths.Root, "usage-cache.json");

    public UsageSnapshotCacheLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new(UsageCacheLoadStatus.Missing);
        }

        try
        {
            using var stream = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var document = JsonSerializer.Deserialize<CacheDocument>(stream, SerializerOptions);
            if (document is null || document.FormatVersion <= 0)
            {
                AtomicFile.Quarantine(FilePath, "usage-cache");
                return new(UsageCacheLoadStatus.Corrupt);
            }

            if (document.FormatVersion != FormatVersion)
            {
                ReliabilityEventLog.RecordRecovery("usage-cache", "unsupported-cache-ignored");
                return new(UsageCacheLoadStatus.Unsupported);
            }

            if (!IsValid(document))
            {
                AtomicFile.Quarantine(FilePath, "usage-cache");
                return new(UsageCacheLoadStatus.Corrupt);
            }

            return new(
                UsageCacheLoadStatus.Available,
                new UsageSnapshotCache(document.SavedAt, document.Providers));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            if (exception is JsonException)
                AtomicFile.Quarantine(FilePath, "usage-cache");
            else
                ReliabilityEventLog.RecordError("usage-cache", exception);
            return new(UsageCacheLoadStatus.Corrupt);
        }
    }

    public void Save(UsageSnapshotCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var document = new CacheDocument(FormatVersion, cache.SavedAt, cache.Providers);
        if (!IsValid(document)) throw new ArgumentException("Usage cache contains invalid values.", nameof(cache));
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(
            stream,
            document,
            SerializerOptions));
    }

    private static bool IsValid(CacheDocument document)
    {
        if (document.SavedAt == default || document.Providers is null ||
            document.Providers.Any(provider => provider is null) ||
            document.Providers.Select(provider => provider.ProviderId)
                .Distinct(StringComparer.Ordinal).Count() != document.Providers.Count)
        {
            return false;
        }

        return document.Providers.All(provider =>
            !string.IsNullOrWhiteSpace(provider.ProviderId) &&
            provider.FetchedAt != default &&
            Valid(provider.Today) &&
            Valid(provider.ActiveBlock) &&
            Valid(provider.WeekTotal) &&
            Valid(provider.MonthTotal));
    }

    private static bool Valid(DailyUsage? usage) => usage is null ||
        (DateOnly.TryParseExact(usage.Date, "yyyy-MM-dd", out _) &&
         usage.InputTokens >= 0 && usage.OutputTokens >= 0 &&
         usage.CacheCreationTokens >= 0 && usage.CacheReadTokens >= 0 &&
         usage.TotalTokens >= 0 && ValidCost(usage.TotalCost));

    private static bool Valid(PeriodUsage? usage) => usage is null ||
        (!string.IsNullOrWhiteSpace(usage.Period) &&
         usage.TotalTokens >= 0 && ValidCost(usage.TotalCost));

    private static bool Valid(BlockUsage? usage) => usage is null ||
        (usage.TotalTokens >= 0 && ValidCost(usage.CostUSD) &&
         (usage.TokensPerMinute is null ||
          double.IsFinite(usage.TokensPerMinute.Value) && usage.TokensPerMinute.Value >= 0));

    private static bool ValidCost(double value) => double.IsFinite(value) && value >= 0;

    private sealed record CacheDocument(
        int FormatVersion,
        DateTimeOffset SavedAt,
        IReadOnlyList<CachedProviderUsage> Providers);
}
