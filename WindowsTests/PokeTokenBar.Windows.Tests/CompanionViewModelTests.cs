using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CompanionViewModelTests
{
    [Fact]
    public async Task Initialize_FreshEggDoesNotRequestSprite()
    {
        var fixture = CreateFixture();

        await fixture.ViewModel.InitializeAsync();

        Assert.True(fixture.ViewModel.IsEgg);
        Assert.False(fixture.ViewModel.HasCompanion);
        Assert.Null(fixture.ViewModel.PokemonId);
        Assert.Equal("Token Egg", fixture.ViewModel.DisplayName);
        Assert.Null(fixture.ViewModel.Sprite);
        Assert.Empty(fixture.SpriteRequests);
        Assert.Equal(0, fixture.Api.LineCalls);
    }

    [Fact]
    public async Task Initialize_RestoredCompanionLoadsMetadataAndSprite()
    {
        var state = StateWithActive(
            baseId: 1,
            pathIds: [1, 2],
            stageIndex: 1,
            nature: PokemonNature.Jolly,
            rarity: PokemonRarity.Rare);
        var fixture = CreateFixture(state, Line(1, 2));

        await fixture.ViewModel.InitializeAsync();

        Assert.False(fixture.ViewModel.IsEgg);
        Assert.True(fixture.ViewModel.HasCompanion);
        Assert.Equal(2, fixture.ViewModel.ActivePokemonId);
        Assert.Equal(2, fixture.ViewModel.PokemonId);
        Assert.Equal("Ivysaur", fixture.ViewModel.DisplayName);
        Assert.Equal(PokemonNature.Jolly, fixture.ViewModel.Nature);
        Assert.Equal("Jolly", fixture.ViewModel.Personality);
        Assert.Equal(PokemonRarity.Rare, fixture.ViewModel.Rarity);
        Assert.Equal(1, fixture.ViewModel.StageIndex);
        Assert.Equal(2, fixture.ViewModel.TotalForms);
        Assert.True(fixture.ViewModel.IsFinalStage);
        Assert.NotNull(fixture.ViewModel.Sprite);
        Assert.Same(fixture.ViewModel.Sprite, fixture.ViewModel.CompanionSprite);
        Assert.Equal([(2, false)], fixture.SpriteRequests);
        Assert.Equal(1, fixture.Api.LineCalls);
    }

    [Fact]
    public async Task InitialCompanionControllerInitializesRestoredCompanionOnlyOnce()
    {
        var fixture = CreateFixture(StateWithActive(1, [1]), Line(1));
        var controller = new InitialCompanionController(fixture.ViewModel);

        await Task.WhenAll(controller.StartAsync(), controller.StartAsync());

        Assert.True(controller.HasStarted);
        Assert.Equal(1, fixture.Api.LineCalls);
        Assert.Single(fixture.SpriteRequests);
    }

    [Fact]
    public async Task InitialCompanionControllerDisposeCancelsPendingInitialization()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(
            StateWithActive(1, [1]),
            Line(1),
            spriteLoader: async (_, _, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            });
        var controller = new InitialCompanionController(fixture.ViewModel);
        var initialization = controller.StartAsync();
        await started.Task;

        controller.Dispose();
        controller.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
        Assert.False(fixture.ViewModel.IsSpriteLoading);
    }

    [Fact]
    public async Task SpriteLoadRaisesPropertyChangedForWpfBinding()
    {
        var fixture = CreateFixture(StateWithActive(1, [1]), Line(1));
        var changes = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await fixture.ViewModel.InitializeAsync();

        Assert.Contains(nameof(CompanionViewModel.Sprite), changes);
        Assert.Contains(nameof(CompanionViewModel.IsSpriteLoading), changes);
    }

    [Fact]
    public async Task Initialize_ShinyCompanionRequestsShinySprite()
    {
        var fixture = CreateFixture(
            StateWithActive(25, [25], shiny: true),
            Line(25));

        await fixture.ViewModel.InitializeAsync();

        Assert.True(fixture.ViewModel.IsShiny);
        Assert.Equal([(25, true)], fixture.SpriteRequests);
    }

    [Theory]
    [InlineData(AppLanguage.Ko, "이상해씨")]
    [InlineData(AppLanguage.En, "Bulbasaur")]
    public async Task Initialize_UsesStoredLanguageForCurrentPokemonName(
        AppLanguage language,
        string expectedName)
    {
        var fixture = CreateFixture(
            StateWithActive(1, [1], language: language),
            Line(1));

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(language, fixture.ViewModel.Language);
        Assert.Equal(expectedName, fixture.ViewModel.DisplayName);
    }

    [Theory]
    [InlineData(AppLanguage.Ko, "명랑")]
    [InlineData(AppLanguage.En, "Jolly")]
    [InlineData(AppLanguage.Ja, "ようき")]
    [InlineData(AppLanguage.Es, "Alegre")]
    [InlineData(AppLanguage.Fr, "Jovial")]
    [InlineData(AppLanguage.Pt, "Alegre")]
    public async Task Initialize_LocalizesPersonalityLikeSwift(
        AppLanguage language,
        string expectedPersonality)
    {
        var fixture = CreateFixture(
            StateWithActive(1, [1], nature: PokemonNature.Jolly, language: language),
            Line(1));

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(expectedPersonality, fixture.ViewModel.Personality);
    }

    [Fact]
    public async Task EggProgressMatchesSwiftFiveMillionTokenThreshold()
    {
        var fixture = CreateFixture(
            new CompanionState
            {
                Language = AppLanguage.En,
                EggUsage = 2_500_000,
            });

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(0.5, fixture.ViewModel.Progress);
        Assert.Equal("2.5M to hatch", fixture.ViewModel.ProgressText);
        Assert.Null(fixture.ViewModel.StageText);
    }

    [Fact]
    public async Task EvolutionProgressMatchesSwiftRarityAndStageThreshold()
    {
        var fixture = CreateFixture(
            StateWithActive(
                1,
                [1, 2],
                usedAtStage: 125_000_000,
                language: AppLanguage.En),
            Line(1, 2));

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal("Stage 1 / 2", fixture.ViewModel.StageText);
        Assert.Equal(0.5, fixture.ViewModel.Progress);
        Assert.Equal("125M to next evolution", fixture.ViewModel.ProgressText);
    }

    [Fact]
    public async Task SpriteFailureKeepsCompanionStateAndExposesNoErrorText()
    {
        var fixture = CreateFixture(
            StateWithActive(1, [1]),
            Line(1),
            spriteLoader: (_, _, _) => throw new IOException("decode source failed"));

        await fixture.ViewModel.InitializeAsync();

        Assert.True(fixture.ViewModel.HasCompanion);
        Assert.Equal(1, fixture.ViewModel.PokemonId);
        Assert.Equal("Bulbasaur", fixture.ViewModel.DisplayName);
        Assert.Null(fixture.ViewModel.Sprite);
        Assert.False(fixture.ViewModel.IsSpriteLoading);
        Assert.DoesNotContain(
            typeof(CompanionViewModel).GetProperties(),
            property => property.Name.Contains("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Initialize_CallerCancellationIsPropagatedAndCanBeRetried()
    {
        var callCount = 0;
        var fixture = CreateFixture(
            StateWithActive(1, [1]),
            Line(1),
            spriteLoader: async (id, shiny, cancellationToken) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return Asset(id, shiny);
            });
        using var cancellation = new CancellationTokenSource();
        var initialize = fixture.ViewModel.InitializeAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialize);
        Assert.True(fixture.ViewModel.HasCompanion);
        Assert.False(fixture.ViewModel.IsSpriteLoading);

        await fixture.ViewModel.RefreshAsync();
        Assert.NotNull(fixture.ViewModel.Sprite);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RepeatedRefreshDoesNotReloadUnchangedSpriteIdentity()
    {
        var fixture = CreateFixture(StateWithActive(1, [1]), Line(1));

        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshAsync();
        await fixture.ViewModel.RefreshAsync();

        Assert.Single(fixture.SpriteRequests);
    }

    [Fact]
    public async Task LateSpriteResultCannotReplaceNewerPokemonSprite()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<PokemonSpriteAsset?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = CreateFixture(
            StateWithActive(1, [1]),
            Line(1),
            spriteLoader: (id, shiny, _) =>
            {
                if (id == 1)
                {
                    firstStarted.TrySetResult();
                    return firstResult.Task;
                }

                return Task.FromResult<PokemonSpriteAsset?>(Asset(id, shiny));
            });
        fixture.Api.Lines[4] = Line(4);
        var initialize = fixture.ViewModel.InitializeAsync();
        await firstStarted.Task;

        Assert.True(await fixture.ViewModel.HatchSpecificAsync(4));
        var newerSprite = fixture.ViewModel.Sprite;
        Assert.NotNull(newerSprite);
        Assert.Equal(4, fixture.ViewModel.PokemonId);

        firstResult.SetResult(Asset(1, shiny: false));
        await initialize;

        Assert.Same(newerSprite, fixture.ViewModel.Sprite);
        Assert.Equal(4, fixture.ViewModel.PokemonId);
    }

    [Fact]
    public async Task RepresentativeChangeLoadsItsSpriteButKeepsActiveCompanionFields()
    {
        var fixture = CreateFixture(
            StateWithActive(1, [1, 2], stageIndex: 1, nature: PokemonNature.Calm),
            Line(1, 2));
        await fixture.ViewModel.InitializeAsync();

        Assert.True(await fixture.ViewModel.SelectRepresentativeAsync(1));

        Assert.Equal(2, fixture.ViewModel.ActivePokemonId);
        Assert.Equal(1, fixture.ViewModel.PokemonId);
        Assert.Equal("Ivysaur", fixture.ViewModel.DisplayName);
        Assert.Equal(PokemonNature.Calm, fixture.ViewModel.Nature);
        Assert.Equal([(2, false), (1, false)], fixture.SpriteRequests);
        Assert.NotNull(fixture.ViewModel.CompanionSprite);
        Assert.NotSame(fixture.ViewModel.Sprite, fixture.ViewModel.CompanionSprite);
    }

    [Fact]
    public async Task ResetReturnsToEggAndClearsSpriteWithoutAnotherRequest()
    {
        var fixture = CreateFixture(StateWithActive(1, [1]), Line(1));
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.Reset();

        Assert.True(fixture.ViewModel.IsEgg);
        Assert.False(fixture.ViewModel.HasCompanion);
        Assert.Equal("Token Egg", fixture.ViewModel.DisplayName);
        Assert.Null(fixture.ViewModel.Sprite);
        Assert.Null(fixture.ViewModel.CompanionSprite);
        Assert.Single(fixture.SpriteRequests);
        Assert.Equal(1, fixture.Persistence.DeleteCalls);
    }

    [Fact]
    public async Task HatchSuccessUpdatesStateAndLoadsSprite()
    {
        var fixture = CreateFixture(
            state: new CompanionState { Language = AppLanguage.En },
            line: Line(4));

        var success = await fixture.ViewModel.HatchSpecificAsync(4);

        Assert.True(success);
        Assert.False(fixture.ViewModel.IsEgg);
        Assert.Equal(4, fixture.ViewModel.PokemonId);
        Assert.Equal("Charmander", fixture.ViewModel.DisplayName);
        Assert.NotNull(fixture.ViewModel.Sprite);
        Assert.Single(fixture.SpriteRequests);
        Assert.False(fixture.ViewModel.IsHatching);
    }

    [Fact]
    public async Task HatchExposesHatchingStateWhileSelectionIsPending()
    {
        var fixture = CreateFixture(state: new CompanionState { Language = AppLanguage.En });
        var result = new TaskCompletionSource<EvoLine>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.PendingLines[4] = result.Task;

        var hatch = fixture.ViewModel.HatchSpecificAsync(4);

        Assert.True(fixture.ViewModel.IsHatching);
        result.SetResult(Line(4));
        Assert.True(await hatch);
        Assert.False(fixture.ViewModel.IsHatching);
    }

    [Fact]
    public async Task HatchFailurePreservesExistingStateAndSprite()
    {
        var fixture = CreateFixture(StateWithActive(1, [1]), Line(1));
        await fixture.ViewModel.InitializeAsync();
        var originalSprite = fixture.ViewModel.Sprite;
        fixture.Api.Errors[4] = new HttpRequestException("offline");

        var success = await fixture.ViewModel.HatchSpecificAsync(4);

        Assert.False(success);
        Assert.Equal(1, fixture.ViewModel.PokemonId);
        Assert.Equal("Bulbasaur", fixture.ViewModel.DisplayName);
        Assert.Same(originalSprite, fixture.ViewModel.Sprite);
        Assert.Single(fixture.SpriteRequests);
    }

    [Fact]
    public async Task FakeStoreToRawSpriteToWpfPresentationFlowNeedsNoInternet()
    {
        var fixture = CreateFixture(
            StateWithActive(1, [1]),
            Line(1),
            spriteLoader: (id, shiny, _) => Task.FromResult<PokemonSpriteAsset?>(
                new PokemonSpriteAsset(
                    OnePixelPng,
                    new Uri($"https://fixture.invalid/{id}.png"),
                    "image/png",
                    IsAnimated: false,
                    shiny)),
            decoder: new WpfPokemonSpriteDecoder());

        await fixture.ViewModel.InitializeAsync();

        Assert.NotNull(fixture.ViewModel.Sprite);
        Assert.Equal(1, fixture.ViewModel.Sprite.StaticImage.PixelWidth);
        Assert.True(fixture.ViewModel.Sprite.StaticImage.IsFrozen);
    }

    [Fact]
    public void ViewModelDoesNotOwnFilesystemOrJsonPersistenceDetails()
    {
        var fieldTypes = typeof(CompanionViewModel)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(JsonCompanionPersistence), fieldTypes);
        Assert.DoesNotContain(typeof(ICompanionPersistence), fieldTypes);
        Assert.DoesNotContain(typeof(FileInfo), fieldTypes);
    }

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static Fixture CreateFixture(
        CompanionState? state = null,
        EvoLine? line = null,
        Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>>? spriteLoader = null,
        IPokemonSpriteDecoder? decoder = null)
    {
        var api = new FakeApi();
        if (line is not null)
        {
            api.Lines[line.BaseId] = line;
        }

        var persistence = new FakePersistence { Loaded = state };
        var store = new CompanionStore(api, persistence, new FixedRandom(0, 1, 3));
        var requests = new List<(int Id, bool Shiny)>();
        spriteLoader ??= (id, shiny, _) =>
        {
            requests.Add((id, shiny));
            return Task.FromResult<PokemonSpriteAsset?>(Asset(id, shiny));
        };
        var viewModel = new CompanionViewModel(store, spriteLoader, decoder ?? new FakeDecoder());
        return new Fixture(viewModel, api, persistence, requests);
    }

    private static CompanionState StateWithActive(
        int baseId,
        IReadOnlyList<int> pathIds,
        int stageIndex = 0,
        long usedAtStage = 0,
        bool shiny = false,
        PokemonNature nature = PokemonNature.Hardy,
        PokemonRarity rarity = PokemonRarity.Common,
        AppLanguage language = AppLanguage.En) =>
        new()
        {
            Language = language,
            Active = new MonState
            {
                BaseId = baseId,
                PathIds = pathIds,
                PlannedPathIds = pathIds,
                StageIndex = stageIndex,
                UsedAtStage = usedAtStage,
                Rarity = rarity,
                TotalForms = pathIds.Count,
                IsShiny = shiny,
                Nature = nature,
            },
        };

    private static EvoLine Line(params int[] ids)
    {
        var node = new EvoNode(ids[^1], []);
        for (var index = ids.Length - 2; index >= 0; index--)
        {
            node = new EvoNode(ids[index], [node]);
        }

        var names = ids.ToDictionary(
            id => id,
            id => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["en"] = id switch
                {
                    1 => "Bulbasaur",
                    2 => "Ivysaur",
                    4 => "Charmander",
                    25 => "Pikachu",
                    _ => $"#{id}",
                },
                ["ko"] = id switch
                {
                    1 => "이상해씨",
                    2 => "이상해풀",
                    4 => "파이리",
                    25 => "피카츄",
                    _ => $"#{id}",
                },
            });
        return new EvoLine(ids[0], node, PokemonRarity.Common, names);
    }

    private static PokemonSpriteAsset Asset(int id, bool shiny) =>
        new(
            new byte[] { (byte)id },
            new Uri($"https://fixture.invalid/{id}-{shiny}.png"),
            "image/png",
            IsAnimated: false,
            shiny);

    private static PokemonSpritePresentation Presentation(byte marker)
    {
        var image = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { marker, marker, marker, 255 },
            4);
        image.Freeze();
        return new PokemonSpritePresentation(image, [], false);
    }

    private sealed record Fixture(
        CompanionViewModel ViewModel,
        FakeApi Api,
        FakePersistence Persistence,
        List<(int Id, bool Shiny)> SpriteRequests);

    private sealed class FakeDecoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation? Decode(PokemonSpriteAsset asset) =>
            asset.Data.IsEmpty ? null : Presentation(asset.Data.Span[0]);
    }

    private sealed class FakeApi : IPokeApiClient
    {
        public Dictionary<int, EvoLine> Lines { get; } = [];
        public Dictionary<int, Exception> Errors { get; } = [];
        public Dictionary<int, Task<EvoLine>> PendingLines { get; } = [];
        public int LineCalls { get; private set; }

        public Task<EvoLine> GetLineAsync(
            int baseSpeciesId,
            CancellationToken cancellationToken = default)
        {
            LineCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Errors.TryGetValue(baseSpeciesId, out var error))
            {
                throw error;
            }

            if (PendingLines.TryGetValue(baseSpeciesId, out var pending))
            {
                return pending.WaitAsync(cancellationToken);
            }

            return Task.FromResult(
                Lines.GetValueOrDefault(baseSpeciesId) ?? CompanionViewModelTests.Line(baseSpeciesId));
        }

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([]);

        public Task<BaseSpecies?> GetBaseSpeciesAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(id, 255));
    }

    private sealed class FakePersistence : ICompanionPersistence
    {
        public CompanionState? Loaded { get; init; }
        public int DeleteCalls { get; private set; }

        public CompanionState? Load() => Loaded;
        public void Save(CompanionState state) { }
        public void Delete() => DeleteCalls++;
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private readonly Queue<int> _values = new(values);

        public override int Next(int maxValue)
        {
            var value = _values.Count == 0 ? 1 : _values.Dequeue();
            return Math.Clamp(value, 0, maxValue - 1);
        }

        public override int Next(int minValue, int maxValue) =>
            minValue + Next(maxValue - minValue);
    }
}
