using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;
using System.Text.Json;

namespace PokeTokenBar.Windows.Tests;

public sealed class ClaudeIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Claude-Integration-{Guid.NewGuid():N}");

    public ClaudeIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ClaudeFileFlowsThroughStoreViewModelAndProviderNeutralCompanionLedger()
    {
        var path = Path.Combine(_directory, "project", "session.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var timestamp = DateTimeOffset.Now.ToUniversalTime();
        File.WriteAllText(path, Line("m1", "r1", timestamp, 10));
        var codex = new MutableProvider("codex", 100);
        var usage = new UsageViewModel(new UsageStore(
            [codex, new LocalClaudeUsageProvider([_directory])]));
        var companion = new CompanionStore(new UnusedApi(), new MemoryPersistence());
        using var controller = new UsageCompanionController(
            usage,
            companion,
            _ => Task.CompletedTask);

        await usage.RefreshAsync();
        await controller.LastUpdate;
        Assert.Equal(0, companion.State.EggUsage);
        Assert.Equal(100, companion.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(10, companion.State.ClaimedTodayTokensByProvider["claude_code"]);

        codex.Tokens = 150;
        File.AppendAllText(path, Environment.NewLine + Line("m2", "r2", timestamp.AddSeconds(1), 20));
        await usage.RefreshAsync();
        await controller.LastUpdate;
        usage.SelectedProviderId = "claude_code";

        Assert.Equal(70, companion.State.EggUsage);
        Assert.Equal(150, companion.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(30, companion.State.ClaimedTodayTokensByProvider["claude_code"]);
        Assert.Equal("Claude Code", usage.ProviderName);
        Assert.Equal(30, usage.TodayTokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string Line(
        string id,
        string request,
        DateTimeOffset timestamp,
        long input) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            requestId = request,
            timestamp = timestamp.ToString("O"),
            message = new
            {
                id,
                model = "claude-opus-4-8",
                usage = new
                {
                    input_tokens = input,
                    output_tokens = 0,
                    cache_creation_input_tokens = 0,
                    cache_read_input_tokens = 0,
                },
            },
        });

    private sealed class MutableProvider(string id, long tokens) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => "Codex";
        public bool ReportsCost => true;
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
