using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class CompanionGameLoopTests
{
    private const string Day1 = "2026-08-30";
    private const string Day2 = "2026-08-31";

    [Fact]
    public async Task FirstObservationSeedsBaselineWithoutRetroactiveGrowth()
    {
        var fixture = Create();

        await Update(fixture.Store, Day1, ("codex", 20_000_000));

        Assert.True(fixture.Store.State.InstallBaselineSet);
        Assert.Equal(20_000_000, fixture.Store.State.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(0, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(0, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task SameUsageDoesNotGrowTwice()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 10));

        await Update(fixture.Store, Day1, ("codex", 10));

        Assert.Equal(0, fixture.Store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task IncreasedUsageAppliesOnlyProviderDelta()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 10));

        await Update(fixture.Store, Day1, ("codex", 4_000_010));

        Assert.Equal(4_000_000, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(4_000_000, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task DecreasedUsageRebasesWithoutNegativeGrowth()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 100));
        await Update(fixture.Store, Day1, ("codex", 150));

        await Update(fixture.Store, Day1, ("codex", 25));
        await Update(fixture.Store, Day1, ("codex", 40));

        Assert.Equal(65, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(40, fixture.Store.State.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public async Task RestartUsesPersistedLedgerWithoutDoubleCounting()
    {
        var persistence = new FakePersistence();
        var first = Create(persistence: persistence);
        await Update(first.Store, Day1, ("codex", 100));
        await Update(first.Store, Day1, ("codex", 125));

        persistence.Loaded = persistence.Saved;
        var restarted = Create(persistence: persistence);
        await Update(restarted.Store, Day1, ("codex", 125));

        Assert.Equal(25, restarted.Store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task DateChangeCountsCurrentDayAndKeepsMissingProviderOpenAtZero()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 100), ("claude", 200));

        await Update(fixture.Store, Day2, ("codex", 30));
        await Update(fixture.Store, Day2, ("codex", 40), ("claude", 25));

        Assert.Equal(65, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(25, fixture.Store.State.ClaimedTodayTokensByProvider!["claude"]);
    }

    [Fact]
    public async Task ProvidersHaveIndependentLedgersAndDeltasAreSummed()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 10), ("claude", 20));

        await Update(fixture.Store, Day1, ("codex", 15), ("claude", 27));

        Assert.Equal(12, fixture.Store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task NewlyAppearingProviderIsSeededWithoutRetroactiveGrowth()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 10));

        await Update(fixture.Store, Day1, ("codex", 20), ("claude", 1_000));
        await Update(fixture.Store, Day1, ("codex", 20), ("claude", 1_050));

        Assert.Equal(60, fixture.Store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task LegacyStateWithoutProviderLedgerSeedsFirstValidObservation()
    {
        var persistence = new FakePersistence
        {
            Loaded = new CompanionState
            {
                InstallBaselineSet = true,
                ClaimedTodayTokensByProvider = null,
                UsedSinceInstall = 123,
            },
        };
        var fixture = Create(persistence: persistence);

        await Update(fixture.Store, Day1, ("codex", 10_000));

        Assert.Equal(123, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(10_000, fixture.Store.State.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public async Task EmptyObservationDoesNotSetInitialBaseline()
    {
        var fixture = Create();

        await fixture.Store.UpdateUsageAsync(
            new Dictionary<string, long>(),
            Day1,
            hasUsageData: false);

        Assert.False(fixture.Store.State.InstallBaselineSet);
        Assert.Null(fixture.Store.State.ClaimedTodayTokensByProvider);
    }

    [Fact]
    public async Task EmptyObservationDoesNotAdvanceDateOrLedger()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 100));

        await fixture.Store.UpdateUsageAsync(
            new Dictionary<string, long>(),
            Day2,
            hasUsageData: false);
        await Update(fixture.Store, Day2, ("codex", 25));

        Assert.Equal(Day2, fixture.Store.State.LastDate);
        Assert.Equal(25, fixture.Store.State.UsedSinceInstall);
    }

    [Fact]
    public async Task UsageBelowEggThresholdPersistsProgress()
    {
        var fixture = Create();
        await Update(fixture.Store, Day1, ("codex", 100));

        await Update(fixture.Store, Day1, ("codex", 4_000_100));

        Assert.Equal(4_000_000, fixture.Store.State.EggUsage);
        Assert.Equal(4_000_000, fixture.Persistence.Saved!.EggUsage);
        Assert.Null(fixture.Store.State.Active);
    }

    [Fact]
    public async Task ExactEggThresholdAutomaticallyHatches()
    {
        var fixture = Create(line: Line(PokemonRarity.Common, 1, 2));
        await Update(fixture.Store, Day1, ("codex", 100));

        await Update(fixture.Store, Day1, ("codex", 5_000_100));

        Assert.Equal(1, fixture.Store.CurrentSpeciesId);
        Assert.Equal(0, fixture.Store.State.EggUsage);
        Assert.Equal([1, 2], fixture.Store.State.Active!.PlannedPathIds);
    }

    [Fact]
    public async Task EggThresholdOverflowCarriesIntoPokemonProgress()
    {
        var fixture = Create(line: Line(PokemonRarity.Common, 1, 2));
        await Update(fixture.Store, Day1, ("codex", 100));

        await Update(fixture.Store, Day1, ("codex", 7_000_100));

        Assert.Equal(2_000_000, fixture.Store.State.Active!.UsedAtStage);
    }

    [Fact]
    public async Task HatchApiFailurePreservesLedgerEggProgressAndPendingSpecies()
    {
        var api = Api(Line(PokemonRarity.Common, 1, 2));
        api.LineError = new HttpRequestException("offline");
        var fixture = Create(api: api);
        await Update(fixture.Store, Day1, ("codex", 100));

        await Update(fixture.Store, Day1, ("codex", 6_000_100));

        Assert.Equal(6_000_000, fixture.Store.State.EggUsage);
        Assert.Equal(6_000_000, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(1, fixture.Store.State.PendingHatchId);
        Assert.Null(fixture.Store.State.Active);
    }

    [Fact]
    public async Task HatchRetryUsesPendingSpeciesAndDoesNotDuplicateUsage()
    {
        var api = Api(Line(PokemonRarity.Common, 1, 2));
        api.LineError = new HttpRequestException("offline");
        var fixture = Create(api: api);
        await Update(fixture.Store, Day1, ("codex", 100));
        await Update(fixture.Store, Day1, ("codex", 6_000_100));
        api.LineError = null;

        await Update(fixture.Store, Day1, ("codex", 6_000_100));

        Assert.Equal(6_000_000, fixture.Store.State.UsedSinceInstall);
        Assert.Equal(1_000_000, fixture.Store.State.Active!.UsedAtStage);
        Assert.Null(fixture.Store.State.PendingHatchId);
        Assert.Equal(1, api.IndexCalls);
    }

    [Fact]
    public async Task AutomaticHatchPersistsRarityNatureAndShinyRoll()
    {
        var fixture = Create(
            line: Line(PokemonRarity.Rare, 1),
            random: new FixedRandom(0, 0, 3));
        await Update(fixture.Store, Day1, ("codex", 0));

        await Update(fixture.Store, Day1, ("codex", PokemonBalance.EggHatchThreshold));

        Assert.Equal(PokemonRarity.Rare, fixture.Store.State.Active!.Rarity);
        Assert.True(fixture.Store.State.Active.IsShiny);
        Assert.Equal((PokemonNature)3, fixture.Store.State.Active.Nature);
        Assert.Equal(fixture.Store.State.Active, fixture.Persistence.Saved!.Active);
    }

    [Fact]
    public async Task EvolutionBelowThresholdKeepsCurrentStage()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1, 2, 3));
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);

        await Update(fixture.Store, Day1, ("codex", threshold - 1));

        Assert.Equal(1, fixture.Store.CurrentSpeciesId);
        Assert.Equal(threshold - 1, fixture.Store.State.Active!.UsedAtStage);
    }

    [Fact]
    public async Task ExactEvolutionThresholdAdvancesAlongPlannedPath()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1, 2, 3));
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);

        await Update(fixture.Store, Day1, ("codex", threshold));

        Assert.Equal(2, fixture.Store.CurrentSpeciesId);
        Assert.Equal(1, fixture.Store.State.Active!.StageIndex);
        Assert.Equal(0, fixture.Store.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task EvolutionOverflowCarriesToNextStage()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1, 2, 3));
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);

        await Update(fixture.Store, Day1, ("codex", threshold + 42));

        Assert.Equal(2, fixture.Store.CurrentSpeciesId);
        Assert.Equal(42, fixture.Store.State.Active!.UsedAtStage);
    }

    [Fact]
    public async Task UsageAccruedWhileLineLoadFailsIsEvaluatedAfterRetry()
    {
        var line = Line(PokemonRarity.Common, 1, 2, 3);
        var api = Api(line);
        api.LineError = new HttpRequestException("offline");
        var fixture = Create(
            api: api,
            persistence: new FakePersistence { Loaded = ActiveState([1], [1, 2, 3]) });
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);

        await Update(fixture.Store, Day1, ("codex", threshold + 42));
        api.LineError = null;
        await Update(fixture.Store, Day1, ("codex", threshold + 42));

        Assert.Equal(2, fixture.Store.CurrentSpeciesId);
        Assert.Equal(42, fixture.Store.State.Active!.UsedAtStage);
    }

    [Fact]
    public async Task OneLargeDeltaCrossesAllStagesAndGraduates()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1, 2, 3));

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common)));

        Assert.Null(fixture.Store.State.Active);
        Assert.Equal([1, 2, 3], Assert.Single(fixture.Store.State.Dex).ChainOrder);
        Assert.Equal(0, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task PlannedBranchRemainsStableAcrossRestart()
    {
        var line = BranchLine();
        var persistence = new FakePersistence
        {
            Loaded = ActiveState([1], [1, 3]),
        };
        var first = Create(line: line, persistence: persistence);
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 2, 0);

        await Update(first.Store, Day1, ("codex", threshold));
        persistence.Loaded = persistence.Saved;
        var restarted = Create(line: line, persistence: persistence);
        await Update(restarted.Store, Day1, ("codex", threshold));

        Assert.Equal(3, restarted.Store.CurrentSpeciesId);
        Assert.Equal([1, 3], restarted.Store.State.Active!.PlannedPathIds);
    }

    [Fact]
    public async Task FinalStageBelowThresholdDoesNotGraduate()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1));

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common) - 1));

        Assert.NotNull(fixture.Store.State.Active);
        Assert.Empty(fixture.Store.State.Dex);
    }

    [Fact]
    public async Task FinalThresholdGraduatesAndCreatesFreshEgg()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1));

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common)));

        Assert.Null(fixture.Store.State.Active);
        Assert.Equal(CompanionStateKind.Egg, fixture.Store.DisplayState);
        Assert.Equal(0, fixture.Store.State.EggUsage);
        Assert.Equal("1:1", Assert.Single(fixture.Store.State.CollectedFinals));
    }

    [Fact]
    public async Task GraduationPersistsDexCatchHistoryAndIndividualTraits()
    {
        var caughtAt = new DateTimeOffset(2026, 8, 30, 1, 2, 3, TimeSpan.Zero);
        var state = ActiveState([1], [1]) with
        {
            Active = ActiveState([1], [1]).Active! with
            {
                IsShiny = true,
                Nature = PokemonNature.Jolly,
            },
        };
        var fixture = Create(
            line: Line(PokemonRarity.Common, 1),
            persistence: new FakePersistence { Loaded = state },
            timeProvider: new FixedTimeProvider(caughtAt));

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common)));

        var entry = Assert.Single(fixture.Store.State.Dex);
        Assert.Equal([1], entry.ChainOrder);
        Assert.Equal(caughtAt, entry.CaughtAt);
        Assert.True(entry.IsShiny);
        Assert.Equal(PokemonNature.Jolly, entry.Nature);
        Assert.NotNull(entry.Names);
        Assert.Equal(entry, Assert.Single(fixture.Persistence.Saved!.Dex));
    }

    [Fact]
    public async Task DuplicateSpeciesAddsCatchRecordButCollectedFinalRemainsUnique()
    {
        var state = ActiveState([1], [1]) with
        {
            Dex =
            [
                new DexEntry
                {
                    BaseId = 1,
                    FinalId = 1,
                    ChainOrder = [1],
                    Rarity = PokemonRarity.Common,
                },
            ],
            CollectedFinals = new HashSet<string> { "1:1" },
        };
        var fixture = Create(
            line: Line(PokemonRarity.Common, 1),
            persistence: new FakePersistence { Loaded = state });

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common)));

        Assert.Equal(2, fixture.Store.State.Dex.Count);
        Assert.Single(fixture.Store.State.CollectedFinals);
    }

    [Fact]
    public async Task GraduationPreservesOwnedRepresentativeSelection()
    {
        var state = ActiveState([1], [1]) with { RepresentativeSpeciesId = 1 };
        var fixture = Create(
            line: Line(PokemonRarity.Common, 1),
            persistence: new FakePersistence { Loaded = state });

        await Update(
            fixture.Store,
            Day1,
            ("codex", PokemonBalance.GraduationTotal(PokemonRarity.Common)));

        Assert.Equal(1, fixture.Store.State.RepresentativeSpeciesId);
        Assert.Equal(1, fixture.Store.RepresentativeSubject.SpeciesId);
    }

    [Fact]
    public async Task GraduationOverflowDoesNotCarryIntoNextEgg()
    {
        var fixture = ActiveFixture(Line(PokemonRarity.Common, 1));

        await Update(fixture.Store, Day1, ("codex", 900_000_000));

        Assert.Null(fixture.Store.State.Active);
        Assert.Equal(0, fixture.Store.State.EggUsage);
    }

    [Fact]
    public async Task ProgressAndLedgerSurvivePersistenceRoundTrip()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PokeTokenBar-Loop-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "companion-state.json");
        try
        {
            var persistence = new PokeTokenBar.Windows.Infrastructure.JsonCompanionPersistence(path);
            var api = Api(Line(PokemonRarity.Common, 1, 2));
            var store = new CompanionStore(api, persistence, new FixedRandom(1, 0, 1));
            await Update(store, Day1, ("codex", 100));
            await Update(store, Day1, ("codex", 4_000_100));

            var restarted = new CompanionStore(api, persistence);

            Assert.Equal(4_000_000, restarted.State.EggUsage);
            Assert.Equal(4_000_100, restarted.State.ClaimedTodayTokensByProvider!["codex"]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Fixture ActiveFixture(EvoLine line) =>
        Create(
            line: line,
            persistence: new FakePersistence
            {
                Loaded = ActiveState([line.BaseId], Path(line.Tree)),
            });

    private static CompanionState ActiveState(
        IReadOnlyList<int> reached,
        IReadOnlyList<int> planned) =>
        new()
        {
            InstallBaselineSet = true,
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 0 },
            LastDate = Day1,
            Active = new MonState
            {
                BaseId = reached[0],
                PathIds = reached,
                PlannedPathIds = planned,
                StageIndex = reached.Count - 1,
                UsedAtStage = 0,
                Rarity = PokemonRarity.Common,
                TotalForms = planned.Count,
                Nature = PokemonNature.Hardy,
            },
        };

    private static async Task Update(
        CompanionStore store,
        string date,
        params (string Id, long Tokens)[] values) =>
        await store.UpdateUsageAsync(
            values.ToDictionary(value => value.Id, value => value.Tokens),
            date,
            hasUsageData: true);

    private static Fixture Create(
        EvoLine? line = null,
        FakeApi? api = null,
        FakePersistence? persistence = null,
        Random? random = null,
        TimeProvider? timeProvider = null)
    {
        line ??= Line(PokemonRarity.Common, 1, 2);
        api ??= Api(line);
        persistence ??= new FakePersistence();
        var store = new CompanionStore(
            api,
            persistence,
            random ?? new FixedRandom(1, 0, 1),
            timeProvider);
        return new Fixture(store, api, persistence);
    }

    private static FakeApi Api(EvoLine line) =>
        new()
        {
            Index = [new BaseSpecies(line.BaseId, 255)],
            Lines = { [line.BaseId] = line },
        };

    private static EvoLine Line(PokemonRarity rarity, params int[] ids)
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
                ["en"] = $"#{id}",
            });
        return new EvoLine(ids[0], node, rarity, names);
    }

    private static EvoLine BranchLine()
    {
        var root = new EvoNode(1, [new EvoNode(2, []), new EvoNode(3, [])]);
        return new EvoLine(
            1,
            root,
            PokemonRarity.Common,
            new Dictionary<int, IReadOnlyDictionary<string, string>>());
    }

    private static IReadOnlyList<int> Path(EvoNode node)
    {
        var result = new List<int> { node.SpeciesId };
        while (node.Children.Count > 0)
        {
            node = node.Children[0];
            result.Add(node.SpeciesId);
        }

        return result;
    }

    private sealed record Fixture(
        CompanionStore Store,
        FakeApi Api,
        FakePersistence Persistence);

    private sealed class FakeApi : IPokeApiClient
    {
        public Dictionary<int, EvoLine> Lines { get; } = [];
        public IReadOnlyList<BaseSpecies> Index { get; set; } = [];
        public Exception? LineError { get; set; }
        public int IndexCalls { get; private set; }

        public Task<EvoLine> GetLineAsync(
            int baseSpeciesId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LineError is null
                ? Task.FromResult(Lines[baseSpeciesId])
                : Task.FromException<EvoLine>(LineError);
        }

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IndexCalls++;
            return Task.FromResult(Index);
        }

        public Task<BaseSpecies?> GetBaseSpeciesAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(Index.FirstOrDefault(value => value.Id == id));
    }

    private sealed class FakePersistence : ICompanionPersistence
    {
        public CompanionState? Loaded { get; set; }
        public CompanionState? Saved { get; private set; }
        public int SaveCount { get; private set; }
        public CompanionState? Load() => Loaded;
        public void Save(CompanionState state)
        {
            Saved = state;
            SaveCount++;
        }

        public void Delete() { }
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
