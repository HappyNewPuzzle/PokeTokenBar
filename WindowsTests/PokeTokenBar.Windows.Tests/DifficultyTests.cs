using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class DifficultyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ptb-difficulty-" + Guid.NewGuid().ToString("N"));
    private const string Date = "2026-09-22";

    [Theory]
    [InlineData("{}", 1)]
    [InlineData("{\"growthDifficulty\":0,\"shopDifficulty\":0}", 0.1)]
    [InlineData("{\"growthDifficulty\":-1,\"shopDifficulty\":-1}", 0.1)]
    [InlineData("{\"growthDifficulty\":0.01,\"shopDifficulty\":0.01}", 0.1)]
    [InlineData("{\"growthDifficulty\":20,\"shopDifficulty\":20}", 2)]
    [InlineData("{\"growthDifficulty\":\"NaN\",\"shopDifficulty\":\"Infinity\"}", 1)]
    [InlineData("{\"growthDifficulty\":1e999,\"shopDifficulty\":-1e999}", 1)]
    public void SettingsPersistence_DefaultsAndNormalizesDifficulty(string json, double expected)
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, json);
        var settings = new JsonAppSettingsPersistence(path).Load()!;
        Assert.Equal(expected, settings.GrowthDifficulty);
        Assert.Equal(expected, settings.ShopDifficulty);
        var companion = new MemoryCompanion(Seed());
        var before = companion.State;
        var store = new CompanionStore(new Api(), companion, settingsPersistence: new JsonAppSettingsPersistence(path));
        Assert.Equal(expected, store.GrowthDifficulty);
        Assert.Equal(expected, store.ShopDifficulty);
        Assert.Equal(before, companion.State);
        Assert.Equal(0, companion.Saves);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task NonFiniteDifficultyFallsBackWithoutChangingProgress(double value)
    {
        var (store, settings, persistence) = Fixture(Seed() with { EggUsage = 2_500_000 });
        await store.SaveDifficultyAsync(value, value, () => { });
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(1, store.ShopDifficulty);
        Assert.Equal(2_500_000, store.State.EggUsage);
        Assert.Equal(0, persistence.Saves);
        Assert.False(JsonAppSettingsPersistence.IsValid(settings.State with { GrowthDifficulty = value }));
    }

    [Fact]
    public void MissingSettingsAndDefaultDifficultyPreserveBasePricesAndThresholds()
    {
        var settings = new JsonAppSettingsPersistence(Path.Combine(_directory, "missing.json"));
        var store = new CompanionStore(new Api(), new MemoryCompanion(Seed()), settingsPersistence: settings);
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(1, store.ShopDifficulty);
        Assert.Equal(5_000_000, store.EggHatchThreshold);
        Assert.Equal(125_000_000, store.StageThreshold(Mon()));
        Assert.Equal(new long[] { 100_000_000, 500_000_000, 1_000_000_000, 2_500_000_000, 3_000_000_000, 4_000_000_000 },
            store.ShopProducts.Select(product => product.Price));
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(0.1, 500_000)]
    [InlineData(1, 5_000_000)]
    [InlineData(2, 10_000_000)]
    public async Task EggUsesScaledThresholdAndCarriesOnlyOverflow(double growth, long threshold)
    {
        var (store, _, _) = Fixture(Seed(), growth);
        await Usage(store, threshold - 1);
        Assert.Null(store.State.Active);
        await Usage(store, threshold + 7);
        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(7, store.State.Active!.UsedAtStage);
    }

    [Theory]
    [InlineData(0.1, 12_500_000, 37_500_000)]
    [InlineData(1, 125_000_000, 375_000_000)]
    [InlineData(2, 250_000_000, 750_000_000)]
    public async Task EvolutionAndGraduationUseScaledThresholds(double growth, long evolution, long graduation)
    {
        var (store, _, _) = Fixture(Seed(Mon()), growth);
        await Usage(store, evolution - 1);
        Assert.Equal(1, store.CurrentSpeciesId);
        await Usage(store, evolution + 7);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(7, store.State.Active!.UsedAtStage);

        var (final, _, _) = Fixture(Seed(Mon(stage: 2)), growth);
        await Usage(final, graduation - 1);
        Assert.NotNull(final.State.Active);
        await Usage(final, graduation + 7);
        Assert.Null(final.State.Active);
        Assert.Equal(3, Assert.Single(final.State.Dex).FinalId);
        Assert.Equal(0, final.State.EggUsage);
    }

    [Theory]
    [InlineData(false, 0.1)]
    [InlineData(false, 2)]
    [InlineData(true, 0.1)]
    [InlineData(true, 2)]
    public async Task SavePreservesHalfProgressAndAllAccounting(bool active, double growth)
    {
        var state = Seed(active ? Mon(used: 62_500_000) : null) with { EggUsage = active ? 0 : 2_500_000 };
        var (store, settings, _) = Fixture(state);
        var before = store.State;
        var events = 0;
        store.GameEventOccurred += (_, _) => events++;
        await Save(store, settings, growth, 1.5);
        Assert.Equal(0.5, active
            ? (double)store.State.Active!.UsedAtStage / store.StageThreshold(store.State.Active)
            : (double)store.State.EggUsage / store.EggHatchThreshold, 10);
        Assert.Equal(before, store.State with { EggUsage = before.EggUsage, Active = before.Active });
        if (active) Assert.Equal(before.Active, store.State.Active! with { UsedAtStage = before.Active!.UsedAtStage });
        Assert.Equal(before.UsedSinceInstall - before.SpentTokens, store.AvailableTokens);
        Assert.Equal(0, events);
    }

    [Theory]
    [InlineData("egg")]
    [InlineData("evolution")]
    [InlineData("graduation")]
    [InlineData("ditto")]
    public async Task SaveAndZeroDeltaNeverCompleteAnIncompleteStageButNextTokenDoes(string kind)
    {
        var mon = kind switch
        {
            "egg" => null,
            "graduation" => Mon(stage: 2, used: 375_000_000 - 1),
            "ditto" => Mon(used: 125_000_000 - 1) with { DittoDisguise = 1 },
            _ => Mon(used: 125_000_000 - 1),
        };
        var (store, settings, _) = Fixture(Seed(mon) with { EggUsage = mon is null ? 4_999_999 : 0 });
        if (mon is not null) await store.LoadCurrentLineAsync();
        var species = store.CurrentSpeciesId;
        var events = 0;
        store.GameEventOccurred += (_, _) => events++;
        await Save(store, settings, 0.5, 1);
        Assert.Equal(species, store.CurrentSpeciesId);
        Assert.Equal(0, events);
        await Usage(store, 0);
        Assert.Equal(species, store.CurrentSpeciesId);
        Assert.Equal(0, events);
        await Usage(store, 1);
        Assert.True(events > 0);
        Assert.Equal(kind switch { "egg" => 1, "evolution" => 2, "ditto" => 132, _ => (int?)null }, store.CurrentSpeciesId);
    }

    [Fact]
    public async Task RepeatedChangesKeepFractionWithinWholeTokenRounding()
    {
        var (store, settings, _) = Fixture(Seed(Mon(used: 41_666_667)));
        for (var i = 0; i < 20; i++)
        {
            await Save(store, settings, 0.25, 1);
            await Save(store, settings, 2, 1);
            await Save(store, settings, 1, 1);
        }
        Assert.InRange(41_666_667 - store.State.Active!.UsedAtStage, 0, 4);
        Assert.Equal(0, store.State.Active.StageIndex);
    }

    [Fact]
    public async Task GrowthAndShopRemainIndependent()
    {
        var (store, settings, _) = Fixture(Seed(Mon(used: 10)));
        var prices = store.ShopProducts.Select(product => product.Price).ToArray();
        await Save(store, settings, 0.1, 1);
        Assert.Equal(500_000, store.EggHatchThreshold);
        Assert.Equal(12_500_000, store.StageThreshold(store.State.Active!));
        Assert.Equal(prices, store.ShopProducts.Select(product => product.Price));
        var state = store.State;
        await Save(store, settings, 0.1, 2);
        Assert.Same(state, store.State);
        Assert.Equal(500_000, store.EggHatchThreshold);
        Assert.Equal(12_500_000, store.StageThreshold(store.State.Active!));
        Assert.Equal(prices.Select(price => price * 2), store.ShopProducts.Select(product => product.Price));
    }

    [Theory]
    [InlineData("mint", 25_000_000)]
    [InlineData("rareCandy", 125_000_000)]
    [InlineData("shinyCharm", 750_000_000)]
    [InlineData("egg.basic", 250_000_000)]
    [InlineData("egg.uncommon", 625_000_000)]
    [InlineData("egg.rare", 1_000_000_000)]
    public async Task ShopDisplayGateAndDebitUseSameScaledPrice(string id, long price)
    {
        var (store, _, _) = Fixture(Seed(Mon()) with { UsedSinceInstall = price - 1, SpentTokens = 0 }, shop: 0.25);
        var vm = new EconomyViewModel(store, _ => Task.CompletedTask, new LocalizationService(AppLanguage.En));
        var row = Assert.Single(vm.ShopProducts, row => row.Product.Id == id);
        Assert.Equal(price, row.Product.Price);
        Assert.False(row.CanPurchase);
        Assert.Equal(PurchaseResult.InsufficientFunds, await store.PurchaseAsync(id));
        await Usage(store, 1);
        vm.Refresh();
        row = Assert.Single(vm.ShopProducts, row => row.Product.Id == id);
        Assert.True(row.CanPurchase);
        Assert.Equal(new LocalizationService(AppLanguage.En).Tokens(price), row.PriceText);
        await row.PurchaseCommand.ExecuteAsync();
        Assert.Equal(price, store.State.SpentTokens);
        Assert.Equal(0, store.AvailableTokens);
        if (id.StartsWith("egg.")) Assert.True(Assert.Single(store.State.Dex).IsReleased);
    }

    [Theory]
    [InlineData(0.1, ItemUseResult.Graduated)]
    [InlineData(1, ItemUseResult.Progressed)]
    [InlineData(2, ItemUseResult.Progressed)]
    public async Task RareCandyKeepsFixedXpAndExistingOverflowLoop(double growth, ItemUseResult expected)
    {
        var (store, _, _) = Fixture(Seed(Mon()), growth);
        await store.LoadCurrentLineAsync();
        var before = store.State.UsedSinceInstall;
        Assert.Equal(expected, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        if (expected == ItemUseResult.Progressed)
            Assert.Equal(100_000_000, store.State.Active!.UsedAtStage);
        else
            Assert.Equal(0, store.State.EggUsage);
        Assert.Equal(before, store.State.UsedSinceInstall);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public async Task RareCandyAtStandardDifficultyStillAdvancesOneStageWithOverflow()
    {
        var (store, _, _) = Fixture(Seed(Mon(used: 124_999_999)));
        await store.LoadCurrentLineAsync();
        Assert.Equal(ItemUseResult.Evolved, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(1, store.State.Active!.StageIndex);
        Assert.Equal(99_999_999, store.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task DraftDiscardSaveAndProductionCompositionRefreshAreConnected()
    {
        var companion = new MemoryCompanion(Seed() with { EggUsage = 2_500_000, UsedSinceInstall = 30_000_000, SpentTokens = 0 });
        var settings = new MemorySettings();
        using var composition = AppComposition.CreateApplication(new HttpClient(), companion, settings, new AutoStart());
        var vm = composition.ViewModel;
        var before = vm.Companion.ProgressText;
        vm.Settings.DraftGrowthDifficulty = 0.5;
        vm.Settings.DraftShopDifficulty = 0.25;
        Assert.True(vm.Settings.HasDifficultyChanges);
        Assert.True(vm.Settings.SaveDifficultyCommand.CanExecute(null));
        Assert.Equal(AppSettings.Default, settings.State);
        Assert.Equal(1, composition.CompanionStore.GrowthDifficulty);
        Assert.Equal(before, vm.Companion.ProgressText);
        vm.Settings.DiscardDifficultyDraft();
        Assert.False(vm.Settings.HasDifficultyChanges);
        Assert.False(vm.Settings.SaveDifficultyCommand.CanExecute(null));
        Assert.Equal(0, companion.Saves);

        vm.Settings.DraftGrowthDifficulty = 0.5;
        vm.Settings.DraftShopDifficulty = 0.25;
        var observed = false;
        vm.Settings.DifficultySaved += (_, _) =>
        {
            observed = true;
            Assert.Equal(0.5, composition.CompanionStore.GrowthDifficulty);
            Assert.Equal(0.25, composition.CompanionStore.ShopDifficulty);
            Assert.Equal(0.5, settings.State.GrowthDifficulty);
            Assert.Equal(0.25, settings.State.ShopDifficulty);
        };
        await vm.Settings.SaveDifficultyCommand.ExecuteAsync();
        Assert.True(observed);
        Assert.False(vm.Settings.HasDifficultyChanges);
        Assert.Equal(0.5, vm.Companion.Progress);
        Assert.NotEqual(before, vm.Companion.ProgressText);
        Assert.Equal(1_250_000, companion.State!.EggUsage);
        var mint = Assert.Single(vm.Economy.ShopProducts, row => row.Product.Id == "mint");
        Assert.True(mint.CanPurchase);
        Assert.Equal(25_000_000, mint.Product.Price);
        Assert.Null(composition.CompanionStore.State.Active);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SliderHasStableReachableValues(double value)
    {
        var vm = new SettingsViewModel(new MemorySettings(), new AutoStart());
        vm.DraftGrowthDifficulty = value;
        vm.DraftShopDifficulty = value;
        Assert.Equal(value, vm.DraftGrowthDifficulty);
        Assert.Equal(value, vm.DraftShopDifficulty);
        Assert.Equal(1, vm.GrowthDifficulty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsPersistence_RestartRestoresPreferencesAndRescaledCreditsOnce(bool active)
    {
        var settings = new JsonAppSettingsPersistence(Path.Combine(_directory, "settings.json"));
        var companion = new JsonCompanionPersistence(Path.Combine(_directory, "companion-state.json"));
        companion.Save(Seed(active ? Mon(used: 62_500_000) : null) with { EggUsage = active ? 0 : 2_500_000 });
        var store = new CompanionStore(new Api(), companion, settingsPersistence: settings);
        var vm = new SettingsViewModel(settings, new AutoStart(), companion: store)
        {
            DraftGrowthDifficulty = 0.5, DraftShopDifficulty = 1.5,
        };
        await vm.SaveDifficultyCommand.ExecuteAsync();
        Assert.Null(vm.ErrorMessage);
        var savedBytes = File.ReadAllBytes(companion.FilePath);
        for (var i = 0; i < 2; i++)
        {
            var restarted = new CompanionStore(new Api(), companion, settingsPersistence: settings);
            Assert.Equal(0.5, restarted.GrowthDifficulty);
            Assert.Equal(1.5, restarted.ShopDifficulty);
            Assert.Equal(active ? 31_250_000 : 1_250_000,
                restarted.State.Active?.UsedAtStage ?? restarted.State.EggUsage);
            Assert.Equal(savedBytes, File.ReadAllBytes(companion.FilePath));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFailureDoesNotPublishEitherDifficultyAndCanRetry(bool companionFails)
    {
        var (store, settings, companion) = Fixture(Seed() with { EggUsage = 2_500_000 });
        var vm = new SettingsViewModel(settings, new AutoStart(), companion: store)
        {
            DraftGrowthDifficulty = 0.5, DraftShopDifficulty = 0.25,
        };
        companion.Fail = companionFails;
        settings.Fail = !companionFails;
        await vm.SaveDifficultyCommand.ExecuteAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.HasDifficultyChanges);
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(1, store.ShopDifficulty);
        Assert.Equal(1, settings.State.GrowthDifficulty);
        Assert.Equal(1, settings.State.ShopDifficulty);
        Assert.Equal(2_500_000, companion.State!.EggUsage);
        Assert.Equal(2_500_000, store.State.EggUsage);
        companion.Fail = settings.Fail = false;
        await vm.SaveDifficultyCommand.ExecuteAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(1_250_000, store.State.EggUsage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateTransferKeepsDestinationDifficultyWhileImportingGameplay(bool legacy)
    {
        var settings = new JsonAppSettingsPersistence(Path.Combine(_directory, "settings.json"));
        var companion = new JsonCompanionPersistence(Path.Combine(_directory, "companion-state.json"));
        settings.Save(AppSettings.Default with { GrowthDifficulty = 0.25, ShopDifficulty = 0.5 });
        companion.Save(Seed(Mon(used: 123)));
        var transfer = new StateTransferService(settings, companion, "2.5.7");
        var data = transfer.Export();
        if (legacy)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(data)!;
            document["settings"]!.AsObject().Remove("growthDifficulty");
            document["settings"]!.AsObject().Remove("shopDifficulty");
            data = Encoding.UTF8.GetBytes(document.ToJsonString());
        }
        settings.Save(AppSettings.Default with { GrowthDifficulty = 2, ShopDifficulty = 1.5 });
        companion.Save(Seed());
        transfer.Import(data);
        Assert.Equal(2, settings.Load()!.GrowthDifficulty);
        Assert.Equal(1.5, settings.Load()!.ShopDifficulty);
        Assert.Equal(123, companion.Load()!.Active!.UsedAtStage);
        Assert.Equal(StateTransferService.FormatVersion, transfer.Preview(data).FormatVersion);
    }

    [Theory]
    [InlineData(AppLanguage.Ko)]
    [InlineData(AppLanguage.En)]
    [InlineData(AppLanguage.Ja)]
    [InlineData(AppLanguage.Es)]
    [InlineData(AppLanguage.Fr)]
    [InlineData(AppLanguage.Pt)]
    [InlineData(AppLanguage.De)]
    public void DifficultyLocalizationIsUsableInEveryLanguage(AppLanguage language)
    {
        var text = new LocalizationService(language);
        Assert.All(new[] { text.Difficulty, text.Growth, text.Shop, text.Save, text.DifficultyHint, text.DifficultySaveFailed },
            value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Contains("10%–200%", text.DifficultyHint);
        if (language != AppLanguage.En) Assert.NotEqual(new LocalizationService(AppLanguage.En).DifficultyHint, text.DifficultyHint);
    }

    [Fact]
    public void XamlUsesDraftBindingsAccessibleSlidersAndDirtySaveVisibility()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var document = XDocument.Load(Path.Combine(root, "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var sliders = document.Descendants(ns + "Slider").Where(element =>
            element.Attribute("Value")?.Value.Contains("Draft") == true).ToArray();
        Assert.Equal(2, sliders.Length);
        foreach (var slider in sliders)
        {
            Assert.Equal("0.1", slider.Attribute("Minimum")?.Value);
            Assert.Equal("2", slider.Attribute("Maximum")?.Value);
            Assert.Equal("0.05", slider.Attribute("SmallChange")?.Value);
            Assert.NotNull(slider.Attribute("AutomationProperties.Name"));
        }
        var button = Assert.Single(document.Descendants(ns + "Button"), element =>
            element.Attribute("Command")?.Value.Contains("SaveDifficultyCommand") == true);
        Assert.Contains(button.Descendants(ns + "DataTrigger"), element =>
            element.Attribute("Binding")?.Value.Contains("HasDifficultyChanges") == true);
        var code = File.ReadAllText(Path.Combine(root, "src", "PokeTokenBar.Windows.App", "MainWindow.xaml.cs"));
        Assert.Contains("IsVisibleChanged += OnVisibilityChanged", code);
        Assert.Contains("DiscardDifficultyDraft()", code);
    }

    private static CompanionState Seed(MonState? mon = null) => new()
    {
        InstallBaselineSet = true, LastDate = Date,
        ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["test"] = 0 },
        UsedSinceInstall = 2_000_000_000, SpentTokens = 10,
        Active = mon, Language = AppLanguage.En,
        Inventory = new Dictionary<string, int> { ["rareCandy"] = 1 },
    };

    private static MonState Mon(int stage = 0, long used = 0) => new()
    {
        BaseId = 1, PathIds = Enumerable.Range(1, stage + 1).ToArray(), PlannedPathIds = [1, 2, 3],
        StageIndex = stage, UsedAtStage = used, TotalForms = 3, Rarity = PokemonRarity.Common,
        IsShiny = true, Nature = PokemonNature.Hardy,
    };

    private static (CompanionStore Store, MemorySettings Settings, MemoryCompanion Persistence) Fixture(
        CompanionState state, double growth = 1, double shop = 1)
    {
        var settings = new MemorySettings { State = AppSettings.Default with { GrowthDifficulty = growth, ShopDifficulty = shop } };
        var persistence = new MemoryCompanion(state);
        return (new CompanionStore(new Api(), persistence, new Random(7), settingsPersistence: settings), settings, persistence);
    }

    private static Task Save(CompanionStore store, MemorySettings settings, double growth, double shop) =>
        store.SaveDifficultyAsync(growth, shop, () => settings.Save(settings.State with { GrowthDifficulty = growth, ShopDifficulty = shop }));
    private static Task Usage(CompanionStore store, long total) =>
        store.UpdateUsageAsync(new Dictionary<string, long> { ["test"] = total }, Date, true);

    private sealed class MemoryCompanion(CompanionState state) : ICompanionPersistence
    {
        public CompanionState? State { get; private set; } = state;
        public bool Fail { get; set; }
        public int Saves { get; private set; }
        public CompanionState? Load() => State;
        public void Save(CompanionState value) { if (Fail) throw new IOException("fixture"); State = value; Saves++; }
        public void Delete() => State = null;
    }
    private sealed class MemorySettings : IAppSettingsPersistence
    {
        public AppSettings State { get; set; } = AppSettings.Default;
        public bool Fail { get; set; }
        public AppSettings? Load() => State;
        public void Save(AppSettings value) { if (Fail) throw new IOException("fixture"); State = value; }
    }
    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => false;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }
    private sealed class Api : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(
            new EvoLine(id, id == 132 ? new EvoNode(id, []) : new EvoNode(1, [new EvoNode(2, [new EvoNode(3, [])])]),
                id == 132 ? PokemonRarity.Rare : PokemonRarity.Common, new Dictionary<int, IReadOnlyDictionary<string, string>>()));
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new BaseSpecies(1, 255)]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(id, 255));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
