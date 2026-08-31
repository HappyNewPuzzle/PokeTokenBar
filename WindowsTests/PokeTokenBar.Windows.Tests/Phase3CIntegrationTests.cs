using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase3CIntegrationTests
{
    [Fact]
    public async Task CursorDashboardFlowsThroughStoreSelectorAndUsageViewModel()
    {
        var handler = new CursorHandler { Tokens = 125 };
        var usage = new UsageViewModel(new UsageStore([Cursor(handler)]));

        await usage.RefreshAsync();
        usage.SelectedProviderId = "cursor";

        Assert.Equal("Cursor", usage.ProviderName);
        Assert.Equal(125, usage.TodayTokens);
        Assert.Equal(125, usage.RecentFiveHourTokens);
        Assert.Equal(125, usage.WeekTokens);
        Assert.Equal(125, usage.MonthTokens);
        Assert.False(usage.ShowsCost);
    }

    [Fact]
    public async Task CursorAndExistingProviderDeltasUseIndependentCompanionLedger()
    {
        var handler = new CursorHandler { Tokens = 100 };
        var codex = new MutableProvider("codex", 200);
        var usage = new UsageViewModel(new UsageStore([codex, Cursor(handler)]));
        var companion = new CompanionStore(new UnusedApi(), new MemoryPersistence());
        using var controller = new UsageCompanionController(usage, companion, _ => Task.CompletedTask);

        await usage.RefreshAsync();
        await controller.LastUpdate;
        handler.Tokens = 150;
        codex.Tokens = 225;
        await usage.RefreshAsync();
        await controller.LastUpdate;

        Assert.Equal(75, companion.State.EggUsage);
        Assert.Equal(225, companion.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(150, companion.State.ClaimedTodayTokensByProvider["cursor"]);
    }

    [Fact]
    public async Task CursorFailurePreservesStaleCursorWhileOtherProviderKeepsRefreshing()
    {
        var handler = new CursorHandler { Tokens = 80 };
        var codex = new MutableProvider("codex", 10);
        var usage = new UsageViewModel(new UsageStore([codex, Cursor(handler)]));
        await usage.RefreshAsync();

        handler.Fail = true;
        codex.Tokens = 20;
        await usage.RefreshAsync();

        Assert.Equal(20, usage.Providers.Single(item => item.ProviderId == "codex").Today!.TotalTokens);
        Assert.Equal(80, usage.Providers.Single(item => item.ProviderId == "cursor").Today!.TotalTokens);
    }

    private static LocalCursorUsageProvider Cursor(CursorHandler handler) =>
        new(
            new HttpClient(handler),
            [Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}")],
            () => "test-token",
            cacheLifetime: TimeSpan.Zero);

    private sealed class CursorHandler : HttpMessageHandler
    {
        public long Tokens { get; set; }
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var json = JsonSerializer.Serialize(new
            {
                usageEventsDisplay = new[]
                {
                    new
                    {
                        id = "cursor-request",
                        timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(),
                        model = "composer",
                        tokenUsage = new { inputTokens = Tokens },
                    },
                },
                totalUsageEventsCount = 1,
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class MutableProvider(string id, long tokens) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public bool ReportsCost => true;
        public long Tokens { get; set; } = tokens;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(new(
                DateTimeOffset.Now.ToString("yyyy-MM-dd"), Tokens, 0, 0, 0, Tokens, 0));
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class MemoryPersistence : ICompanionPersistence
    {
        public CompanionState? Load() => null;
        public void Save(CompanionState state) { }
        public void Delete() { }
    }

    private sealed class UnusedApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
