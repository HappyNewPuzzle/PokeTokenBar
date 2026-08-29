using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class PokemonSpriteLoaderTests : IDisposable
{
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a-test");
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1];

    private readonly string _cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-SpriteTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task NormalAnimatedSuccess_DoesNotRequestStatic()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.True(asset!.IsAnimated);
        Assert.False(asset.IsShiny);
        Assert.Equal("image/gif", asset.ContentType);
        Assert.Equal([Animated(25, shiny: false)], handler.RequestUris);
    }

    [Fact]
    public async Task NormalAnimated404_FallsBackToNormalStatic()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, false)
                ? Response(HttpStatusCode.NotFound)
                : Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.False(asset!.IsAnimated);
        Assert.False(asset.IsShiny);
        Assert.Equal([Animated(25, false), Static(25, false)], handler.RequestUris);
    }

    [Fact]
    public async Task ShinyAnimatedSuccess_IsSelectedFirst()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));

        var asset = await Loader(handler).LoadAsync(25, shiny: true);

        Assert.True(asset!.IsAnimated);
        Assert.True(asset.IsShiny);
        Assert.Equal([Animated(25, true)], handler.RequestUris);
    }

    [Fact]
    public async Task ShinyAnimated404_FallsBackToShinyStatic()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, true)
                ? Response(HttpStatusCode.NotFound)
                : Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: true);

        Assert.False(asset!.IsAnimated);
        Assert.True(asset.IsShiny);
        Assert.Equal([Animated(25, true), Static(25, true)], handler.RequestUris);
    }

    [Fact]
    public async Task MissingShinyAssets_FallBackToNormalAnimatedBeforeNormalStatic()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, false)
                ? Response(HttpStatusCode.OK, Gif)
                : Response(HttpStatusCode.NotFound));

        var asset = await Loader(handler).LoadAsync(25, shiny: true);

        Assert.True(asset!.IsAnimated);
        Assert.False(asset.IsShiny);
        Assert.Equal(
            [Animated(25, true), Static(25, true), Animated(25, false)],
            handler.RequestUris);
    }

    [Fact]
    public async Task MissingShinyAndNormalAnimated_FallsBackToNormalStatic()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Static(25, false)
                ? Response(HttpStatusCode.OK, Png)
                : Response(HttpStatusCode.NotFound));

        var asset = await Loader(handler).LoadAsync(25, shiny: true);

        Assert.False(asset!.IsAnimated);
        Assert.False(asset.IsShiny);
        Assert.Equal(
            [Animated(25, true), Static(25, true), Animated(25, false), Static(25, false)],
            handler.RequestUris);
    }

    [Fact]
    public async Task AllCandidates404_ReturnsNull()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.NotFound));

        Assert.Null(await Loader(handler).LoadAsync(25, shiny: true));
        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Fact]
    public async Task ServerError_IsCandidateFailureAndFallbackContinues()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, false)
                ? Response(HttpStatusCode.InternalServerError)
                : Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.False(asset!.IsAnimated);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task EmptyBody_IsCandidateFailureAndFallbackContinues()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, false)
                ? Response(HttpStatusCode.OK, [])
                : Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.False(asset!.IsAnimated);
    }

    [Fact]
    public async Task MalformedImageBytes_AreRejectedAtRawFormatBoundary()
    {
        var handler = new StubHandler(request =>
            request.RequestUri == Animated(25, false)
                ? Response(HttpStatusCode.OK, Encoding.UTF8.GetBytes("not-a-gif"))
                : Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.False(asset!.IsAnimated);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task ContentTypeHeader_IsNotUsedForSelection()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif, "text/plain"));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.True(asset!.IsAnimated);
        Assert.Equal("image/gif", asset.ContentType);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedWithoutTryingFallbacks()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response(HttpStatusCode.OK, Gif);
        });
        using var cancellation = new CancellationTokenSource();
        var load = Loader(handler).LoadAsync(25, shiny: true, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.Single(handler.RequestUris);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(325)]
    [InlineData(649)]
    public void SourceUrls_UseSpeciesIdWithoutFormOrGenderSuffix(int id)
    {
        Assert.Equal(
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/animated/{id}.gif",
            Animated(id, false).AbsoluteUri);
        Assert.Equal(
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/animated/shiny/{id}.gif",
            Animated(id, true).AbsoluteUri);
        Assert.Equal(
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{id}.png",
            Static(id, false).AbsoluteUri);
        Assert.Equal(
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/shiny/{id}.png",
            Static(id, true).AbsoluteUri);
    }

    [Theory]
    [InlineData(false, false, "25-s")]
    [InlineData(true, false, "25-a")]
    [InlineData(false, true, "25-shs")]
    [InlineData(true, true, "25-sha")]
    public void CacheKey_MatchesSwiftFormat(bool animated, bool shiny, string expected)
    {
        Assert.Equal(expected, PokemonSpriteLoader.GetCacheKey(25, animated, shiny));
    }

    [Fact]
    public async Task SpeciesAbove649_SkipsAnimatedCandidate()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(650, shiny: false);

        Assert.False(asset!.IsAnimated);
        Assert.Equal([Static(650, false)], handler.RequestUris);
    }

    [Fact]
    public async Task StaticPreference_DoesNotRequestAnimatedCandidate()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, preferAnimated: false, shiny: true);

        Assert.False(asset!.IsAnimated);
        Assert.True(asset.IsShiny);
        Assert.Equal([Static(25, true)], handler.RequestUris);
    }

    [Fact]
    public async Task RepeatedSameCandidate_UsesMemoryCache()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));
        var loader = Loader(handler);

        await loader.LoadAsync(25, shiny: false);
        await loader.LoadAsync(25, shiny: false);

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task ShinyAndNormalHaveSeparateCacheKeys()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));
        var loader = Loader(handler);

        await loader.LoadAsync(25, shiny: false);
        await loader.LoadAsync(25, shiny: true);

        Assert.Equal([Animated(25, false), Animated(25, true)], handler.RequestUris);
    }

    [Fact]
    public async Task DifferentSpeciesHaveSeparateCacheKeys()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));
        var loader = Loader(handler);

        await loader.LoadAsync(1, shiny: false);
        await loader.LoadAsync(25, shiny: false);

        Assert.Equal([Animated(1, false), Animated(25, false)], handler.RequestUris);
    }

    [Fact]
    public async Task FailedCandidatesAreNotCached()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.NotFound));
        var loader = Loader(handler);

        await loader.LoadAsync(25, preferAnimated: false, shiny: false);
        await loader.LoadAsync(25, preferAnimated: false, shiny: false);

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task ConcurrentSameKeyRequestsAreNotDeduplicated()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                bothStarted.SetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            return Response(HttpStatusCode.OK, Png);
        });
        var loader = Loader(handler);

        var first = loader.LoadAsync(25, preferAnimated: false, shiny: false);
        var second = loader.LoadAsync(25, preferAnimated: false, shiny: false);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task DiskCacheHitSkipsNetworkAcrossLoaderInstances()
    {
        var firstHandler = new StubHandler(_ => Response(HttpStatusCode.OK, Png));
        await Loader(firstHandler).LoadAsync(25, preferAnimated: false, shiny: false);
        var secondHandler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("network used"));

        var asset = await Loader(secondHandler).LoadAsync(25, preferAnimated: false, shiny: false);

        Assert.NotNull(asset);
        Assert.Empty(secondHandler.RequestUris);
    }

    [Fact]
    public async Task DiskCacheUsesSwiftFilenameAndHasNoMetadataOrTtl()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Gif));

        await Loader(handler).LoadAsync(25, shiny: true);

        var file = Path.Combine(_cacheDirectory, "25-sha.gif");
        Assert.True(File.Exists(file));
        Assert.Equal(Gif, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFiles(_cacheDirectory));
    }

    [Fact]
    public async Task OldDiskCacheEntryIsStillUsedBecauseThereIsNoTtl()
    {
        Directory.CreateDirectory(_cacheDirectory);
        var file = Path.Combine(_cacheDirectory, "25-s.png");
        File.WriteAllBytes(file, Png);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddYears(-10));
        var handler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("network used"));

        var asset = await Loader(handler).LoadAsync(25, preferAnimated: false, shiny: false);

        Assert.NotNull(asset);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task CorruptDiskCandidateDoesNotTriggerNetworkReplacement()
    {
        Directory.CreateDirectory(_cacheDirectory);
        File.WriteAllBytes(Path.Combine(_cacheDirectory, "25-a.gif"), Encoding.UTF8.GetBytes("corrupt"));
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Png));

        var asset = await Loader(handler).LoadAsync(25, shiny: false);

        Assert.False(asset!.IsAnimated);
        Assert.Equal([Static(25, false)], handler.RequestUris);
    }

    [Fact]
    public async Task MemoryCacheEvictsLeastRecentlyUsedEntryAtConfiguredLimit()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, Png));
        var loader = new PokemonSpriteLoader(new HttpClient(handler), _cacheDirectory, memoryLimit: 2);
        await loader.LoadAsync(1, preferAnimated: false, shiny: false);
        await loader.LoadAsync(2, preferAnimated: false, shiny: false);
        await loader.LoadAsync(3, preferAnimated: false, shiny: false);
        Directory.Delete(_cacheDirectory, recursive: true);

        await loader.LoadAsync(1, preferAnimated: false, shiny: false);

        Assert.Equal(4, handler.RequestUris.Count);
    }

    private PokemonSpriteLoader Loader(StubHandler handler) =>
        new(new HttpClient(handler), _cacheDirectory);

    private static Uri Animated(int id, bool shiny) =>
        PokemonSpriteLoader.GetSourceUri(id, animated: true, shiny);

    private static Uri Static(int id, bool shiny) =>
        PokemonSpriteLoader.GetSourceUri(id, animated: false, shiny);

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        byte[]? bytes = null,
        string contentType = "application/octet-stream")
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(bytes ?? []),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

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

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return _response(request, cancellationToken);
        }
    }
}
