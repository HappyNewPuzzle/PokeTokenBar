using System.Text.Json;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase3BIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Phase3B-Integration-{Guid.NewGuid():N}");

    public Phase3BIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task GeminiFixtureAndFourProvidersFlowThroughViewModelAndIndependentCompanionLedger()
    {
        var path = Path.Combine(_directory, "hash", "chats", "session.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var timestamp = DateTimeOffset.Now.ToUniversalTime();
        File.WriteAllText(path, GeminiLine("g1", timestamp, 10));
        var codex = new MutableProvider("codex", "Codex", 100);
        var claude = new MutableProvider("claude_code", "Claude Code", 200);
        var antigravity = new MutableProvider("antigravity", "Antigravity", 300, reportsCost: false);
        var usage = new UsageViewModel(new UsageStore(
            [codex, claude, new LocalGeminiUsageProvider([_directory]), antigravity]));
        var companion = new CompanionStore(new UnusedApi(), new MemoryPersistence());
        using var controller = new UsageCompanionController(usage, companion, _ => Task.CompletedTask);

        await usage.RefreshAsync();
        await controller.LastUpdate;
        Assert.Equal(0, companion.State.EggUsage);

        codex.Tokens = 150;
        claude.Tokens = 240;
        antigravity.Tokens = 330;
        File.AppendAllText(path, Environment.NewLine + GeminiLine("g2", timestamp.AddSeconds(1), 20));
        await usage.RefreshAsync();
        await controller.LastUpdate;
        usage.SelectedProviderId = "gemini";

        Assert.Equal(140, companion.State.EggUsage);
        Assert.Equal(150, companion.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(240, companion.State.ClaimedTodayTokensByProvider["claude_code"]);
        Assert.Equal(30, companion.State.ClaimedTodayTokensByProvider["gemini"]);
        Assert.Equal(330, companion.State.ClaimedTodayTokensByProvider["antigravity"]);
        Assert.Equal("Gemini", usage.ProviderName);
        Assert.Equal(30, usage.TodayTokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string GeminiLine(string id, DateTimeOffset timestamp, long input) =>
        JsonSerializer.Serialize(new
        {
            type = "gemini",
            id,
            timestamp = timestamp.ToString("O"),
            model = "gemini-2.5-pro",
            tokens = new { input, output = 0, cached = 0, thoughts = 0, tool = 0 },
        });

    private sealed class MutableProvider(
        string id,
        string displayName,
        long tokens,
        bool reportsCost = true) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public bool ReportsCost => reportsCost;
        public long Tokens { get; set; } = tokens;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(new(
                DateTimeOffset.Now.ToString("yyyy-MM-dd"),
                Tokens,
                0,
                0,
                0,
                Tokens,
                0));
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
