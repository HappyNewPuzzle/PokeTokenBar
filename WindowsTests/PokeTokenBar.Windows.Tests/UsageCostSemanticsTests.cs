using PokeTokenBar.Windows.App.Formatting;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;
using System.Text.Json;

namespace PokeTokenBar.Windows.Tests;

public sealed class UsageCostSemanticsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExplicitZeroUnknownAndEstimateRemainDistinctThroughAggregation()
    {
        var day = DateOnly.FromDateTime(Now.Date);
        var entries = new[]
        {
            Entry("zero", 10, 0, CostCoverage.Source),
            Entry("known", 20, 0.01, CostCoverage.Estimate),
            Entry("unknown", 30, 0, CostCoverage.Unavailable),
        };

        var usage = Assert.IsType<DailyUsage>(LocalUsageSupport.Daily(entries, day));

        Assert.Equal(60, usage.TotalTokens);
        Assert.Equal(0.01, usage.TotalCost, precision: 6);
        Assert.Equal(new CostCoverage(Reported: true, Estimated: true, Unknown: true), usage.CostCoverage);
    }

    [Fact]
    public void UnknownModelIsUnavailableWhileKnownModelCanBeEstimated()
    {
        Assert.Null(LocalUsageSupport.EstimatedCost("future-model", 1_000, 100, 0, 0));
        Assert.Equal(
            0.0065,
            LocalUsageSupport.EstimatedCost("gpt-5.5", 1_000, 50, 0, 0)!.Value,
            precision: 6);
    }

    [Fact]
    public void PricingNormalizesDocumentedAliasesAndAppliesLongContextMultipliers()
    {
        Assert.Equal(
            15,
            LocalUsageSupport.EstimatedCost("anthropic/claude-opus-4", 1_000_000, 0, 0, 0));
        Assert.Equal(
            3.75,
            LocalUsageSupport.EstimatedCost("gpt-5.4", 300_000, 100_000, 0, 0)!.Value,
            precision: 6);
        Assert.Null(LocalUsageSupport.EstimatedCost("gpt-5.6-codex", 1_000, 0, 0, 0));
    }

    [Fact]
    public void CostRenderingUsesPlainDollarAmountsWithoutEstimateOrPartialMarkers()
    {
        Assert.Equal("$12.50", UsageValueFormatter.Cost(
            new UsageCost(12.5, new CostCoverage(Estimated: true, Unknown: true))));
        Assert.Equal("$0.00", UsageValueFormatter.Cost(new UsageCost(0, CostCoverage.Source)));
        Assert.Equal("$—", UsageValueFormatter.Cost(new UsageCost(0, CostCoverage.Unavailable)));
    }

    [Fact]
    public void OpenCodeExplicitZeroDoesNotFallBackToModelPrice()
    {
        var entry = Assert.IsType<LocalUsageEntry>(LocalOpenCodeUsageProvider.ParseMessage(
            """
            {"id":"m","providerID":"openai","modelID":"gpt-5.5",
             "time":{"created":1789718400000},"tokens":{"input":1000,"output":100},"cost":0}
            """,
            "fallback",
            DateTimeOffset.MinValue,
            TimeZoneInfo.Utc));

        Assert.Equal(0, entry.Cost);
        Assert.Equal(CostCoverage.Source, entry.CostCoverage);
    }

    [Theory]
    [InlineData(0, false, false, true)]
    [InlineData(1.25, false, true, false)]
    public void LegacyCacheCoverageInferencePreservesVersionOneData(
        double cost,
        bool reported,
        bool estimated,
        bool unknown)
    {
        Assert.Equal(
            new CostCoverage(reported, estimated, unknown),
            CostCoverage.FromLegacy(500, cost));
    }

    [Fact]
    public void VersionOneUsageCacheWithoutCoverageStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"usage-cost-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                savedAt = Now,
                providers = new[]
                {
                    new
                    {
                        providerId = "legacy",
                        today = new
                        {
                            date = "2026-09-18", inputTokens = 500, outputTokens = 0,
                            cacheCreationTokens = 0, cacheReadTokens = 0,
                            totalTokens = 500, totalCost = 0,
                        },
                        activeBlock = (object?)null,
                        weekTotal = (object?)null,
                        monthTotal = (object?)null,
                        fetchedAt = Now,
                    },
                },
            }));

            var store = new UsageStore(
                [new EmptyProvider()],
                new FixedTimeProvider(Now),
                snapshotPersistence: new JsonUsageSnapshotPersistence(path));

            Assert.Equal(CostCoverage.Unavailable, store.Snapshot("legacy")?.Today?.CostCoverage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LocalUsageEntry Entry(
        string id,
        long tokens,
        double cost,
        CostCoverage coverage) =>
        new(id, Now, DateOnly.FromDateTime(Now.Date), tokens, 0, 0, 0, cost, coverage);

    private sealed class EmptyProvider : IUsageProvider
    {
        public string Id => "legacy";
        public string DisplayName => "Legacy";
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(null);
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
