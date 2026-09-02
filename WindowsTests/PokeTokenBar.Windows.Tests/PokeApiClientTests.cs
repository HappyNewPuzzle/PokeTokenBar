using System.Net;
using System.Text;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class PokeApiClientTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "PokeTokenBar.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetLine_UsesSpeciesIdAndTrustedEvolutionEndpoint()
    {
        var handler = HandlerForBulbasaurLine();
        var client = Client(handler);

        await client.GetLineAsync(1);

        Assert.Equal(
            [
                "https://pokeapi.co/api/v2/pokemon-species/1",
                "https://pokeapi.co/api/v2/evolution-chain/1/",
                "https://pokeapi.co/api/v2/pokemon-species/2",
            ],
            handler.Requests.Select(request => request.Uri));
    }

    [Fact]
    public async Task GetLine_MapsTreeRarityAndOnlySupportedNames()
    {
        var line = await Client(HandlerForBulbasaurLine()).GetLineAsync(1);

        Assert.Equal(1, line.BaseId);
        Assert.Equal(PokemonRarity.Rare, line.Rarity);
        Assert.Equal(2, line.TotalForms);
        Assert.Equal(2, Assert.Single(line.Tree.Children).SpeciesId);
        Assert.Equal("이상해씨", line.Names[1]["ko"]);
        Assert.Equal("Bulbasaur", line.Names[1]["en"]);
        Assert.Equal("ignored", line.Names[1]["de"]);
    }

    [Fact]
    public async Task SuccessfulLine_IsCachedAndReusesInjectedHttpClient()
    {
        var handler = HandlerForBulbasaurLine();
        var client = Client(handler);

        var first = await client.GetLineAsync(1);
        var second = await client.GetLineAsync(1);

        Assert.Same(first, second);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBaseSpecies_ReturnsOnlyEvolutionRootsAndExcludesDitto()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/1", StringComparison.Ordinal)
                ? Json(SpeciesJson(1, 45, evolvesFrom: null))
                : Json(SpeciesJson(2, 45, evolvesFrom: "bulbasaur")));
        var client = Client(handler);

        Assert.Equal(new BaseSpecies(1, 45), await client.GetBaseSpeciesAsync(1));
        Assert.Null(await client.GetBaseSpeciesAsync(2));
        Assert.Null(await client.GetBaseSpeciesAsync(PokemonOdds.DittoSpeciesId));
        Assert.DoesNotContain(handler.Requests, request => request.Uri.EndsWith("/132", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BaseIndex_UsesSingleGraphQlPostAndCachesResult()
    {
        var handler = new StubHandler(_ => Json("""
            {"data":{"pokemonspecies":[{"id":1,"capture_rate":45},{"id":4,"capture_rate":45}]}}
            """));
        var client = Client(handler);

        var first = await client.GetBaseSpeciesIndexAsync();
        var second = await client.GetBaseSpeciesIndexAsync();

        Assert.Equal([new BaseSpecies(1, 45), new BaseSpecies(4, 45)], first);
        Assert.Same(first, second);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graphql.pokeapi.co/v1beta2", request.Uri);
        Assert.Contains("_lte: 649", request.Body);
        Assert.Contains("_neq: 132", request.Body);
    }

    [Fact]
    public async Task BaseIndex_FreshDiskCacheReturnsWithoutNetwork()
    {
        await WriteSnapshotAsync(Now - TimeSpan.FromDays(29), [new BaseSpecies(7, 45)]);
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));

        var result = await CacheClient(handler).GetBaseSpeciesIndexAsync();

        Assert.Equal([new BaseSpecies(7, 45)], result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BaseIndex_NoCacheFetchesPersistsAndRoundTrips()
    {
        var handler = new StubHandler(_ => Json(
            """{"data":{"pokemonspecies":[{"id":6,"capture_rate":45}]}}"""));

        var fetched = await CacheClient(handler).GetBaseSpeciesIndexAsync();
        var offline = new StubHandler(_ => throw new InvalidOperationException("Fresh disk cache expected."));
        var roundTripped = await CacheClient(offline).GetBaseSpeciesIndexAsync();

        Assert.Equal([new BaseSpecies(6, 45)], fetched);
        Assert.Equal(fetched, roundTripped);
        Assert.Single(handler.Requests);
        Assert.Empty(offline.Requests);
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "*.tmp-*"));
    }

    [Fact]
    public async Task BaseIndex_StaleCacheReturnsImmediatelyAndRebuildsInBackground()
    {
        await WriteSnapshotAsync(Now - TimeSpan.FromDays(30), [new BaseSpecies(7, 45)]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Json("""{"data":{"pokemonspecies":[{"id":8,"capture_rate":90}]}}""");
        });

        var result = await CacheClient(handler).GetBaseSpeciesIndexAsync();

        Assert.Equal([new BaseSpecies(7, 45)], result);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        await WaitUntilAsync(async () => (await ReadSnapshotEntriesAsync()) == 8);
    }

    [Fact]
    public async Task BaseIndex_FailedBackgroundRebuildKeepsStaleSnapshot()
    {
        await WriteSnapshotAsync(Now - TimeSpan.FromDays(31), [new BaseSpecies(7, 45)]);
        var original = await File.ReadAllTextAsync(CachePath);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await CacheClient(handler).GetBaseSpeciesIndexAsync();
        await WaitUntilAsync(() => Task.FromResult(handler.Requests.Count == 1));

        Assert.Equal([new BaseSpecies(7, 45)], result);
        Assert.Equal(original, await File.ReadAllTextAsync(CachePath));
    }

    [Fact]
    public async Task BaseIndex_CorruptCacheFallsBackToNetworkAndReplacesIt()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(CachePath, "{bad");
        var handler = new StubHandler(_ => Json(
            """{"data":{"pokemonspecies":[{"id":9,"capture_rate":120}]}}"""));

        var result = await CacheClient(handler).GetBaseSpeciesIndexAsync();

        Assert.Equal([new BaseSpecies(9, 120)], result);
        Assert.Equal(9, await ReadSnapshotEntriesAsync());
    }

    [Fact]
    public async Task BaseIndex_ConcurrentStaleReadsStartOnlyOneRebuild()
    {
        await WriteSnapshotAsync(Now - TimeSpan.FromDays(31), [new BaseSpecies(7, 45)]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await release.Task.WaitAsync(cancellationToken);
            return Json("""{"data":{"pokemonspecies":[{"id":8,"capture_rate":90}]}}""");
        });
        var client = CacheClient(handler);

        await Task.WhenAll(
            client.GetBaseSpeciesIndexAsync(),
            client.GetBaseSpeciesIndexAsync(),
            client.GetBaseSpeciesIndexAsync());
        await WaitUntilAsync(() => Task.FromResult(handler.Requests.Count == 1));
        release.SetResult();
        await WaitUntilAsync(async () => (await ReadSnapshotEntriesAsync()) == 8);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NonSuccessStatus_ThrowsHttpRequestException()
    {
        var client = Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLineAsync(1));
    }

    [Fact]
    public async Task MalformedJson_ThrowsJsonException()
    {
        var client = Client(new StubHandler(_ => Json("{bad")));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => client.GetLineAsync(1));
    }

    [Fact]
    public async Task MissingRequiredField_ThrowsJsonException()
    {
        var client = Client(new StubHandler(_ => Json("""
            {"capture_rate":45,"is_legendary":false,"is_mythical":false,"names":[]}
            """)));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => client.GetLineAsync(1));
    }

    [Theory]
    [InlineData("http://pokeapi.co/api/v2/evolution-chain/1/")]
    [InlineData("https://example.com/api/v2/evolution-chain/1/")]
    [InlineData("not a url")]
    public async Task UntrustedEvolutionUrl_IsRejectedWithoutSecondRequest(string chainUrl)
    {
        var handler = new StubHandler(_ => Json(SpeciesJson(1, 45, chainUrl: chainUrl)));
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLineAsync(1));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(handler).GetLineAsync(1, cancellation.Token));
    }

    [Fact]
    public async Task RequestTimeout_CancelsSlowRequest()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });
        var client = new PokeApiClient(new HttpClient(handler), TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLineAsync(1));
    }

    [Fact]
    public async Task ClientDoesNotRequestSpritePayloads()
    {
        var handler = HandlerForBulbasaurLine();

        await Client(handler).GetLineAsync(1);

        Assert.All(handler.Requests, request =>
            Assert.DoesNotContain("/pokemon/", request.Uri, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CachePath => Path.Combine(_temporaryDirectory, "base-index.json");

    private PokeApiClient Client(StubHandler handler) =>
        new(new HttpClient(handler), TimeSpan.FromSeconds(5), CachePath, new FixedTimeProvider(Now));

    private PokeApiClient CacheClient(StubHandler handler) => Client(handler);

    private async Task WriteSnapshotAsync(DateTimeOffset fetchedAt, BaseSpecies[] entries)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(
            CachePath,
            System.Text.Json.JsonSerializer.Serialize(new { FetchedAt = fetchedAt, Entries = entries }));
    }

    private async Task<int?> ReadSnapshotEntriesAsync()
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(CachePath));
            return document.RootElement.GetProperty("Entries")[0].GetProperty("Id").GetInt32();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(await condition());
    }

    private static StubHandler HandlerForBulbasaurLine() =>
        new(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/pokemon-species/1" => Json(SpeciesJson(1, 45)),
            "/api/v2/pokemon-species/2" => Json(SpeciesJson(2, 45, evolvesFrom: "bulbasaur")),
            "/api/v2/evolution-chain/1/" => Json("""
                {
                  "chain": {
                    "species": {"name":"bulbasaur","url":"https://pokeapi.co/api/v2/pokemon-species/1/"},
                    "evolves_to": [
                      {
                        "species": {"name":"ivysaur","url":"https://pokeapi.co/api/v2/pokemon-species/2/"},
                        "evolves_to": []
                      }
                    ]
                  }
                }
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

    private static string SpeciesJson(
        int id,
        int captureRate,
        string? evolvesFrom = null,
        string chainUrl = "https://pokeapi.co/api/v2/evolution-chain/1/")
    {
        var koreanName = id == 1 ? "이상해씨" : "이상해풀";
        var englishName = id == 1 ? "Bulbasaur" : "Ivysaur";
        object? evolvesFromValue = evolvesFrom is null
            ? "null"
            : new Dictionary<string, object?> { ["name"] = evolvesFrom, ["url"] = null };
        if (evolvesFrom is null)
        {
            evolvesFromValue = null;
        }

        return System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["capture_rate"] = captureRate,
                ["is_legendary"] = false,
                ["is_mythical"] = false,
                ["names"] = new object[]
                {
                    new { name = koreanName, language = new { name = "ko", url = (string?)null } },
                    new { name = englishName, language = new { name = "en", url = (string?)null } },
                    new { name = "ignored", language = new { name = "de", url = (string?)null } },
                },
                ["evolution_chain"] = new { url = chainUrl },
                ["evolves_from_species"] = evolvesFromValue,
            });
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this((request, _) => Task.FromResult(response(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsoluteUri, body));
            return await _response(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? Body);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
