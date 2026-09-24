using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;
using static PokeTokenBar.Windows.Tests.ProfileFixture;

namespace PokeTokenBar.Windows.Tests;

public sealed class PokemonDetailViewModelTests
{
    [Fact]
    public void IndividualsMatchExactSpeciesActiveFirstNewestDexNextWithoutMutatingState()
    {
        var active = Mon(2) with { Profile = Profile("active", 42) };
        var state = State(active) with { Dex = [Entry("old", 100, 1), Entry("released", 70, 3, released: true), Entry("new", 100, 2)] };
        using var store = Store(state);
        var before = store.State;
        Assert.Empty(store.GetPokemonIndividuals(99));
        Assert.Empty(store.GetPokemonIndividuals(1)); // Neither Active.PathIds nor Dex.ChainOrder grants an individual.
        Assert.Empty(store.GetPokemonIndividuals(2));
        var individuals = store.GetPokemonIndividuals(3);
        Assert.Equal(["active", "released", "new", "old"], individuals.Select(i => i.Profile.InstanceId));
        Assert.True(individuals[0].IsCurrent);
        Assert.True(individuals[1].IsReleased);
        Assert.Equal(70, individuals[1].Profile.Level);
        Assert.Same(before.Active!.Profile, individuals[0].Profile);
        Assert.Same(before.Dex[1].Profile, individuals[1].Profile);
        Assert.Same(before, store.State);
        Assert.Equal(3, store.State.Dex.Count);
    }

