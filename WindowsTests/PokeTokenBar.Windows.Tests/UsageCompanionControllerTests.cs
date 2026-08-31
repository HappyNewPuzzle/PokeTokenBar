using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class UsageCompanionControllerTests
{
    [Fact]
    public async Task StartupRefreshSeedsBaselineWithoutRetroactiveProgress()
    {
        var fixture = Create(100);
        using var controller = fixture.Controller;
        var startup = new InitialRefreshController(fixture.Usage);

        await startup.StartAsync();
        await controller.LastUpdate;

        Assert.Equal(100, fixture.Store.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(0, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task ManualRefreshAppliesOnlyTheNewProviderDelta()
    {
        var fixture = Create(100);
        using var controller = fixture.Controller;
        await fixture.Usage.RefreshAsync();
        await controller.LastUpdate;

        fixture.Provider.Tokens = 1_000_100;
        await fixture.Usage.RefreshAsync();
        await controller.LastUpdate;
        await fixture.Usage.RefreshAsync();
        await controller.LastUpdate;

        Assert.Equal(1_000_000, fixture.Store.State.EggUsage);
        Assert.Equal(0.2, fixture.Companion.Progress, precision: 3);
        Assert.Contains("4M", fixture.Companion.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRefreshDoesNotAdvanceCompanionFromStaleUsage()
    {
        var fixture = Create(100);
        using var controller = fixture.Controller;
        await fixture.Usage.RefreshAsync();
        await controller.LastUpdate;

        fixture.Provider.Tokens = 1_000_100;
        fixture.Provider.Error = new IOException("scan failed");
        await fixture.Usage.RefreshAsync();
        await controller.LastUpdate;

        Assert.Equal(0, fixture.Store.State.EggUsage);
        Assert.Equal(100, fixture.Store.State.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public async Task DisposedControllerStopsFollowingRefreshIntegration()
    {
        var fixture = Create(100);
        await fixture.Usage.RefreshAsync();
        await fixture.Controller.LastUpdate;
        fixture.Controller.Dispose();

        fixture.Provider.Tokens = 1_000_100;
        await fixture.Usage.RefreshAsync();

        Assert.Equal(0, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task OfficialLimitEdgeGrantsCandyThroughRefreshIntegration()
    {
        var provider = new MutableUsageProvider { Tokens = 100 };
        var usage = new UsageViewModel(new UsageStore(
            [provider],
            claudeRateLimitsProvider: new FixedClaudeLimits()));
        var persistence = new MemoryPersistence(new CompanionState { CandyFeatureSeeded = true });
        var store = new CompanionStore(new UnusedPokeApi(), persistence);
        using var controller = new UsageCompanionController(usage, store, _ => Task.CompletedTask);

        await usage.RefreshAsync();
        await controller.LastUpdate;

        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(1, persistence.State?.Inventory["rareCandy"]);
    }

    private static Fixture Create(long tokens)
    {
        var provider = new MutableUsageProvider { Tokens = tokens };
        var usage = new UsageViewModel(new UsageStore([provider]));
        var store = new CompanionStore(new UnusedPokeApi(), new MemoryPersistence());
        var companion = new CompanionViewModel(
            store,
            (_, _, _) => Task.FromResult<PokemonSpriteAsset?>(null),
            new NullSpriteDecoder());
        return new Fixture(
            provider,
            usage,
            store,
            companion,
            new UsageCompanionController(usage, store, companion.RefreshAsync));
    }

    private sealed record Fixture(
        MutableUsageProvider Provider,
        UsageViewModel Usage,
        CompanionStore Store,
        CompanionViewModel Companion,
        UsageCompanionController Controller);

    private sealed class MutableUsageProvider : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => true;
        public long Tokens { get; set; }
        public Exception? Error { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Error is null
                ? Task.FromResult<DailyUsage?>(new(
                    DateTimeOffset.Now.ToString("yyyy-MM-dd"),
                    Tokens,
                    0,
                    0,
                    0,
                    Tokens,
                    0))
                : Task.FromException<DailyUsage?>(Error);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class MemoryPersistence(CompanionState? state = null) : ICompanionPersistence
    {
        public CompanionState? State { get; private set; } = state;
        public CompanionState? Load() => State;
        public void Save(CompanionState state) => State = state;
        public void Delete() => State = null;
    }

    private sealed class FixedClaudeLimits : IClaudeRateLimitsProvider
    {
        public Task<ClaudeRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaudeRateLimitStatus?>(new(
                new ClaudeRateLimitWindow(100, null),
                null, null, null, null, null));
    }

    private sealed class UnusedPokeApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullSpriteDecoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation? Decode(PokemonSpriteAsset asset) => null;
    }
}
