using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CompanionEconomyTests
{
    private static readonly EvoLine Linear = new(
        1,
        new EvoNode(1, [new EvoNode(2, [new EvoNode(3, [])])]),
        PokemonRarity.Common,
        new Dictionary<int, IReadOnlyDictionary<string, string>>
        {
            [1] = new Dictionary<string, string> { ["en"] = "One" },
            [2] = new Dictionary<string, string> { ["en"] = "Two" },
            [3] = new Dictionary<string, string> { ["en"] = "Three" },
        });

    public static TheoryData<string, long> CatalogPrices => new()
    {
        { "mint", 100_000_000 },
        { "rareCandy", 500_000_000 },
        { "egg.basic", 1_000_000_000 },
        { "egg.uncommon", 2_500_000_000 },
        { "shinyCharm", 3_000_000_000 },
        { "egg.rare", 4_000_000_000 },
    };

    public static TheoryData<CompanionItemKind> ConsumableItems => new()
    {
        CompanionItemKind.Mint,
        CompanionItemKind.RareCandy,
    };

    public static TheoryData<CompanionItemKind> StoreItems => new()
    {
        CompanionItemKind.Mint,
        CompanionItemKind.RareCandy,
        CompanionItemKind.ShinyCharm,
    };

    [Fact]
    public void InitialBalance_EqualsUnspentUsage()
    {
        var store = Create(State(used: 900));
        Assert.Equal(900, store.AvailableTokens);
    }

    [Theory]
    [InlineData(1_000, 400, 600)]
    [InlineData(100, 500, 0)]
    public void Balance_SubtractsSpentAndNeverGoesNegative(long used, long spent, long expected)
    {
        Assert.Equal(expected, Create(State(used, spent)).AvailableTokens);
    }

    [Theory]
    [MemberData(nameof(CatalogPrices))]
    public void Catalog_UsesUpstreamPrices(string id, long price)
    {
        var store = Create(ActiveState(used: 10_000_000_000));
        Assert.Equal(price, Assert.Single(store.ShopProducts, product => product.Id == id).Price);
    }

    [Fact]
    public void Catalog_IsPriceOrdered()
    {
        var products = Create(ActiveState(used: 10_000_000_000)).ShopProducts;
        Assert.Equal(products.Select(product => product.Price).Order(), products.Select(product => product.Price));
    }

    [Theory]
    [MemberData(nameof(StoreItems))]
    public async Task Purchase_ItemDebitsAndAddsInventory(CompanionItemKind kind)
    {
        var store = Create(State(used: 5_000_000_000));
        var result = await store.PurchaseAsync(kind.Key());
        Assert.Equal(PurchaseResult.Success, result);
        Assert.Equal(1, store.ItemCount(kind));
        Assert.Equal(kind.Price(), store.State.SpentTokens);
    }

    [Theory]
    [MemberData(nameof(StoreItems))]
    public async Task Purchase_InsufficientFundsIsNoOp(CompanionItemKind kind)
    {
        var store = Create(State(used: kind.Price() - 1));
        Assert.Equal(PurchaseResult.InsufficientFunds, await store.PurchaseAsync(kind.Key()));
        Assert.Equal(0, store.ItemCount(kind));
        Assert.Equal(0, store.State.SpentTokens);
    }

    [Fact]
    public async Task Purchase_PassiveItemCannotBeBoughtTwice()
    {
        var store = Create(State(used: 10_000_000_000));
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("shinyCharm"));
        Assert.Equal(PurchaseResult.AlreadyOwned, await store.PurchaseAsync("shinyCharm"));
        Assert.Equal(1, store.ItemCount(CompanionItemKind.ShinyCharm));
    }

    [Fact]
    public async Task Purchase_PersistenceFailureRollsBackBothSides()
    {
        var persistence = new MemoryPersistence(State(used: 1_000_000_000)) { FailNextSave = true };
        var store = Create(persistence: persistence);
        Assert.Equal(PurchaseResult.PersistenceFailed, await store.PurchaseAsync("rareCandy"));
        Assert.Equal(0, store.State.SpentTokens);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task Purchase_ConcurrentCommandsCannotOverspend()
    {
        var store = Create(State(used: CompanionEconomyRules.RareCandyPrice));
        var results = await Task.WhenAll(
            store.PurchaseAsync("rareCandy"),
            store.PurchaseAsync("rareCandy"));
        Assert.Single(results, result => result == PurchaseResult.Success);
        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task Purchase_PersistsAcrossRestart()
    {
        var persistence = new MemoryPersistence(State(used: 1_000_000_000));
        Assert.Equal(PurchaseResult.Success, await Create(persistence: persistence).PurchaseAsync("rareCandy"));
        var restarted = Create(persistence: persistence);
        Assert.Equal(1, restarted.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(500_000_000, restarted.AvailableTokens);
    }

    [Fact]
    public void LegacyState_MissingInventoryDefaultsEmpty()
    {
        var store = Create(State(used: 1));
        Assert.Empty(store.OwnedItems);
    }

    [Fact]
    public void Bag_UsesCanonicalItemOrder()
    {
        var state = State() with { Inventory = Inventory(mint: 1, candy: 2, charm: 1) };
        Assert.Equal(
            [CompanionItemKind.Mint, CompanionItemKind.RareCandy, CompanionItemKind.ShinyCharm],
            Create(state).OwnedItems.Select(item => item.Kind));
    }

    [Theory]
    [MemberData(nameof(ConsumableItems))]
    public async Task Use_ConsumableOnEggKeepsInventory(CompanionItemKind kind)
    {
        var state = State() with { Inventory = One(kind) };
        var store = Create(state);
        Assert.Equal(ItemUseResult.Unavailable, (await store.UseItemAsync(kind)).Result);
        Assert.Equal(1, store.ItemCount(kind));
    }

    [Fact]
    public async Task Mint_ChangesToDifferentNatureAndConsumesOne()
    {
        var store = Create(ActiveState(inventory: Inventory(mint: 1)), random: new Random(3));
        var outcome = await store.UseItemAsync(CompanionItemKind.Mint);
        Assert.Equal(ItemUseResult.NatureChanged, outcome.Result);
        Assert.NotEqual(PokemonNature.Adamant, store.State.Active?.Nature);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.Mint));
    }

    [Fact]
    public async Task Mint_DoesNotChangeIdentityGrowthOrShiny()
    {
        var state = ActiveState(inventory: Inventory(mint: 1), usedAtStage: 42) with
        {
            Active = ActiveState().Active! with { IsShiny = true, UsedAtStage = 42 },
        };
        var store = Create(state);
        await store.UseItemAsync(CompanionItemKind.Mint);
        Assert.Equal(1, store.State.Active?.CurrentId);
        Assert.Equal(42, store.State.Active?.UsedAtStage);
        Assert.True(store.State.Active?.IsShiny);
    }

    [Fact]
    public async Task Mint_PersistsAcrossRestart()
    {
        var persistence = new MemoryPersistence(ActiveState(inventory: Inventory(mint: 2)));
        var first = Create(persistence: persistence);
        var nature = (await first.UseItemAsync(CompanionItemKind.Mint)).Nature;
        var restarted = Create(persistence: persistence);
        Assert.Equal(nature, restarted.State.Active?.Nature);
        Assert.Equal(1, restarted.ItemCount(CompanionItemKind.Mint));
    }

    [Fact]
    public async Task RareCandy_ProgressesWithoutChangingUsageCurrency()
    {
        var store = await Loaded(ActiveState(used: 1_000, inventory: Inventory(candy: 1)));
        Assert.Equal(ItemUseResult.Progressed, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(100_000_000, store.State.Active?.UsedAtStage);
        Assert.Equal(1_000, store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task RareCandy_CrossesEvolutionThresholdWithOverflow()
    {
        var store = await Loaded(ActiveState(inventory: Inventory(candy: 1), usedAtStage: 100_000_000));
        Assert.Equal(ItemUseResult.Evolved, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(2, store.State.Active?.CurrentId);
        Assert.Equal(75_000_000, store.State.Active?.UsedAtStage);
    }

    [Fact]
    public async Task RareCandy_FinalStageCanGraduate()
    {
        var active = ActiveState(inventory: Inventory(candy: 1)).Active! with
        {
            PathIds = [1, 2, 3], PlannedPathIds = [1, 2, 3], StageIndex = 2, UsedAtStage = 300_000_000,
        };
        var store = await Loaded(ActiveState(inventory: Inventory(candy: 1)) with { Active = active });
        Assert.Equal(ItemUseResult.Graduated, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Null(store.State.Active);
        Assert.Single(store.State.Dex);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task RareCandy_UnloadedLineKeepsItem()
    {
        var store = Create(ActiveState(inventory: Inventory(candy: 1)), api: new ThrowingApi());
        Assert.Equal(ItemUseResult.Unavailable, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task RareCandy_PersistenceFailureRollsBackProgressionAndItem()
    {
        var persistence = new MemoryPersistence(ActiveState(inventory: Inventory(candy: 1)));
        var store = await Loaded(persistence: persistence);
        persistence.FailNextSave = true;
        Assert.Equal(ItemUseResult.PersistenceFailed, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(0, store.State.Active?.UsedAtStage);
        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task RareCandy_UsePersistsAcrossRestart()
    {
        var persistence = new MemoryPersistence(ActiveState(inventory: Inventory(candy: 1)));
        var store = await Loaded(persistence: persistence);
        await store.UseItemAsync(CompanionItemKind.RareCandy);
        var restarted = Create(persistence: persistence);
        Assert.Equal(100_000_000, restarted.State.Active?.UsedAtStage);
        Assert.Equal(0, restarted.ItemCount(CompanionItemKind.RareCandy));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(48, false, false)]
    [InlineData(48, true, true)]
    [InlineData(64, false, true)]
    [InlineData(64, true, false)]
    public void ShinyRoll_Uses64Or48Denominator(int roll, bool charm, bool expected)
    {
        Assert.Equal(expected, CompanionEconomyRules.RollsShiny(roll, charm));
    }

    [Fact]
    public async Task ShinyCharm_IsOneTimePersistentPurchase()
    {
        var persistence = new MemoryPersistence(State(used: 6_000_000_000));
        var store = Create(persistence: persistence);
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("shinyCharm"));
        Assert.Equal(PurchaseResult.AlreadyOwned, await store.PurchaseAsync("shinyCharm"));
        Assert.True(Create(persistence: persistence).OwnsShinyCharm);
    }

    [Theory]
    [InlineData(LimitWindowClass.Session, 1)]
    [InlineData(LimitWindowClass.Weekly, 5)]
    public void CandyEvaluation_UsesWindowClassGrant(LimitWindowClass kind, int expected)
    {
        var ledger = new Dictionary<string, int>();
        var grant = Assert.Single(CompanionStore.EvaluateCandyGrants(
            [new CandyWindow("window", "Window", kind, 100)], ledger));
        Assert.Equal(expected, grant.Count);
    }

    [Fact]
    public void CandyEvaluation_RearmsBelow100()
    {
        var ledger = new Dictionary<string, int> { ["window"] = 1 };
        Assert.Empty(CompanionStore.EvaluateCandyGrants(
            [new CandyWindow("window", "Window", LimitWindowClass.Session, 99.9)], ledger));
        Assert.DoesNotContain("window", ledger.Keys);
    }

    [Fact]
    public async Task CandyGrant_FirstReadyObservationSeedsWithoutReward()
    {
        var store = Create();
        Assert.Equal(0, await store.GrantCandiesAsync([Window("one", 100)], true));
        Assert.True(store.State.CandyFeatureSeeded);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task CandyGrant_SameWindowNeverDuplicates()
    {
        var store = Create(State() with { CandyFeatureSeeded = true });
        Assert.Equal(1, await store.GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(0, await store.GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task CandyGrant_PersistsLedgerAcrossRestart()
    {
        var persistence = new MemoryPersistence(State() with { CandyFeatureSeeded = true });
        Assert.Equal(1, await Create(persistence: persistence).GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(0, await Create(persistence: persistence).GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(1, persistence.State!.Inventory["rareCandy"]);
    }

    [Fact]
    public async Task CandyGrant_FailedPersistenceCanRetryWithoutDoubleReward()
    {
        var persistence = new MemoryPersistence(State() with { CandyFeatureSeeded = true }) { FailNextSave = true };
        var store = Create(persistence: persistence);
        Assert.Equal(0, await store.GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(1, await store.GrantCandiesAsync([Window("one", 100)], true));
        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task CandyGrant_WaitsUntilLimitsAreReady()
    {
        var store = Create();
        Assert.Equal(0, await store.GrantCandiesAsync([Window("one", 100)], false));
        Assert.False(store.State.CandyFeatureSeeded);
    }

    [Theory]
    [InlineData(null, 1_000_000_000)]
    [InlineData(PokemonRarity.Uncommon, 2_500_000_000)]
    [InlineData(PokemonRarity.Rare, 4_000_000_000)]
    public void EggPrice_FollowsGraduationRatio(PokemonRarity? rarity, long expected)
    {
        Assert.Equal(expected, CompanionEconomyRules.EggPrice(rarity));
    }

    [Fact]
    public async Task PremiumEgg_PurchaseDiscardsActiveWithoutDexAndRecordsGuarantee()
    {
        var store = Create(ActiveState(used: 5_000_000_000, usedAtStage: 42));
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("egg.rare"));
        Assert.Null(store.State.Active);
        Assert.Empty(store.State.Dex);
        Assert.Equal(PokemonRarity.Rare, store.State.EggTier);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task PremiumEgg_CannotBeBoughtWhileIncubating()
    {
        Assert.Equal(PurchaseResult.NotAllowed,
            await Create(State(used: 5_000_000_000)).PurchaseAsync("egg.rare"));
    }

    [Fact]
    public async Task PremiumEgg_UsesTierSpecificFunds()
    {
        var store = Create(ActiveState(used: 3_000_000_000));
        Assert.Equal(PurchaseResult.InsufficientFunds, await store.PurchaseAsync("egg.rare"));
        Assert.NotNull(store.State.Active);
    }

    [Fact]
    public async Task PremiumEgg_RejectsUnsoldTier()
    {
        Assert.Equal(PurchaseResult.InvalidProduct,
            await Create(ActiveState(used: 10_000_000_000)).PurchaseAsync("egg.legendary"));
    }

    [Fact]
    public async Task PremiumEgg_PersistenceFailureKeepsActiveAndFunds()
    {
        var persistence = new MemoryPersistence(ActiveState(used: 5_000_000_000)) { FailNextSave = true };
        var store = Create(persistence: persistence);
        Assert.Equal(PurchaseResult.PersistenceFailed, await store.PurchaseAsync("egg.rare"));
        Assert.NotNull(store.State.Active);
        Assert.Equal(0, store.State.SpentTokens);
    }

    [Fact]
    public async Task PremiumEgg_GuaranteePersistsAcrossRestart()
    {
        var persistence = new MemoryPersistence(ActiveState(used: 5_000_000_000));
        await Create(persistence: persistence).PurchaseAsync("egg.uncommon");
        Assert.Equal(PokemonRarity.Uncommon, Create(persistence: persistence).State.EggTier);
    }

    [Fact]
    public async Task PremiumEgg_HatchesOnlyEligiblePoolAndConsumesGuarantee()
    {
        var state = State() with
        {
            InstallBaselineSet = true,
            EggUsage = PokemonBalance.EggHatchThreshold,
            EggTier = PokemonRarity.Rare,
        };
        var store = Create(state, api: new EconomyApi());
        Assert.True(await store.HatchRandomAsync());
        Assert.Equal(PokemonRarity.Rare, store.State.Active?.Rarity);
        Assert.Null(store.State.EggTier);
    }

    [Fact]
    public async Task Collection_ContainsCurrentAndGraduatedEntries()
    {
        var state = ActiveState() with
        {
            Dex = [Dex(25, PokemonRarity.Rare, true, PokemonNature.Jolly)],
        };
        var store = await Loaded(state);
        var viewModel = new EconomyViewModel(store, _ => Task.CompletedTask);
        Assert.Equal(2, viewModel.CollectionEntries.Count);
        Assert.Contains(viewModel.CollectionEntries, entry => entry.IsCurrent && entry.SpeciesId == 1);
        Assert.Contains(viewModel.CollectionEntries, entry => entry.SpeciesId == 25 && entry.IsShiny);
    }

    [Fact]
    public async Task Collection_RepresentativeCommandPersistsSelection()
    {
        var persistence = new MemoryPersistence(State() with { Dex = [Dex(25)] });
        var store = Create(persistence: persistence);
        var viewModel = new EconomyViewModel(store, _ => Task.CompletedTask);
        await Assert.Single(viewModel.CollectionEntries).SelectRepresentativeCommand.ExecuteAsync();
        Assert.Equal(25, Create(persistence: persistence).State.RepresentativeSpeciesId);
    }

    [Fact]
    public async Task EconomyViewModel_ExposesBalanceShopBagAndDisabledUse()
    {
        var store = Create(State(used: 1_000_000_000) with { Inventory = Inventory(candy: 1) });
        var viewModel = new EconomyViewModel(store, _ => Task.CompletedTask);
        Assert.Contains("1,000,000,000", viewModel.BalanceText);
        Assert.Equal(3, viewModel.ShopProducts.Count);
        Assert.Single(viewModel.BagItems);
        Assert.False(viewModel.BagItems[0].CanUse);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TempProductionPersistence_RestoresPurchaseUseAndCollection()
    {
        var directory = Directory.CreateTempSubdirectory("PokeTokenBar-Economy-QA-");
        try
        {
            var persistence = new JsonCompanionPersistence(Path.Combine(directory.FullName, "companion-state.json"));
            persistence.Save(ActiveState(used: 2_000_000_000) with { Dex = [Dex(25)] });
            var store = new CompanionStore(new EconomyApi(), persistence, new Random(2));
            Assert.True(await store.LoadCurrentLineAsync());
            Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("mint"));
            Assert.Equal(ItemUseResult.NatureChanged,
                (await store.UseItemAsync(CompanionItemKind.Mint)).Result);

            var restarted = new CompanionStore(new EconomyApi(), persistence, new Random(2));
            var viewModel = new EconomyViewModel(restarted, _ => Task.CompletedTask);
            Assert.Equal(100_000_000, restarted.State.SpentTokens);
            Assert.Equal(0, restarted.ItemCount(CompanionItemKind.Mint));
            Assert.Equal(2, viewModel.CollectionEntries.Count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static CompanionStore Create(
        CompanionState? state = null,
        MemoryPersistence? persistence = null,
        IPokeApiClient? api = null,
        Random? random = null) =>
        new(api ?? new EconomyApi(), persistence ?? new MemoryPersistence(state), random ?? new Random(1));

    private static async Task<CompanionStore> Loaded(
        CompanionState? state = null,
        MemoryPersistence? persistence = null)
    {
        var store = Create(state, persistence);
        Assert.True(await store.LoadCurrentLineAsync());
        return store;
    }

    private static CompanionState State(long used = 0, long spent = 0) => new()
    {
        InstallBaselineSet = true,
        UsedSinceInstall = used,
        SpentTokens = spent,
        LastDate = "2026-08-31",
    };

    private static CompanionState ActiveState(
        long used = 0,
        IReadOnlyDictionary<string, int>? inventory = null,
        long usedAtStage = 0) => State(used) with
    {
        Active = new MonState
        {
            BaseId = 1,
            PathIds = [1],
            PlannedPathIds = [1, 2, 3],
            StageIndex = 0,
            UsedAtStage = usedAtStage,
            Rarity = PokemonRarity.Common,
            TotalForms = 3,
            Nature = PokemonNature.Adamant,
        },
        Inventory = inventory ?? new Dictionary<string, int>(),
    };

    private static IReadOnlyDictionary<string, int> Inventory(
        int mint = 0, int candy = 0, int charm = 0) => new Dictionary<string, int>
    {
        ["mint"] = mint,
        ["rareCandy"] = candy,
        ["shinyCharm"] = charm,
    };

    private static IReadOnlyDictionary<string, int> One(CompanionItemKind kind) =>
        new Dictionary<string, int> { [kind.Key()] = 1 };

    private static CandyWindow Window(string key, double utilization) =>
        new(key, key, LimitWindowClass.Session, utilization);

    private static DexEntry Dex(
        int id,
        PokemonRarity rarity = PokemonRarity.Common,
        bool shiny = false,
        PokemonNature? nature = PokemonNature.Hardy) => new()
    {
        BaseId = id,
        FinalId = id,
        ChainOrder = [id],
        Rarity = rarity,
        IsShiny = shiny,
        Nature = nature,
        CaughtAt = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
        Names = new Dictionary<int, IReadOnlyDictionary<string, string>>
        {
            [id] = new Dictionary<string, string> { ["en"] = $"P{id}" },
        },
    };

    private sealed class MemoryPersistence(CompanionState? state = null) : ICompanionPersistence
    {
        public CompanionState? State { get; private set; } = state;
        public bool FailNextSave { get; set; }
        public CompanionState? Load() => State;
        public void Save(CompanionState state)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("fixture save failure");
            }

            State = state;
        }
        public void Delete() => State = null;
    }

    private sealed class EconomyApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult(baseSpeciesId == 3
                ? new EvoLine(3, new EvoNode(3, []), PokemonRarity.Rare,
                    new Dictionary<int, IReadOnlyDictionary<string, string>>())
                : Linear);

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>(
                [new BaseSpecies(1, 255), new BaseSpecies(3, 30)]);

        public Task<BaseSpecies?> GetBaseSpeciesAsync(int speciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(speciesId == 3
                ? new BaseSpecies(3, 30)
                : new BaseSpecies(1, 255));
    }

    private sealed class ThrowingApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            Task.FromException<EvoLine>(new IOException());
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int speciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(null);
    }
}
