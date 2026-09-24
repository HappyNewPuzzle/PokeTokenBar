using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class RepeatGrowthTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ptb-repeat-" + Guid.NewGuid().ToString("N"));
    private const string Date = "2026-09-22";

    [Theory]
    [InlineData(PokemonRarity.Common)]
    [InlineData(PokemonRarity.Uncommon)]
    [InlineData(PokemonRarity.Rare)]
    [InlineData(PokemonRarity.Legendary)]
    public void ThresholdsComposeRepeatThenDifficultyForEverySupportedFormAndStage(PokemonRarity rarity)
    {
        var store = Create(Seed());
        // Include every form count accepted by StateTransfer, not only the usual 1..3.
        for (var forms = 1; forms <= 12; forms++)
        for (var stage = 0; stage < forms; stage++)
        foreach (var difficulty in new[] { 0.1, 0.5, 1, 2 })
        {
            var mon = Mon() with { Rarity = rarity, TotalForms = forms, StageIndex = stage };
            var standard = PokemonBalance.PhaseThreshold(rarity, forms, stage);
            var half = (long)Math.Round(standard / 2d, MidpointRounding.AwayFromZero);
            Assert.Equal((long)Math.Round(standard * difficulty, MidpointRounding.AwayFromZero),
                store.StageThreshold(mon with { HasGrowthBoost = false }, difficulty));
            Assert.Equal(Math.Max(1, (long)Math.Round(half * difficulty, MidpointRounding.AwayFromZero)),
                store.StageThreshold(mon, difficulty));
        }
        Assert.Equal(31_250_000, store.StageThreshold(Mon(), 0.5));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    public void RepeatRoundingIsAwayFromZeroAndNeverBelowOne(long standard, long expected)
    {
        Assert.Equal(expected, PokemonBalance.RepeatAdjustedThreshold(standard, true));
        Assert.True(PokemonBalance.Scale(expected, 0.1) >= 1);
    }

    [Fact]
    public async Task GraduationEnablesRepeatBaseEvenWhenNewPlannedFinalDiffers()
    {
        var store = Create(Seed(), api: new Api(branching: true));
        Assert.True(await store.HatchAsync(1));
        Assert.False(store.State.Active!.HasGrowthBoost);
        var firstFinal = store.State.Active.PlannedPathIds[^1];
        await Usage(store, 750_000_000);
        Assert.Null(store.State.Active);
        Assert.Equal($"1:{firstFinal}", Assert.Single(store.State.CollectedFinals));
        Assert.True(store.State.HasCollectedFinalForBase(1));

        Assert.True(await store.HatchAsync(1));
        var repeat = store.State.Active!;
        Assert.NotEqual(firstFinal, repeat.PlannedPathIds[^1]);
        Assert.True(repeat.HasGrowthBoost);
        Assert.Equal(125_000_000, store.StageThreshold(repeat));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("1:3", true)]
    [InlineData("10:12", false)]
    public async Task HatchEligibilityUsesExactBasePrefix(string collected, bool expected)
    {
        var store = Create(Seed() with { CollectedFinals = Finals(collected) });
        Assert.True(await store.HatchAsync(1));
        Assert.Equal(expected, store.State.Active!.HasGrowthBoost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EggPurchaseReleasedRecordAloneDoesNotQualify(bool previouslyGraduated)
    {
        var store = Create(Seed() with { CollectedFinals = Finals(previouslyGraduated ? "1:3" : "") });
        Assert.True(await store.HatchAsync(1));
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("egg.basic"));
        Assert.True(Assert.Single(store.State.Dex).IsReleased);
        Assert.Equal(previouslyGraduated, store.State.HasCollectedFinalForBase(1));
        await Usage(store, store.EggHatchThreshold);
        Assert.Equal(previouslyGraduated, store.State.Active!.HasGrowthBoost);
    }

    [Theory]
    [InlineData(0.1, 6_250_000, 18_750_000)]
    [InlineData(1, 62_500_000, 187_500_000)]
    [InlineData(2, 125_000_000, 375_000_000)]
    public async Task EvolutionAndGraduationUseBoostedThresholdsAndPreserveOverflow(
        double growth, long evolution, long graduation)
    {
        var store = Create(Seed(Mon()), growth);
        await Usage(store, evolution - 1);
        Assert.Equal(1, store.CurrentSpeciesId);
        await Usage(store, evolution + 7);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.True(store.State.Active!.HasGrowthBoost);
        Assert.Equal(7, store.State.Active.UsedAtStage);

        var final = Create(Seed(Mon(stage: 2)) with { RepresentativeSpeciesId = 3 }, growth);
        await Usage(final, graduation - 1);
        Assert.NotNull(final.State.Active);
        await Usage(final, graduation + 7);
        Assert.Null(final.State.Active);
        Assert.Equal(3, Assert.Single(final.State.Dex).FinalId);
        Assert.Contains("1:3", final.State.CollectedFinals);
        Assert.Equal(3, final.State.RepresentativeSpeciesId);
        Assert.Equal(0, final.State.EggUsage); // Graduation overflow is discarded.
    }

    [Theory]
    [InlineData(0.1, 500_000)]
    [InlineData(1, 5_000_000)]
    [InlineData(2, 10_000_000)]
    public async Task RepeatEggUsesSameIncubationThenOverflowUsesBoost(double difficulty, long eggThreshold)
    {
        var store = Create(Seed() with { CollectedFinals = Finals("1:3") }, difficulty);
        Assert.Equal(eggThreshold, store.EggHatchThreshold);
        await Usage(store, eggThreshold - 1);
        Assert.Null(store.State.Active);
        var firstStage = (long)(62_500_000 * difficulty);
        await Usage(store, eggThreshold + firstStage + 7);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.True(store.State.Active!.HasGrowthBoost);
        Assert.Equal(7, store.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task DifficultySavePreservesBoostedFractionAccountingAndPresentation()
    {
        var store = Create(Seed(Mon(used: 31_250_000)));
        using var vm = ViewModel(store);
        var before = store.State;
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        await store.SaveDifficultyAsync(0.5, 1, () => { });
        vm.RefreshPresentation();
        Assert.Equal(31_250_000, store.StageThreshold(store.State.Active!));
        Assert.Equal(15_625_000, store.State.Active!.UsedAtStage);
        Assert.Equal(0.5, vm.Progress);
        Assert.True(vm.HasGrowthBoost);
        Assert.Contains(nameof(vm.HasGrowthBoost), changes);
        Assert.Equal(before, store.State with { Active = before.Active });
        Assert.Equal(before.Active, store.State.Active with { UsedAtStage = before.Active!.UsedAtStage });
    }

    [Theory]
    [InlineData("egg")]
    [InlineData("evolution")]
    [InlineData("graduation")]
    [InlineData("ditto")]
    public async Task DifficultySaveAndZeroDeltaDoNotTransitionButNextRealTokenDoes(string kind)
    {
        var mon = kind switch
        {
            "egg" => null,
            "graduation" => Mon(stage: 2, used: 187_500_000 - 1),
            "ditto" => Mon(used: 62_500_000 - 1) with { DittoDisguise = 1 },
            _ => Mon(used: 62_500_000 - 1),
        };
        var store = Create(Seed(mon) with { EggUsage = mon is null ? 4_999_999 : 0, CollectedFinals = Finals("1:3") });
        if (mon is not null) Assert.True(await store.LoadCurrentLineAsync());
        var events = 0;
        store.GameEventOccurred += (_, _) => events++;
        var species = store.CurrentSpeciesId;
        await store.SaveDifficultyAsync(0.5, 1, () => { });
        Assert.Equal(species, store.CurrentSpeciesId);
        Assert.Equal(0, events);
        await Usage(store, 0);
        Assert.Equal(species, store.CurrentSpeciesId);
        Assert.Equal(0, events);
        await Usage(store, 1);
        Assert.True(events > 0);
        Assert.Equal(kind switch { "egg" => 1, "evolution" => 2, "ditto" => 132, _ => (int?)null }, store.CurrentSpeciesId);
        if (store.State.Active is { } active) Assert.True(active.HasGrowthBoost);
    }

    [Fact]
    public async Task DittoRepeatHatchRevealsWithOverflowAndKeepsIndividualBoostAfterBaseChanges()
    {
        var store = Create(Seed() with { CollectedFinals = Finals("1:3") }, random: new ZeroRandom(), ditto: true);
        Assert.True(await store.HatchAsync(1));
        Assert.Equal(1, store.State.Active!.DittoDisguise);
        using var vm = ViewModel(store);
        await Usage(store, 62_500_000 - 1);
        Assert.False(store.State.Active!.DittoRevealed);
        await Usage(store, 62_500_000 + 7);
        await vm.RefreshAsync();
        var revealed = store.State.Active!;
        Assert.True(revealed.DittoRevealed);
        Assert.True(vm.HasGrowthBoost);
        Assert.Equal(132, revealed.BaseId);
        Assert.False(store.State.HasCollectedFinalForBase(132));
        Assert.Equal(7, revealed.UsedAtStage);
        Assert.Equal(1_500_000_000, store.StageThreshold(revealed));
        await Usage(store, 62_500_000 + 1_500_000_000);
        Assert.Null(store.State.Active);
        Assert.Contains("132:132", store.State.CollectedFinals);
    }

    [Theory]
    [InlineData(1, ItemUseResult.Evolved, 1, 37_500_000)]
    [InlineData(0.5, ItemUseResult.Evolved, 2, 6_250_000)]
    [InlineData(0.1, ItemUseResult.Graduated, -1, 0)]
    public async Task RareCandyKeepsFixedExperienceAndExistingMultiStageOverflow(
        double difficulty, ItemUseResult outcome, int stage, long overflow)
    {
        var store = Create(Seed(Mon()), difficulty);
        Assert.True(await store.LoadCurrentLineAsync());
        var before = store.State;
        Assert.Equal(100_000_000, CompanionEconomyRules.RareCandyExperience);
        Assert.Equal(outcome, (await store.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        Assert.Equal(stage, store.State.Active?.StageIndex ?? -1);
        Assert.Equal(overflow, store.State.Active?.UsedAtStage ?? store.State.EggUsage);
        if (store.State.Active is { } active) Assert.True(active.HasGrowthBoost);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(before.UsedSinceInstall, store.State.UsedSinceInstall);
        Assert.Equal(before.SpentTokens, store.State.SpentTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceRestartAndStateTransferPreserveDecisionWithoutLegacyRetroactiveBoost(bool legacy)
    {
        var persistence = new JsonCompanionPersistence(Path.Combine(_directory, "companion-state.json"));
        var settings = new JsonAppSettingsPersistence(Path.Combine(_directory, "settings.json"));
        persistence.Save(Seed(Mon(used: 123)) with { CollectedFinals = Finals("1:3") });
        if (legacy)
        {
            var json = JsonNode.Parse(File.ReadAllText(persistence.FilePath))!;
            Assert.True(json["active"]!.AsObject().Remove("hasGrowthBoost"));
            File.WriteAllText(persistence.FilePath, json.ToJsonString());
        }
        var restart = new CompanionStore(new Api(), persistence);
        await restart.LoadCurrentLineAsync();
        Assert.Equal(!legacy, restart.State.Active!.HasGrowthBoost);
        Assert.Equal(123, restart.State.Active.UsedAtStage);
        using var vm = ViewModel(restart);
        await vm.InitializeAsync();
        Assert.Equal(!legacy, vm.HasGrowthBoost);

        var transfer = new StateTransferService(settings, persistence, "2.5.7");
        var data = transfer.Export();
        if (legacy)
        {
            var envelope = JsonNode.Parse(data)!;
            envelope["state"]!["active"]!.AsObject().Remove("hasGrowthBoost");
            data = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        }
        Assert.Equal(2, transfer.Preview(data).FormatVersion);
        persistence.Save(Seed());
        transfer.Import(data, new Dictionary<string, long> { ["test"] = 0 }, Date, hasUsageData: true);
        var imported = new CompanionStore(new Api(), new JsonCompanionPersistence(persistence.FilePath));
        Assert.Equal(!legacy, imported.State.Active!.HasGrowthBoost);
        Assert.Equal(123, imported.State.Active.UsedAtStage);
        await Usage(imported, 0);
        Assert.Equal(123, imported.State.Active!.UsedAtStage);
        Assert.Equal(!legacy, imported.State.Active.HasGrowthBoost);
    }

    [Fact]
    public async Task RestartMaintenanceUsesBoostedThresholdWithoutRecomputingEligibility()
    {
        var persistence = new JsonCompanionPersistence(Path.Combine(_directory, "companion-state.json"));
        persistence.Save(Seed(Mon(used: 62_500_007))); // No collected base: individual decision remains authoritative.
        var store = new CompanionStore(new Api(), persistence);
        await Usage(store, 0);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(7, store.State.Active!.UsedAtStage);
        Assert.True(store.State.Active.HasGrowthBoost);
    }

    [Theory]
    [InlineData("1:3", 150, 10)]
    [InlineData("10:12", 150, 1)]
    [InlineData("", 200, 1)]
    public async Task CollectedBaseRollWeightRemainsHalfIndependentlyOfGrowth(string collected, int totalWeight, int selected)
    {
        var random = new WeightRandom();
        var store = Create(Seed() with { CollectedFinals = Finals(collected) }, random: random, api: new Api(twoBases: true));
        Assert.True(await store.HatchRandomAsync());
        Assert.Equal(totalWeight, random.FirstMaximum);
        Assert.Equal(selected, store.State.Active!.BaseId);
        Assert.False(store.State.Active.HasGrowthBoost); // Roll 50 selects an uncollected base in all three fixtures.
    }

    [Fact]
    public async Task ViewModelBadgeRefreshesAfterHatchAndDisappearsWithNewEgg()
    {
        var store = Create(Seed() with { CollectedFinals = Finals("1:3") });
        using var vm = ViewModel(store);
        Assert.False(vm.HasGrowthBoost);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.True(await vm.HatchSpecificAsync(1));
        Assert.True(vm.HasGrowthBoost);
        Assert.Contains(nameof(vm.HasGrowthBoost), changes);
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("egg.basic"));
        await vm.RefreshAsync();
        Assert.True(vm.IsEgg);
        Assert.False(vm.HasGrowthBoost);
        Assert.True(await vm.HatchSpecificAsync(10));
        Assert.False(vm.HasGrowthBoost);
    }

    [Fact]
    public void BadgeUsesSevenLocalizedLabelsAndViewModelVisibilityBinding()
    {
        var labels = new[] { "2× 성장", "2× growth", "成長2倍", "Crecimiento ×2", "Croissance ×2", "Crescimento ×2", "2× Wachstum" };
        var texts = new LocalizationService(AppLanguage.En);
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            texts.Language = language;
            Assert.Equal(labels[(int)language], texts.GrowthBoost);
        }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var document = XDocument.Load(Path.Combine(root, "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var label = Assert.Single(document.Descendants(ns + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding Texts.GrowthBoost, Mode=OneWay}");
        var badge = label.Parent!;
        Assert.Equal(ns + "Border", badge.Name);
        var style = Assert.Single(badge.Descendants(ns + "Style"));
        Assert.Equal("Collapsed", Assert.Single(style.Elements(ns + "Setter")).Attribute("Value")?.Value);
        var trigger = Assert.Single(style.Descendants(ns + "DataTrigger"));
        Assert.Equal("{Binding Companion.HasGrowthBoost, Mode=OneWay}", trigger.Attribute("Binding")?.Value);
        Assert.Equal("True", trigger.Attribute("Value")?.Value);
        Assert.Equal("Visible", Assert.Single(trigger.Elements(ns + "Setter")).Attribute("Value")?.Value);
    }

    private static IReadOnlySet<string> Finals(string entry) =>
        entry.Length == 0 ? new HashSet<string>() : new HashSet<string> { entry };

    private static MonState Mon(int stage = 0, long used = 0) => new()
    {
        BaseId = 1, PathIds = Enumerable.Range(1, stage + 1).ToArray(), PlannedPathIds = [1, 2, 3],
        StageIndex = stage, UsedAtStage = used, TotalForms = 3, Rarity = PokemonRarity.Common,
        HasGrowthBoost = true,
    };

    private static CompanionState Seed(MonState? mon = null) => new()
    {
        InstallBaselineSet = true, LastDate = Date,
        ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["test"] = 0 },
        Active = mon, UsedSinceInstall = 2_000_000_000, Language = AppLanguage.En,
        Inventory = new Dictionary<string, int> { ["rareCandy"] = 1 },
    };

    private static CompanionStore Create(CompanionState state, double difficulty = 1,
        Api? api = null, Random? random = null, bool ditto = false) =>
        new(api ?? new Api(), new MemoryPersistence(state), random ?? new Random(7),
            dittoDisguiseRollingEnabled: ditto, settingsPersistence: new Settings(difficulty));

    private static Task Usage(CompanionStore store, long total) =>
        store.UpdateUsageAsync(new Dictionary<string, long> { ["test"] = total }, Date, true);

    private static CompanionViewModel ViewModel(CompanionStore store) =>
        new(store, (_, _, _) => Task.FromResult<PokemonSpriteAsset?>(null), new NullDecoder());

    private sealed class NullDecoder : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation? Decode(PokemonSpriteAsset asset) => null;
    }

    private sealed class Settings(double difficulty) : IAppSettingsPersistence
    {
        public AppSettings? Load() => AppSettings.Default with { GrowthDifficulty = difficulty };
        public void Save(AppSettings settings) => throw new NotSupportedException();
    }

    private sealed class MemoryPersistence(CompanionState state) : ICompanionPersistence
    {
        private CompanionState? _state = state;
        public CompanionState? Load() => _state;
        public void Save(CompanionState value) => _state = value;
        public void Delete() => _state = null;
    }

    private sealed class Api(bool branching = false, bool twoBases = false) : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(
            new EvoLine(id, id == 132 ? new EvoNode(id, []) : branching
                    ? new EvoNode(id, [new EvoNode(id + 1, []), new EvoNode(id + 2, [])])
                    : new EvoNode(id, [new EvoNode(id + 1, [new EvoNode(id + 2, [])])]),
                id == 132 ? PokemonRarity.Rare : PokemonRarity.Common, new Dictionary<int, IReadOnlyDictionary<string, string>>()));
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>(twoBases ? [new(1, 100), new(10, 100)] : [new(1, 100)]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new(id, 100));
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    private sealed class WeightRandom : Random
    {
        public int? FirstMaximum { get; private set; }
        public override int Next(int maxValue)
        {
            if (FirstMaximum is not null) return 0;
            FirstMaximum = maxValue;
            return 50;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