    [Fact]
    public async Task OpensOnlySelectedSpeciesLoadsAndPresentsExactProfileAndSpeciesData()
    {
        var provider = new Provider { Response = Metadata };
        using var store = Store(State(Mon(2) with { Profile = Profile("active", 42), Nature = PokemonNature.Adamant }), provider);
        using var vm = ViewModel(store);
        Assert.Empty(provider.Requests);
        Assert.True(vm.IsCollectionVisible);
        await vm.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        Assert.Equal([3], provider.Requests);
        Assert.True(vm.IsOpen);
        Assert.False(vm.IsCollectionVisible);
        Assert.True(vm.HasDetails);
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.Equal("Venusaur", vm.Name);
        Assert.Equal("42", vm.LevelText);
        Assert.Equal("Female", vm.GenderText);
        Assert.Equal("Current", vm.RoleText);
        Assert.Equal("Solar Power", vm.AbilityText);
        Assert.True(vm.HasHiddenAbility);
        Assert.Equal("Adamant", vm.NatureText);
        Assert.Equal("Actual stats", vm.StatsTitle);
        var expected = PokemonStatCalculator.Calculate(Metadata(3), store.State.Active!.Profile!, PokemonNature.Adamant);
        Assert.Equal(expected.Select(s => s.Value), vm.Stats.Select(s => s.StatValue));
        Assert.Equal(["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"], vm.Stats.Select(s => s.StatName));
        Assert.Equal(["IV 1", "IV 2", "IV 3", "IV 4", "IV 5", "IV 6"], vm.Stats.Select(s => s.IvText));
        Assert.Equal("0.7 m", vm.HeightText);
        Assert.Equal("6.9 kg", vm.WeightText);
        Assert.Equal(300, vm.BaseStatTotal); // Unknown stats are excluded.
        Assert.Equal("Grass · Poison", vm.TypesText);
        Assert.Equal("Overgrow · Solar Power (Hidden)", vm.PossibleAbilitiesText);
        Assert.Equal(store.State.Active!.Profile!.Moves.Select(m => PokemonDetailViewModel.DisplayIdentifier(m.Name)), vm.KnownMoves.Select(m => m.MoveName));
        Assert.InRange(vm.KnownMoves.Count, 1, 4);
        Assert.Contains(vm.KnownMoves, m => m.MoveName == "Vine Whip" && m.MethodText == "Lv. 7");
        Assert.Contains(vm.CompleteMoves, m => m.MethodText == "Start · TM · Egg · Tutor · Special Method");
        Assert.Contains(store.State.Active.Profile.Moves, m => m.Name == "vine-whip");
        await vm.RepresentCommand.ExecuteAsync();
        Assert.Equal(3, store.State.RepresentativeSpeciesId);
        Assert.True(vm.IsRepresentative);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task NoExactIndividualShowsBaseStatsWithoutFabricatingProfile(int species)
    {
        using var store = Store(State() with { Dex = [Entry("graduate", 100, 1)] }, new Provider());
        using var vm = ViewModel(store);
        await vm.OpenAsync(species, $"#{species}", PokemonRarity.Common, false);
        Assert.Empty(vm.Individuals);
        Assert.False(vm.HasIndividual);
        Assert.Null(vm.SelectedIndividual);
        Assert.Equal("Base stats", vm.StatsTitle);
        Assert.All(vm.Stats, stat => { Assert.Null(stat.IvText); Assert.Equal(50, stat.StatValue); });
        Assert.Empty(vm.KnownMoves);
        Assert.Equal("—", vm.LevelText);
        Assert.Equal("—", vm.GenderText);
        Assert.Equal("—", vm.NatureText);
        Assert.Single(store.State.Dex);
    }

    [Fact]
    public async Task SelectionChangesAllIndividualPresentationAndShinySpriteWithoutMetadataRequest()
    {
        var provider = new Provider { Response = Metadata };
        var first = Entry("normal", 100, 2) with { Nature = PokemonNature.Adamant };
        var second = Entry("shiny", 70, 1, true) with
        {
            IsShiny = true, Nature = PokemonNature.Modest,
            Profile = Profile("shiny", 70) with { Gender = PokemonGender.Male, IVs = new(31, 30, 29, 28, 27, 26), AbilitySlot = 1, AbilityIsHidden = false },
        };
        using var store = Store(State() with { Dex = [first, second] }, provider);
        var sprites = new List<(int, bool)>();
        using var vm = ViewModel(store, (id, shiny, _) => { sprites.Add((id, shiny)); return Task.FromResult<PokemonSpriteAsset?>(Asset(shiny ? (byte)2 : (byte)1)); });
        await vm.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        Assert.True(vm.HasMultipleIndividuals);
        Assert.Equal("#1 · Lv. 100", vm.Individuals[0].Label);
        var originalStats = vm.Stats.Select(s => s.StatValue).ToArray();
        Assert.Equal("Solar Power", vm.AbilityText);
        Assert.Contains(vm.KnownMoves, m => m.MoveName == "Final Move");
        vm.SelectedIndividual = vm.Individuals[1];
        await vm.SpriteLoadTask;
        Assert.Equal("70", vm.LevelText);
        Assert.Equal("Released", vm.RoleText);
        Assert.Equal("Male", vm.GenderText);
        Assert.Equal("Modest", vm.NatureText);
        Assert.Equal("Overgrow", vm.AbilityText);
        Assert.False(vm.HasHiddenAbility);
        Assert.Equal("IV 31", vm.Stats[0].IvText);
        Assert.False(originalStats.SequenceEqual(vm.Stats.Select(s => s.StatValue)));
        Assert.DoesNotContain(vm.KnownMoves, m => m.MoveName == "Final Move");
        Assert.True(vm.IsShiny);
        Assert.Equal((byte)2, Pixel(vm.Sprite!));
        Assert.Equal([(3, false), (3, true)], sprites);
        Assert.Equal([3], provider.Requests);
        vm.SelectedIndividual = vm.Individuals[0];
        await vm.SpriteLoadTask;
        Assert.Equal("100", vm.LevelText);
        Assert.Equal((byte)1, Pixel(vm.Sprite!));
        Assert.Equal([3], provider.Requests);
    }

    [Fact]
    public async Task LoadingFailureAndRetryAreVisibleAndDoNotLoadOtherCollectionSpecies()
    {
        var pending = new TaskCompletionSource<PokemonDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Pending = new() { [3] = pending.Task } };
        using var store = Store(State() with { Dex = [Entry("saved", 100, 1)] }, provider);
        using var vm = ViewModel(store);
        var load = vm.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        Assert.True(vm.IsLoading);
        Assert.False(vm.RetryCommand.CanExecute(null));
        pending.SetException(new IOException("fixture offline"));
        await load;
        Assert.True(vm.HasError);
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasDetails);
        Assert.Equal("saved", vm.SelectedIndividual!.Individual.Profile.InstanceId);
        provider.Pending.Clear();
        await vm.RetryCommand.ExecuteAsync();
        Assert.False(vm.HasError);
        Assert.True(vm.HasDetails);
        Assert.Equal([3, 3], provider.Requests);
    }

    [Fact]
    public async Task LateAMetadataCannotOverwriteB()
    {
        var pending = new TaskCompletionSource<PokemonDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Pending = new() { [1] = pending.Task } };
        using var store = Store(State(), provider);
        using var vm = ViewModel(store);
        var a = vm.OpenAsync(1, "A", PokemonRarity.Common, false);
        await vm.OpenAsync(2, "B", PokemonRarity.Rare, false);
        pending.SetResult(Metadata(1));
        await a;
        Assert.Equal(2, vm.SpeciesId);
        Assert.Equal("B", vm.Name);
        Assert.True(vm.HasDetails);
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackOrDisposeDuringHttpCannotReopenDetail(bool dispose)
    {
        var pending = new TaskCompletionSource<PokemonDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var store = Store(State(), new Provider { Pending = new() { [1] = pending.Task } });
        using var vm = ViewModel(store);
        var load = vm.OpenAsync(1, "A", PokemonRarity.Common, false);
        if (dispose) { vm.Dispose(); vm.Dispose(); }
        else await vm.BackCommand.ExecuteAsync();
        pending.SetResult(Metadata(1));
        await load;
        Assert.True(vm.IsCollectionVisible);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasDetails);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.Sprite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededOrClosedSpriteIsCancelledAndIgnoredEvenIfLoaderIgnoresCancellation(bool close)
    {
        var pending = new TaskCompletionSource<PokemonSpriteAsset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        using var store = Store(State(), new Provider());
        using var vm = ViewModel(store, (id, _, token) =>
        {
            if (id == 1) { firstToken = token; return pending.Task; }
            return Task.FromResult<PokemonSpriteAsset?>(Asset(2));
        });
        var a = vm.OpenAsync(1, "A", PokemonRarity.Common, false);
        if (close) vm.Close();
        else await vm.OpenAsync(2, "B", PokemonRarity.Common, false);
        Assert.True(firstToken.IsCancellationRequested);
        pending.SetResult(Asset(1));
        await a;
        if (close) { Assert.Null(vm.Sprite); Assert.False(vm.IsOpen); }
        else { Assert.Equal(2, vm.SpeciesId); Assert.Equal((byte)2, Pixel(vm.Sprite!)); }
    }

    [Theory]
    [InlineData(78, 286, 300)]
    [InlineData(106, 342, 400)]
    public async Task StatScaleRoundsToNextHundredWithMinimum300(int hpBase, int hpValue, int scale)
    {
        var provider = new Provider { Response = id => Details(id) with
        {
            BaseStats = PokemonStatCalculator.Order.ToDictionary(s => s, s => s == "hp" ? hpBase : 1),
        } };
        using var store = Store(State(Mon() with { Profile = Profile("scale", 100) with { IVs = new(20, 0, 0, 0, 0, 0) } }), provider);
        using var vm = ViewModel(store);
        await vm.OpenAsync(1, "A", PokemonRarity.Common, false);
        Assert.Equal(hpValue, vm.Stats[0].StatValue);
        Assert.All(vm.Stats, s => Assert.Equal(scale, s.ScaleMaximum));
    }

    [Fact]
    public async Task GenderlessAndSpriteFailureDoNotHideProfileOrSpeciesData()
    {
        var mon = Mon() with { BaseId = 132, PathIds = [132], PlannedPathIds = [132], TotalForms = 1,
            Profile = Profile("ditto", 42) with { Gender = PokemonGender.Genderless } };
        using var store = Store(State(mon), new Provider());
        using var vm = ViewModel(store, (_, _, _) => throw new IOException("fixture sprite failure"));
        await vm.OpenAsync(132, "Ditto", PokemonRarity.Rare, false);
        Assert.Equal("Genderless", vm.GenderText);
        Assert.Equal("42", vm.LevelText);
        Assert.True(vm.HasDetails);
        Assert.False(vm.HasError);
        Assert.Null(vm.Sprite);
    }

    [Fact]
    public async Task LateNormalSpriteCannotOverwriteNewShinySelectionAndDisposeCancelsPendingSprite()
    {
        using var store = Store(State() with { Dex = [Entry("normal", 100, 2), Entry("shiny", 70, 1) with { IsShiny = true }] }, new Provider());
        var pending = new TaskCompletionSource<PokemonSpriteAsset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken normalToken = default;
        using var vm = ViewModel(store, (_, shiny, token) =>
        {
            if (!shiny) { normalToken = token; return pending.Task; }
            return Task.FromResult<PokemonSpriteAsset?>(Asset(2));
        });
        var load = vm.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        vm.SelectedIndividual = vm.Individuals[1];
        await vm.SpriteLoadTask;
        Assert.True(normalToken.IsCancellationRequested);
        pending.SetResult(Asset(1));
        await load;
        Assert.True(vm.IsShiny);
        Assert.Equal((byte)2, Pixel(vm.Sprite!));

        var afterDispose = new TaskCompletionSource<PokemonSpriteAsset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken disposeToken = default;
        using var disposed = ViewModel(store, (_, _, token) => { disposeToken = token; return afterDispose.Task; });
        var closing = disposed.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        disposed.Dispose();
        disposed.Dispose();
        Assert.True(disposeToken.IsCancellationRequested);
        afterDispose.SetResult(Asset(3));
        await closing;
        Assert.Null(disposed.Sprite);
        Assert.False(disposed.IsOpen);
    }

    [Theory]
    [InlineData(AppLanguage.Ko)]
    [InlineData(AppLanguage.En)]
    [InlineData(AppLanguage.Ja)]
    [InlineData(AppLanguage.Es)]
    [InlineData(AppLanguage.Fr)]
    [InlineData(AppLanguage.Pt)]
    [InlineData(AppLanguage.De)]
    public async Task AllSevenLanguagesTranslateDetailLabelsAndRetainSelectionWithoutRefetch(AppLanguage language)
    {
        var texts = new LocalizationService(AppLanguage.En);
        var provider = new Provider { Response = id => Metadata(id) with { Moves = [] } };
        using var store = Store(State() with { Dex = [Entry("one", 100, 2), Entry("two", 70, 1)] }, provider);
        using var vm = ViewModel(store, texts: texts);
        await vm.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        vm.SelectedIndividual = vm.Individuals[1];
        texts.Language = language;
        Assert.Equal("two", vm.SelectedIndividual!.Individual.Profile.InstanceId);
        Assert.Equal(texts.ActualStats, vm.StatsTitle);
        Assert.Equal(texts.Female, vm.GenderText);
        Assert.True(vm.HasNoKnownMoves);
        Assert.Equal([3], provider.Requests);
        Assert.All(new[] { texts.Details, texts.Back, texts.Retry, texts.LoadingPokemonDetails, texts.PokemonDetailsUnavailable,
            texts.Individual, texts.Level, texts.Gender, texts.Nature, texts.Ability, texts.HiddenAbility, texts.Hidden,
            texts.KnownMoves, texts.NoLevelMoves, texts.ActualStats, texts.BaseStats, texts.SpeciesData, texts.Types,
            texts.Height, texts.Weight, texts.BaseTotal, texts.PossibleAbilities, texts.CompleteMoveList(0),
            texts.Male, texts.Female, texts.Genderless, texts.Attack, texts.Defense, texts.SpecialAttack, texts.SpecialDefense,
            texts.Speed, texts.StartMove, texts.EggMove, texts.Tutor }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        if (language != AppLanguage.En) Assert.NotEqual("Loading Pokémon details…", texts.LoadingPokemonDetails);
    }

    [Fact]
    public async Task ClickedFinalRowPreselectsItsIndividualAndLaterUserSelectionSurvivesRefresh()
    {
        var provider = new Provider { Response = Metadata };
        using var store = Store(State(Mon(2) with { Profile = Profile("active", 42) }) with
        {
            Dex = [Entry("A", 100, 2), Entry("B", 70, 1, released: true)],
        }, provider);
        var texts = new LocalizationService(AppLanguage.En);
        var economy = new EconomyViewModel(store, _ => Task.CompletedTask, texts);
        using var detail = economy.Detail;
        var open = economy.CollectionEntries.Last(row => row.SpeciesId == 3).DetailsCommand.ExecuteAsync();
        Assert.Equal("B", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        await open;
        Assert.Equal("B", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        Assert.Equal("70", detail.LevelText);

        detail.SelectedIndividual = detail.Individuals.Single(i => i.Individual.Profile.InstanceId == "A");
        texts.Language = AppLanguage.Ko;
        Assert.Equal("A", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        detail.RefreshPresentation();
        Assert.Equal("A", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        economy.Refresh();
        Assert.Equal("A", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        Assert.Equal([3], provider.Requests);

        await economy.CollectionEntries.Single(row => row.IsCurrent).DetailsCommand.ExecuteAsync();
        Assert.Equal("active", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        Assert.True(detail.SelectedIndividual.Individual.IsCurrent);
        Assert.Equal([3], provider.Requests); // Reopening reuses the existing species metadata.
        Assert.Equal(["A", "B"], store.State.Dex.Select(entry => entry.Profile!.InstanceId));
        Assert.Equal("active", store.State.Active!.Profile!.InstanceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierEvolutionRowsDoNotForwardCurrentOrFinalProfileIdentity(bool persisted)
    {
        using var store = Store(State(Mon(2) with { Profile = Profile("active", 42) }) with
        {
            Dex = [Entry("final", 100, 1)],
        }, new Provider());
        var economy = new EconomyViewModel(store, _ => Task.CompletedTask);
        using var detail = economy.Detail;
        var rows = economy.CollectionEntries.Where(row => row.SpeciesId == 1).ToArray();
        await rows[persisted ? 1 : 0].DetailsCommand.ExecuteAsync();
        Assert.Equal(1, detail.SpeciesId);
        Assert.Empty(detail.Individuals);
        Assert.False(detail.HasIndividual);
        // Verify the navigation argument, not just the species filter's rejection of a wrong ID.
        Assert.Null(typeof(PokemonDetailViewModel).GetField("_preferredInstanceId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(detail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("other-species")]
    public async Task AbsentOrNonmatchingPreferredIdentityFallsBackToFirstCurrentSpeciesIndividual(string? preferred)
    {
        var provider = new Provider();
        using var store = Store(State() with { Dex = [Entry("A", 100, 2), Entry("B", 70, 1),
            Entry("other-species", 100, 3) with { BaseId = 4, FinalId = 4, ChainOrder = [4] }] }, provider);
        using var detail = ViewModel(store);
        await detail.OpenAsync(3, "Venusaur", PokemonRarity.Common, false, preferred);
        Assert.Equal("A", detail.SelectedIndividual!.Individual.Profile.InstanceId);
        Assert.Equal([3], provider.Requests);
    }

    [Theory]
    [InlineData(AppLanguage.Ko, "전체 기술 목록 4개")]
    [InlineData(AppLanguage.En, "Complete move list · 4")]
    [InlineData(AppLanguage.Ja, "全技一覧 · 4")]
    [InlineData(AppLanguage.Es, "Lista completa de movimientos · 4")]
    [InlineData(AppLanguage.Fr, "Liste complète des capacités · 4")]
    [InlineData(AppLanguage.Pt, "Lista completa de golpes · 4")]
    [InlineData(AppLanguage.De, "Vollständige Attackenliste · 4")]
    public async Task CompleteMoveTitleTracksCountAndLanguageWithoutMetadataRequest(AppLanguage language, string expected)
    {
        var texts = new LocalizationService(language == AppLanguage.En ? AppLanguage.Ko : AppLanguage.En);
        var provider = new Provider { Response = Metadata };
        using var store = Store(State(), provider);
        using var detail = ViewModel(store, texts: texts);
        Assert.Equal(texts.CompleteMoveList(0), detail.CompleteMovesTitle);
        await detail.OpenAsync(3, "Venusaur", PokemonRarity.Common, false);
        Assert.Equal(4, detail.CompleteMoves.Count);
        Assert.Equal(texts.CompleteMoveList(detail.CompleteMoves.Count), detail.CompleteMovesTitle);
        var priorTitle = detail.CompleteMovesTitle;
        var observedTitles = new List<string>();
        detail.PropertyChanged += (_, _) => observedTitles.Add(detail.CompleteMovesTitle);
        texts.Language = language;
        Assert.Equal(expected, detail.CompleteMovesTitle);
        Assert.NotEqual(priorTitle, detail.CompleteMovesTitle);
        Assert.Contains(expected, observedTitles);
        Assert.Equal([3], provider.Requests);
    }

    [Fact]
    public async Task CollectionDetailsBackAndRepresentativeKeepExistingCommands()
    {
        var provider = new Provider();
        using var store = Store(State() with { Dex = [Entry("final", 100, 1)] }, provider);
        var economy = new EconomyViewModel(store, _ => Task.CompletedTask);
        using var detail = economy.Detail;
        Assert.Equal(3, economy.CollectionEntries.Count);
        Assert.Empty(provider.Requests);
        await economy.CollectionEntries[0].DetailsCommand.ExecuteAsync();
        Assert.Equal(1, detail.SpeciesId);
        Assert.False(detail.HasIndividual);
        await detail.BackCommand.ExecuteAsync();
        Assert.True(detail.IsCollectionVisible);
        await economy.CollectionEntries[2].SelectRepresentativeCommand.ExecuteAsync();
        Assert.Equal(3, store.State.RepresentativeSpeciesId);
        await economy.ClearRepresentativeCommand.ExecuteAsync();
        Assert.Null(store.State.RepresentativeSpeciesId);
    }

    private static PokemonProfile Profile(string id, int level) => PokemonProfile.Generate(42, id) with
    {
        Level = level, Gender = PokemonGender.Female, IVs = new(1, 2, 3, 4, 5, 6),
        AbilitySlot = 3, AbilityName = "solar-power", AbilityIsHidden = true,
    };
    private static DexEntry Entry(string id, int level, int day, bool released = false) => new()
    {
        Id = id, BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3], Profile = Profile(id, level),
        CaughtAt = DateTimeOffset.UnixEpoch.AddDays(day), ReleasedAt = released ? DateTimeOffset.UnixEpoch.AddDays(day) : null,
    };
    private static PokemonDetails Metadata(int id) => Details(id) with
    {
        Height = 7, Weight = 69, Types = ["grass", "poison"],
        BaseStats = new Dictionary<string, int>(Details(id).BaseStats) { ["unknown"] = 999 },
        Abilities = [new("overgrow", 1, false), new("solar-power", 3, true)],
        Moves = [new("vine-whip", [new("level-up", 7)]), new("razor-leaf", [new("level-up", 13)]),
            new("final-move", [new("level-up", 100)]),
            new("all-methods", [new("level-up", 0), new("machine", 0), new("egg", 0), new("tutor", 0), new("special-method", 0)])],
    };
    private static CompanionStore Store(CompanionState state, Provider? provider = null) => new(new Api(), new Memory(state), detailProvider: provider);
    private static PokemonDetailViewModel ViewModel(CompanionStore store,
        Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>>? sprite = null, LocalizationService? texts = null) =>
        new(store, texts ?? new LocalizationService(AppLanguage.En), sprite ?? ((_, _, _) => Task.FromResult<PokemonSpriteAsset?>(null)),
            new Decoder(), (id, _) => { store.SetRepresentativeSpeciesId(id); return Task.CompletedTask; });
    private static PokemonSpriteAsset Asset(byte marker) => new(new byte[] { marker }, new Uri("https://fixture.invalid/sprite.png"), "image/png", false, false);
    private static byte Pixel(PokemonSpritePresentation presentation)
    {
        var bytes = new byte[4];
        presentation.StaticImage.CopyPixels(bytes, 4, 0);
        return bytes[0];
    }
    private sealed class Decoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation Decode(PokemonSpriteAsset asset)
        {
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { asset.Data.Span[0], 0, 0, 255 }, 4);
            image.Freeze();
            return new(image, [], false);
        }
    }
    private sealed class Provider : IPokemonDetailProvider
    {
        public ConcurrentQueue<int> Requests { get; } = new();
        public Dictionary<int, Task<PokemonDetails>> Pending { get; init; } = [];
        public Func<int, PokemonDetails> Response { get; init; } = Details;
        public Task<PokemonDetails> GetPokemonDetailsAsync(int speciesId, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(speciesId);
            return Pending.GetValueOrDefault(speciesId) ?? Task.FromResult(Response(speciesId));
        }
    }
}
