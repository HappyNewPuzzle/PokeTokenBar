using System.Net;
using System.Net.Http;
using System.Text;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class PokemonDetailsTests : IDisposable
{
    private readonly ProfileFixture _fixture = new();
    private const string PokemonJson = """
        {"id":1,"name":"bulbasaur","height":7,"weight":69,"base_experience":64,
         "types":[{"slot":2,"type":{"name":"poison"}},{"slot":1,"type":{"name":"grass"}}],
         "stats":[{"stat":{"name":"hp"},"base_stat":45},{"stat":{"name":"alien"},"base_stat":999},
                  {"stat":{"name":"hp"},"base_stat":46}],
         "abilities":[{"ability":{"name":"overgrow"},"slot":1,"is_hidden":false}],
         "moves":[
          {"move":{"name":"tackle"},"version_group_details":[
           {"version_group":{"name":"black-2-white-2"},"move_learn_method":{"name":"level-up"},"level_learned_at":1},
           {"version_group":{"name":"scarlet-violet"},"move_learn_method":{"name":"level-up"},"level_learned_at":9}]},
          {"move":{"name":"future-only"},"version_group_details":[
           {"version_group":{"name":"scarlet-violet"},"move_learn_method":{"name":"level-up"},"level_learned_at":1}]},
          {"move":{"name":"tackle"},"version_group_details":[
           {"version_group":{"name":"black-2-white-2"},"move_learn_method":{"name":"level-up"},"level_learned_at":1}]}
         ]}
        """;

    [Fact]
    public async Task ParserNormalizesSupportedMovesBeforeMemoryAndDiskAndMemoryAvoidsIO()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var api = Client(http);
        var details = await api.GetPokemonDetailsAsync(1);
        Assert.Equal(1, details.SpeciesId);
        Assert.Equal("bulbasaur", details.Name);
        Assert.Equal(7, details.Height);
        Assert.Equal(69, details.Weight);
        Assert.Equal(64, details.BaseExperience);
        Assert.Equal(1, details.GenderRate);
        Assert.Equal(["grass", "poison"], details.Types);
        Assert.Equal(46, Assert.Single(details.BaseStats).Value);
        Assert.Equal("overgrow", Assert.Single(details.Abilities).Name);
        var move = Assert.Single(details.Moves);
        Assert.Equal("tackle", move.Name);
        Assert.Equal(new PokemonMoveLearnMethod("level-up", 1), Assert.Single(move.LearnMethods));
        var disk = File.ReadAllText(Path.Combine(_fixture.DirectoryPath, "1.json"));
        Assert.DoesNotContain("scarlet", disk);
        Assert.DoesNotContain("future-only", disk);
        Assert.DoesNotContain("version_group", disk);
        File.WriteAllText(Path.Combine(_fixture.DirectoryPath, "1.json"), "broken");
        handler.Fail = true;
        Assert.Same(details, await api.GetPokemonDetailsAsync(1));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task FreshDiskThenExpiredRefreshThenStaleOfflineFallback()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var clock = new Clock();
        await Client(http, clock).GetPokemonDetailsAsync(1);
        Assert.Equal(2, handler.Count);
        await Client(http, clock).GetPokemonDetailsAsync(1);
        Assert.Equal(2, handler.Count);
        clock.Now += TimeSpan.FromDays(31);
        handler.Pokemon = PokemonJson.Replace("bulbasaur", "refreshed");
        Assert.Equal("refreshed", (await Client(http, clock).GetPokemonDetailsAsync(1)).Name);
        Assert.Equal(4, handler.Count);
        clock.Now += TimeSpan.FromDays(31);
        handler.Fail = true;
        Assert.Equal("refreshed", (await Client(http, clock).GetPokemonDetailsAsync(1)).Name);
        Assert.Equal(5, handler.Count);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{\"fetchedAt\":\"2026-09-22T00:00:00Z\",\"details\":null}")]
    [InlineData("{\"details\":{\"speciesId\":1,\"name\":\"bad\",\"moves\":[null]}}")]
    public async Task CorruptDiskIsIgnoredAndRebuilt(string corrupt)
    {
        Directory.CreateDirectory(_fixture.DirectoryPath);
        var path = Path.Combine(_fixture.DirectoryPath, "1.json");
        File.WriteAllText(path, corrupt);
        using var http = new HttpClient(new Handler());
        Assert.Equal("bulbasaur", (await Client(http).GetPokemonDetailsAsync(1)).Name);
        Assert.DoesNotContain("bad", File.ReadAllText(path));
    }

    [Fact]
    public async Task MissingOptionalArraysAreSafeAndWrongSpeciesIsRejectedWithoutCaching()
    {
        using var handler = new Handler { Pokemon = "{\"id\":1,\"name\":\"minimal\"}" };
        using var http = new HttpClient(handler);
        var details = await Client(http).GetPokemonDetailsAsync(1);
        Assert.Empty(details.BaseStats);
        Assert.Empty(details.Abilities);
        Assert.Empty(details.Moves);
        Assert.Null(PokemonProfile.Create().Enrich(details).AbilityName);
        handler.Pokemon = "{\"id\":3,\"name\":\"wrong\"}";
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(http).GetPokemonDetailsAsync(2));
        Assert.False(File.Exists(Path.Combine(_fixture.DirectoryPath, "2.json")));
    }

    private PokeApiClient Client(HttpClient http, TimeProvider? time = null) =>
        new(http, timeProvider: time, detailsCacheDirectory: _fixture.DirectoryPath);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public bool Fail { get; set; }
        public string Pokemon { get; set; } = PokemonJson;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (Fail) throw new HttpRequestException("fixture offline");
            var species = request.RequestUri!.AbsolutePath.Contains("pokemon-species");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(species ? "{\"id\":1,\"gender_rate\":1}" : Pokemon, Encoding.UTF8, "application/json") });
        }
    }
    public void Dispose() => _fixture.Dispose();
}
