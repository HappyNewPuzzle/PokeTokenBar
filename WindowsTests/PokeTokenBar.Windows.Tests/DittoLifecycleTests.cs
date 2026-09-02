using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class DittoLifecycleTests
{
    private const string Day = "2026-09-02";
    private static readonly EvoLine DisguiseLine = Line(
        1, PokemonRarity.Common, new EvoNode(1, [new EvoNode(2, [new EvoNode(3, [])])]));
    private static readonly EvoLine DittoLine = Line(
        PokemonOdds.DittoSpeciesId,
        PokemonRarity.Rare,
        new EvoNode(PokemonOdds.DittoSpeciesId, []));

    [Fact]
    public void CanonicalConstants_MatchUpstream()
    {
        Assert.Equal(132, PokemonOdds.DittoSpeciesId);
        Assert.Equal(128, PokemonOdds.DittoDisguiseDenominator);
        Assert.Equal("132-sha", PokemonSpriteLoader.GetCacheKey(132, animated: true, shiny: true));
        Assert.Contains("/132.gif", PokemonSpriteLoader.GetSourceUri(132, animated: true, shiny: false).AbsoluteUri);
    }

    [Theory]
    [InlineData(PokemonRarity.Common, 2, 0, true)]
    [InlineData(PokemonRarity.Common, 3, 128, true)]
    [InlineData(PokemonRarity.Common, 3, 1, false)]
    [InlineData(PokemonRarity.Common, 3, 127, false)]
    [InlineData(PokemonRarity.Common, 1, 0, false)]
    [InlineData(PokemonRarity.Uncommon, 3, 0, false)]
    [InlineData(PokemonRarity.Legendary, 3, 0, false)]
    public void DisguiseRoll_UsesCommonMultiFormOneIn128Rule(
        PokemonRarity rarity,
        int totalForms,
        int roll,
        bool expected) =>
        Assert.Equal(expected, CompanionStore.DittoDisguiseHit(rarity, totalForms, roll));

    [Fact]
    public async Task EnabledHatch_HidesDittoBehindOriginalSpecies()
    {
        var store = Create(random: new FixedRandom(1, 3, 0), rollingEnabled: true);

        Assert.True(await store.HatchAsync(1));

        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(1, store.State.Active!.DittoDisguise);
        Assert.False(store.State.Active.DittoRevealed);
    }

    [Fact]
    public async Task HatchOverflow_CanRevealImmediatelyAndCarriesIntoDitto()
    {
        var state = new CompanionState
        {
            EggUsage = PokemonBalance.EggHatchThreshold + FirstThreshold() + 42,
        };
        var store = Create(
            state: state,
            random: new FixedRandom(1, 3, 0),
            rollingEnabled: true);
        var events = new List<CompanionGameEvent>();
        store.GameEventOccurred += (_, gameEvent) => events.Add(gameEvent);

        await store.HatchAsync(1);

        Assert.True(store.State.Active!.DittoRevealed);
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.Equal(42, store.State.Active.UsedAtStage);
        Assert.Equal(
            [CompanionGameEventKind.Hatch, CompanionGameEventKind.DittoReveal],
            events.Select(gameEvent => gameEvent.Kind));
        Assert.Equal(1, events[0].SpeciesId);
    }

    [Fact]
    public async Task DefaultTestConstruction_DoesNotConsumeDisguiseRoll()
    {
        var random = new FixedRandom(1, 3, 0);
        var store = Create(random: random);

        await store.HatchAsync(1);

        Assert.Null(store.State.Active!.DittoDisguise);
        Assert.Equal(4, random.CallCount);
    }

    [Fact]
    public async Task PremiumRareEgg_CannotProduceDittoDisguise()
    {
        var line = Line(1, PokemonRarity.Rare, new EvoNode(1, [new EvoNode(2, [])]));
        var state = new CompanionState
        {
            EggUsage = PokemonBalance.EggHatchThreshold,
            EggTier = PokemonRarity.Rare,
        };
        var store = Create(
            line,
            state: state,
            random: new FixedRandom(1, 3, 0),
            rollingEnabled: true);

        await store.HatchAsync(1);

        Assert.Equal(PokemonRarity.Rare, store.State.Active!.Rarity);
        Assert.Null(store.State.Active.DittoDisguise);
    }

    [Fact]
    public async Task DisguisedShiny_IsHiddenButTraitsRemainStored()
    {
        var state = new CompanionState
        {
            Inventory = new Dictionary<string, int> { [CompanionItemKind.ShinyCharm.Key()] = 1 },
        };
        var random = new FixedRandom(0, 7, 0);
        var store = Create(state: state, random: random, rollingEnabled: true);

        await store.HatchAsync(1);

        Assert.True(store.State.Active!.IsShiny);
        Assert.Equal((PokemonNature)7, store.State.Active.Nature);
        Assert.False(store.CurrentIsShiny);
        Assert.False(store.RepresentativeSubject.IsShiny);
        Assert.Contains(CompanionEconomyRules.ShinyCharmDenominator, random.Maximums);
        Assert.Contains(PokemonOdds.DittoDisguiseDenominator, random.Maximums);
    }

    [Fact]
    public async Task BelowFirstThreshold_RemainsDisguised()
    {
        var threshold = FirstThreshold();
        var store = Create(state: DisguiseState(threshold - 1));

        await store.LoadCurrentLineAsync();

        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.False(store.State.Active!.DittoRevealed);
    }

    [Fact]
    public async Task FirstEvolutionThreshold_RevealsInsteadOfEvolving()
    {
        var store = Create(state: DisguiseState(FirstThreshold()));

        await store.LoadCurrentLineAsync();

        Assert.Equal(PokemonOdds.DittoSpeciesId, store.CurrentSpeciesId);
        Assert.NotEqual(2, store.CurrentSpeciesId);
        Assert.True(store.State.Active!.DittoRevealed);
        Assert.Equal([PokemonOdds.DittoSpeciesId], store.State.Active.PathIds);
        Assert.Equal([PokemonOdds.DittoSpeciesId], store.State.Active.PlannedPathIds);
        Assert.Equal(PokemonRarity.Rare, store.State.Active.Rarity);
        Assert.Equal(1, store.State.Active.TotalForms);
    }

    [Fact]
    public async Task Reveal_CarriesFirstEvolutionOverflow()
    {
        var store = Create(state: DisguiseState(FirstThreshold() + 42));

        await store.LoadCurrentLineAsync();

        Assert.Equal(42, store.State.Active!.UsedAtStage);
    }

    [Fact]
    public async Task RevealFailure_KeepsDisguiseAndRetries()
    {
        var api = Api();
        api.FailDitto = true;
        var store = Create(api: api, state: DisguiseState(FirstThreshold()));

        await store.LoadCurrentLineAsync();
        Assert.False(store.State.Active!.DittoRevealed);
        api.FailDitto = false;
        await Update(store, FirstThreshold());

        Assert.True(store.State.Active!.DittoRevealed);
        Assert.Equal(2, api.DittoCalls);
    }

    [Fact]
    public async Task DelayedReveal_DoesNotReplaceNewerActiveState()
    {
        var api = Api();
        api.DittoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.DittoRelease = new TaskCompletionSource<EvoLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Create(api: api, state: DisguiseState(FirstThreshold()));

        var reveal = store.LoadCurrentLineAsync();
        await api.DittoStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        store.Reset();
        api.DittoRelease.SetResult(DittoLine);
        Assert.True(await reveal);

        Assert.Null(store.State.Active);
        Assert.Equal(CompanionStateKind.Egg, store.DisplayState);
    }

    [Fact]
    public async Task PrunedLeaf_RevealsBeforeFalseGraduation()
    {
        var pruned = Line(206, PokemonRarity.Common,
            new EvoNode(206, [new EvoNode(982, [])]));
        var state = DisguiseState(750_000_000, baseId: 206, planned: [206, 982]);
        var store = Create(pruned, state: state);

        await store.LoadCurrentLineAsync();

        Assert.Equal(PokemonOdds.DittoSpeciesId, store.CurrentSpeciesId);
        Assert.Empty(store.State.Dex);
        Assert.DoesNotContain("206:206", store.State.CollectedFinals);
    }

    [Fact]
    public async Task Reveal_UnmasksShinyAndPreservesNature()
    {
        var store = Create(state: DisguiseState(
            FirstThreshold(), shiny: true, nature: PokemonNature.Timid));

        await store.LoadCurrentLineAsync();

        Assert.True(store.CurrentIsShiny);
        Assert.True(store.RepresentativeSubject.IsShiny);
        Assert.Equal(PokemonNature.Timid, store.State.Active!.Nature);
    }

    [Fact]
    public async Task Reveal_ClearsRepresentativeOwnedOnlyByDisguise()
    {
        var state = DisguiseState(FirstThreshold()) with { RepresentativeSpeciesId = 1 };
        var store = Create(state: state);

        await store.LoadCurrentLineAsync();

        Assert.Null(store.State.RepresentativeSpeciesId);
        Assert.Equal(PokemonOdds.DittoSpeciesId, store.RepresentativeSubject.SpeciesId);
    }

    [Fact]
    public async Task Reveal_KeepsRepresentativeWhenDexAlsoOwnsDisguiseSpecies()
    {
        var state = DisguiseState(FirstThreshold()) with
        {
            RepresentativeSpeciesId = 1,
            Dex = [new DexEntry { BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3] }],
        };
        var store = Create(state: state);

        await store.LoadCurrentLineAsync();

        Assert.Equal(1, store.State.RepresentativeSpeciesId);
        Assert.Equal(1, store.RepresentativeSubject.SpeciesId);
    }

    [Fact]
    public async Task RareCandy_CanTriggerRevealThroughSharedProgressionPath()
    {
        var state = DisguiseState(FirstThreshold() - CompanionEconomyRules.RareCandyExperience) with
        {
            Inventory = new Dictionary<string, int> { [CompanionItemKind.RareCandy.Key()] = 1 },
        };
        var store = Create(state: state);
        await store.LoadCurrentLineAsync();

        var outcome = await store.UseItemAsync(CompanionItemKind.RareCandy);

        Assert.Equal(ItemUseResult.Progressed, outcome.Result);
        Assert.True(store.State.Active!.DittoRevealed);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task MintNature_SurvivesLaterReveal()
    {
        var state = DisguiseState(0, nature: PokemonNature.Hardy) with
        {
            Inventory = new Dictionary<string, int> { [CompanionItemKind.Mint.Key()] = 1 },
        };
        var store = Create(state: state, random: new FixedRandom(3));
        await store.LoadCurrentLineAsync();
        await store.UseItemAsync(CompanionItemKind.Mint);
        var minted = store.State.Active!.Nature;

        await Update(store, FirstThreshold());

        Assert.NotEqual(PokemonNature.Hardy, minted);
        Assert.Equal(minted, store.State.Active!.Nature);
    }

    [Fact]
    public async Task RevealedDitto_UsesRareSingleFormGraduationThreshold()
    {
        var threshold = PokemonBalance.GraduationTotal(PokemonRarity.Rare);
        var store = Create(state: RevealedState(threshold - 1));

        await Update(store, 1);

        Assert.Null(store.State.Active);
        Assert.Equal(PokemonOdds.DittoSpeciesId, Assert.Single(store.State.Dex).FinalId);
    }

    [Fact]
    public async Task Graduation_RecordsCanonicalDexKeyAndStartsZeroEgg()
    {
        var threshold = PokemonBalance.GraduationTotal(PokemonRarity.Rare);
        var store = Create(state: RevealedState(threshold - 1, shiny: true));

        await Update(store, 1000);

        var entry = Assert.Single(store.State.Dex);
        Assert.Equal([PokemonOdds.DittoSpeciesId], entry.ChainOrder);
        Assert.True(entry.IsShiny);
        Assert.Contains("132:132", store.State.CollectedFinals);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task Graduation_PreservesDittoRepresentativeAndDiscardsOverflow()
    {
        var threshold = PokemonBalance.GraduationTotal(PokemonRarity.Rare);
        var state = RevealedState(threshold - 1) with
        {
            RepresentativeSpeciesId = PokemonOdds.DittoSpeciesId,
        };
        var store = Create(state: state);

        await Update(store, 10_000);

        Assert.Equal(PokemonOdds.DittoSpeciesId, store.State.RepresentativeSpeciesId);
        Assert.Equal(PokemonOdds.DittoSpeciesId, store.RepresentativeSubject.SpeciesId);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task HugeDisguiseOverflow_RevealsThenGraduatesAndDoesNotFeedEgg()
    {
        var used = FirstThreshold() + PokemonBalance.GraduationTotal(PokemonRarity.Rare) + 99;
        var store = Create(state: DisguiseState(used));

        await store.LoadCurrentLineAsync();

        Assert.Null(store.State.Active);
        Assert.Equal(132, Assert.Single(store.State.Dex).FinalId);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task RepeatedDittoGraduations_KeepIndependentCatchEntries()
    {
        var state = RevealedState(PokemonBalance.GraduationTotal(PokemonRarity.Rare) - 1) with
        {
            Dex =
            [
                new DexEntry
                {
                    BaseId = 132, FinalId = 132, ChainOrder = [132],
                    Rarity = PokemonRarity.Rare, IsShiny = false,
                },
                new DexEntry
                {
                    BaseId = 132, FinalId = 132, ChainOrder = [132],
                    Rarity = PokemonRarity.Rare, IsShiny = true,
                },
            ],
            CollectedFinals = new HashSet<string> { "132:132" },
        };
        var store = Create(state: state);

        await Update(store, 1);

        Assert.Equal(3, store.State.Dex.Count);
        Assert.Contains(store.State.Dex, entry => entry.IsShiny);
        Assert.Contains(store.State.Dex, entry => !entry.IsShiny);
        Assert.Single(store.State.CollectedFinals);
    }

    [Fact]
    public void InvalidDittoState_NormalizesSafelyAndUnknownNameFallsBack()
    {
        var state = new CompanionState
        {
            Active = new MonState
            {
                BaseId = 999,
                PathIds = [],
                DittoDisguise = 999,
            },
            RepresentativeSpeciesId = PokemonOdds.DittoSpeciesId,
        };
        var store = Create(state: state);
        var unknown = new EvoLine(
            999,
            new EvoNode(999, []),
            PokemonRarity.Common,
            new Dictionary<int, IReadOnlyDictionary<string, string>>());

        Assert.Null(store.State.Active);
        Assert.Null(store.State.RepresentativeSpeciesId);
        Assert.Equal("#999", unknown.LocalizedName(999, AppLanguage.En));
    }

    [Fact]
    public void JsonPersistence_RoundTripsDisguiseAndRevealFields()
    {
        using var temp = new TempDirectory();
        var persistence = new JsonCompanionPersistence(temp.File("state.json"));
        persistence.Save(DisguiseState(42, shiny: true));

        var loaded = persistence.Load()!;

        Assert.Equal(1, loaded.Active!.DittoDisguise);
        Assert.False(loaded.Active.DittoRevealed);
        Assert.True(loaded.Active.IsShiny);
    }

    [Fact]
    public void LegacyJsonWithoutDittoFields_LoadsAsOrdinaryPokemon()
    {
        using var temp = new TempDirectory();
        var path = temp.File("state.json");
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(path, """
            {"active":{"baseID":1,"pathIDs":[1],"plannedPathIDs":[1,2],"stageIndex":0,
            "usedAtStage":0,"rarity":"common","totalForms":2}}
            """);

        var active = new JsonCompanionPersistence(path).Load()!.Active!;

        Assert.Null(active.DittoDisguise);
        Assert.False(active.DittoRevealed);
    }

    [Fact]
    public async Task Restart_LoadsThresholdStateAndCompletesReveal()
    {
        using var temp = new TempDirectory();
        var persistence = new JsonCompanionPersistence(temp.File("state.json"));
        persistence.Save(DisguiseState(FirstThreshold() + 9));

        var restarted = Create(persistence: persistence);
        await restarted.LoadCurrentLineAsync();

        Assert.True(restarted.State.Active!.DittoRevealed);
        Assert.Equal(9, restarted.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task DuplicateUsageSnapshot_AfterRestartDoesNotProgressTwice()
    {
        using var temp = new TempDirectory();
        var persistence = new JsonCompanionPersistence(temp.File("state.json"));
        var state = DisguiseState(FirstThreshold() - 5) with
        {
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 100 },
        };
        persistence.Save(state);
        var first = Create(persistence: persistence);
        await UpdateSnapshot(first, new Dictionary<string, long> { ["codex"] = 105 });
        var afterReveal = first.State.Active!.UsedAtStage;

        var restarted = Create(persistence: persistence);
        await UpdateSnapshot(restarted, new Dictionary<string, long> { ["codex"] = 105 });

        Assert.True(restarted.State.Active!.DittoRevealed);
        Assert.Equal(afterReveal, restarted.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task ProviderNeutralLedger_SumsDeltasBeforeReveal()
    {
        var state = DisguiseState(FirstThreshold() - 3) with
        {
            ClaimedTodayTokensByProvider = new Dictionary<string, long>
            {
                ["codex"] = 10, ["claude_code"] = 20,
            },
        };
        var store = Create(state: state);

        await UpdateSnapshot(store, new Dictionary<string, long>
        {
            ["codex"] = 11, ["claude_code"] = 22,
        });

        Assert.True(store.State.Active!.DittoRevealed);
        Assert.Equal(0, store.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task Reveal_PublishesDistinctGameEvent()
    {
        var store = Create(state: DisguiseState(FirstThreshold(), shiny: true));
        CompanionGameEvent? observed = null;
        store.GameEventOccurred += (_, gameEvent) => observed = gameEvent;

        await store.LoadCurrentLineAsync();

        Assert.Equal(CompanionGameEventKind.DittoReveal, observed!.Kind);
        Assert.Equal(1, observed.PreviousSpeciesId);
        Assert.Equal(132, observed.SpeciesId);
        Assert.True(observed.IsShiny);
    }

    [Theory]
    [InlineData(AppLanguage.Ko)]
    [InlineData(AppLanguage.En)]
    [InlineData(AppLanguage.Ja)]
    [InlineData(AppLanguage.Es)]
    [InlineData(AppLanguage.Fr)]
    [InlineData(AppLanguage.Pt)]
    [InlineData(AppLanguage.De)]
    public void RevealNotifications_AreLocalizedForEverySupportedLanguage(AppLanguage language)
    {
        var text = new LocalizationService(language);

        Assert.False(string.IsNullOrWhiteSpace(text.DittoRevealTitle));
        Assert.False(string.IsNullOrWhiteSpace(text.ShinyDittoRevealTitle));
        Assert.Contains("#1", text.DittoRevealBody(1));
    }

    [Fact]
    public async Task ViewModel_ChangesNameSpriteAndShinyOnlyAfterReveal()
    {
        var store = Create(state: DisguiseState(FirstThreshold() - 1, shiny: true));
        var requests = new List<(int Id, bool Shiny)>();
        using var viewModel = new CompanionViewModel(
            store,
            (id, shiny, _) =>
            {
                requests.Add((id, shiny));
                return Task.FromResult<PokemonSpriteAsset?>(null);
            },
            new NullDecoder());
        using var floating = new FloatingPetViewModel(viewModel);
        await viewModel.InitializeAsync();
        Assert.Equal(1, viewModel.ActivePokemonId);
        Assert.False(viewModel.CurrentIsShiny);
        Assert.Equal(1, floating.PokemonId);
        Assert.False(floating.IsShiny);

        await Update(store, 1);
        await viewModel.RefreshAsync();

        Assert.Equal(132, viewModel.ActivePokemonId);
        Assert.Equal("Ditto", viewModel.DisplayName);
        Assert.True(viewModel.CurrentIsShiny);
        Assert.Contains((132, true), requests);
        Assert.Equal(132, floating.PokemonId);
        Assert.True(floating.IsShiny);
    }

    [Fact]
    public async Task Collection_HidesShinyDisguiseThenShowsRevealedDitto()
    {
        var store = Create(state: DisguiseState(FirstThreshold() - 1, shiny: true));
        var economy = new EconomyViewModel(
            store,
            _ => Task.CompletedTask,
            new LocalizationService(AppLanguage.En));
        var disguised = Assert.Single(economy.CollectionEntries);
        Assert.Equal(1, disguised.SpeciesId);
        Assert.False(disguised.IsShiny);

        await Update(store, 1);
        economy.Refresh();

        var revealed = Assert.Single(economy.CollectionEntries);
        Assert.Equal(132, revealed.SpeciesId);
        Assert.True(revealed.IsShiny);
    }

    private static long FirstThreshold() =>
        PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);

    private static CompanionState DisguiseState(
        long usedAtStage,
        int baseId = 1,
        IReadOnlyList<int>? planned = null,
        bool shiny = false,
        PokemonNature nature = PokemonNature.Hardy) =>
        new()
        {
            InstallBaselineSet = true,
            LastDate = Day,
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 0 },
            Active = new MonState
            {
                BaseId = baseId,
                PathIds = [baseId],
                PlannedPathIds = planned ?? [baseId, 2, 3],
                StageIndex = 0,
                UsedAtStage = usedAtStage,
                Rarity = PokemonRarity.Common,
                TotalForms = planned?.Count ?? 3,
                IsShiny = shiny,
                Nature = nature,
                DittoDisguise = baseId,
            },
        };

    private static CompanionState RevealedState(long usedAtStage, bool shiny = false) =>
        new()
        {
            InstallBaselineSet = true,
            LastDate = Day,
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 0 },
            Active = new MonState
            {
                BaseId = 132,
                PathIds = [132],
                PlannedPathIds = [132],
                StageIndex = 0,
                UsedAtStage = usedAtStage,
                Rarity = PokemonRarity.Rare,
                TotalForms = 1,
                IsShiny = shiny,
                Nature = PokemonNature.Timid,
                DittoDisguise = 1,
                DittoRevealed = true,
            },
        };

    private static CompanionStore Create(
        EvoLine? disguiseLine = null,
        TestApi? api = null,
        CompanionState? state = null,
        ICompanionPersistence? persistence = null,
        Random? random = null,
        bool rollingEnabled = false)
    {
        api ??= Api(disguiseLine ?? DisguiseLine);
        persistence ??= new MemoryPersistence(state);
        return new CompanionStore(api, persistence, random, dittoDisguiseRollingEnabled: rollingEnabled);
    }

    private static TestApi Api(EvoLine? disguiseLine = null) =>
        new(disguiseLine ?? DisguiseLine, DittoLine);

    private static EvoLine Line(int baseId, PokemonRarity rarity, EvoNode tree)
    {
        var ids = AllIds(tree).ToArray();
        var names = ids.ToDictionary(
            id => id,
            id => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["en"] = id == 132 ? "Ditto" : $"#{id}",
                ["de"] = id == 132 ? "Ditto" : $"#{id}",
            });
        return new EvoLine(baseId, tree, rarity, names);
    }

    private static IEnumerable<int> AllIds(EvoNode node)
    {
        yield return node.SpeciesId;
        foreach (var child in node.Children)
        foreach (var id in AllIds(child))
            yield return id;
    }

    private static Task Update(CompanionStore store, long delta) =>
        UpdateSnapshot(store, new Dictionary<string, long> { ["codex"] = delta });

    private static Task UpdateSnapshot(
        CompanionStore store,
        IReadOnlyDictionary<string, long> snapshot) =>
        store.UpdateUsageAsync(snapshot, Day, hasUsageData: true);

    private sealed class TestApi(EvoLine disguiseLine, EvoLine dittoLine) : IPokeApiClient
    {
        public bool FailDitto { get; set; }
        public int DittoCalls { get; private set; }
        public TaskCompletionSource? DittoStarted { get; set; }
        public TaskCompletionSource<EvoLine>? DittoRelease { get; set; }

        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (baseSpeciesId == PokemonOdds.DittoSpeciesId)
            {
                DittoCalls++;
                DittoStarted?.TrySetResult();
                if (DittoRelease is not null)
                {
                    return DittoRelease.Task.WaitAsync(cancellationToken);
                }

                return FailDitto
                    ? Task.FromException<EvoLine>(new HttpRequestException("offline"))
                    : Task.FromResult(dittoLine);
            }

            return Task.FromResult(disguiseLine);
        }

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new BaseSpecies(disguiseLine.BaseId, 255)]);

        public Task<BaseSpecies?> GetBaseSpeciesAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(id, 255));
    }

    private sealed class MemoryPersistence(CompanionState? state = null) : ICompanionPersistence
    {
        private CompanionState? _state = state;
        public CompanionState? Load() => _state;
        public void Save(CompanionState state) => _state = state;
        public void Delete() => _state = null;
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private readonly Queue<int> _values = new(values);
        public int CallCount { get; private set; }
        public List<int> Maximums { get; } = [];

        public override int Next(int maxValue)
        {
            CallCount++;
            Maximums.Add(maxValue);
            var value = _values.Count == 0 ? 1 : _values.Dequeue();
            return Math.Clamp(value, 0, maxValue - 1);
        }
    }

    private sealed class NullDecoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation? Decode(PokemonSpriteAsset asset) => null;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBar-Ditto-{Guid.NewGuid():N}");
        }

        public string Path { get; }
        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
