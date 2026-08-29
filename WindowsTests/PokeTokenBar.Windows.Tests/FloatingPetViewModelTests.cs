using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class FloatingPetViewModelTests
{
    [Fact]
    public void FreshCompanionExposesEggPresentation()
    {
        using var companion = CreateCompanion(new CompanionState());
        using var floating = new FloatingPetViewModel(companion);

        Assert.True(floating.IsEgg);
        Assert.Null(floating.PokemonId);
        Assert.False(floating.IsShiny);
        Assert.Null(floating.Sprite);
    }

    [Fact]
    public async Task RepresentativePokemonAndShinyStateAreProjected()
    {
        using var companion = CreateCompanion(StateWithActive(25, shiny: true));
        using var floating = new FloatingPetViewModel(companion);

        await companion.InitializeAsync();

        Assert.False(floating.IsEgg);
        Assert.Equal(25, floating.PokemonId);
        Assert.True(floating.IsShiny);
        Assert.Same(companion.Sprite, floating.Sprite);
    }

    [Fact]
    public async Task PinnedRepresentativeRemainsDistinctFromActivePokemon()
    {
        using var companion = CreateCompanion(
            StateWithActive(
                1,
                pathIds: [1, 2],
                stageIndex: 1,
                representativeSpeciesId: 1));
        using var floating = new FloatingPetViewModel(companion);

        await companion.InitializeAsync();

        Assert.Equal(2, companion.ActivePokemonId);
        Assert.Equal(1, floating.PokemonId);
        Assert.NotNull(floating.Sprite);
        Assert.NotNull(companion.CompanionSprite);
        Assert.NotSame(companion.CompanionSprite, floating.Sprite);
    }

    [Fact]
    public async Task RepresentativeChangeRelaysIdentityAndReplacementSprite()
    {
        using var companion = CreateCompanion(
            StateWithActive(1, pathIds: [1, 2], stageIndex: 1));
        using var floating = new FloatingPetViewModel(companion);
        await companion.InitializeAsync();
        var changes = new List<string?>();
        floating.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Assert.True(await companion.SelectRepresentativeAsync(1));

        Assert.Equal(1, floating.PokemonId);
        Assert.Same(companion.Sprite, floating.Sprite);
        Assert.Contains(nameof(FloatingPetViewModel.PokemonId), changes);
        Assert.Contains(nameof(FloatingPetViewModel.Sprite), changes);
    }

    [Fact]
    public void DisposeUnsubscribesFromCompanionChangesAndIsIdempotent()
    {
        using var companion = CreateCompanion(StateWithActive(1));
        var floating = new FloatingPetViewModel(companion);
        var changes = 0;
        floating.PropertyChanged += (_, _) => changes++;

        floating.Dispose();
        floating.Dispose();
        companion.Reset();

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task LateRepresentativeSpriteCannotReplaceNewerFloatingPresentation()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<PokemonSpriteAsset?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var companion = CreateCompanion(
            StateWithActive(1, pathIds: [1, 2], stageIndex: 1),
            (id, shiny, _) =>
            {
                if (id == 2)
                {
                    firstStarted.TrySetResult();
                    return firstResult.Task;
                }

                return Task.FromResult<PokemonSpriteAsset?>(Asset(id, shiny));
            });
        using var floating = new FloatingPetViewModel(companion);
        var initialize = companion.InitializeAsync();
        await firstStarted.Task;

        Assert.True(await companion.SelectRepresentativeAsync(1));
        var newerPresentation = floating.Sprite;
        firstResult.SetResult(Asset(2, shiny: false));
        await initialize;

        Assert.Equal(1, floating.PokemonId);
        Assert.Same(newerPresentation, floating.Sprite);
    }

    private static CompanionViewModel CreateCompanion(
        CompanionState state,
        Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>>? loadSprite = null)
    {
        var store = new CompanionStore(
            new FakeApi(),
            new FakePersistence(state),
            new Random(1));
        loadSprite ??= (id, shiny, _) =>
            Task.FromResult<PokemonSpriteAsset?>(Asset(id, shiny));
        return new CompanionViewModel(
            store,
            loadSprite,
            new FakeDecoder());
    }

    private static PokemonSpriteAsset Asset(int id, bool shiny) =>
        new(
            new byte[] { (byte)id },
            new Uri($"https://fixture.invalid/{id}-{shiny}.gif"),
            "image/gif",
            IsAnimated: true,
            shiny);

    private static CompanionState StateWithActive(
        int baseId,
        IReadOnlyList<int>? pathIds = null,
        int stageIndex = 0,
        bool shiny = false,
        int? representativeSpeciesId = null)
    {
        pathIds ??= [baseId];
        return new CompanionState
        {
            Active = new MonState
            {
                BaseId = baseId,
                PathIds = pathIds,
                PlannedPathIds = pathIds,
                StageIndex = stageIndex,
                TotalForms = pathIds.Count,
                IsShiny = shiny,
            },
            RepresentativeSpeciesId = representativeSpeciesId,
        };
    }

    private static BitmapSource Image(byte marker)
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
        return image;
    }

    private sealed class FakeDecoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation Decode(PokemonSpriteAsset asset)
        {
            var first = Image(asset.Data.Span[0]);
            var second = Image((byte)(asset.Data.Span[0] + 1));
            return new PokemonSpritePresentation(
                first,
                [
                    new AnimatedSpriteFrame(first, TimeSpan.FromMilliseconds(100)),
                    new AnimatedSpriteFrame(second, TimeSpan.FromMilliseconds(100)),
                ],
                true);
        }
    }

    private sealed class FakeApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(
            int baseSpeciesId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = new EvoNode(baseSpeciesId + 1, []);
            var line = new EvoLine(
                baseSpeciesId,
                new EvoNode(baseSpeciesId, [child]),
                PokemonRarity.Common,
                new Dictionary<int, IReadOnlyDictionary<string, string>>());
            return Task.FromResult(line);
        }

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([]);

        public Task<BaseSpecies?> GetBaseSpeciesAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(id, 255));
    }

    private sealed class FakePersistence(CompanionState state) : ICompanionPersistence
    {
        public CompanionState? Load() => state;

        public void Save(CompanionState state) { }

        public void Delete() { }
    }
}
