using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class PokemonProfileTests
{
    [Fact]
    public void GenerationIsStableBoundedAndNewIndividualsAreDistinct()
    {
        for (ulong seed = 0; seed < 256; seed++)
        {
            var profile = PokemonProfile.Generate(seed, "fixture");
            Assert.Equal(profile, PokemonProfile.Generate(seed, "fixture"));
            Assert.All(PokemonStatCalculator.Order, stat => Assert.InRange(profile.IVs[stat], 0, 31));
            Assert.Equal(5, profile.Level);
            Assert.Equal(0, profile.GrowthTokens);
        }
        Assert.NotEqual(PokemonProfile.Create().InstanceId, PokemonProfile.Create().InstanceId);
        var migrated = PokemonProfileMigration.FromKey("fixture");
        // Fixed SHA-256 vector, independent of process hash randomization and culture.
        Assert.Equal("F16D05EC6B29248D2C61ADB1E9263F78", migrated.InstanceId);
    }

    [Theory]
    [InlineData(-1, PokemonGender.Genderless)]
    [InlineData(0, PokemonGender.Male)]
    [InlineData(8, PokemonGender.Female)]
    public void GenderExtremeRatesAndEnrichmentAreStable(int rate, PokemonGender gender)
    {
        var original = PokemonProfile.Generate(7, "fixture");
        var details = ProfileFixture.Details(1) with { GenderRate = rate };
        var resolved = original.Enrich(details);
        Assert.Equal(gender, resolved.Gender);
        Assert.Equal(resolved, resolved.Enrich(details));
        Assert.Equal(original.IVs, resolved.IVs);
        Assert.Equal(original.InstanceId, resolved.InstanceId);
    }

    [Fact]
    public void SeededGenderAndAbilityDistributionIncludesRareHiddenAndBothNormalSlots()
    {
        var details = ProfileFixture.Details(1) with
        {
            GenderRate = 4,
            Abilities = [new("first", 1, false), new("second", 2, false), new("hidden", 3, true)],
        };
        var profiles = Enumerable.Range(0, 4096).Select(seed => PokemonProfile.Generate((ulong)seed, "fixture").Enrich(details)).ToArray();
        Assert.InRange(profiles.Count(p => p.AbilityIsHidden), 15, 55);
        Assert.InRange(profiles.Count(p => p.AbilitySlot == 1), 1800, 2200);
        Assert.InRange(profiles.Count(p => p.Gender == PokemonGender.Female), 1800, 2200);
        var hidden = profiles.First(p => p.AbilityIsHidden);
        var repeated = PokemonProfile.Generate(hidden.Seed, "fixture").Enrich(details);
        Assert.Equal(hidden.Moves, repeated.Moves);
        Assert.Equal(hidden with { Moves = repeated.Moves }, repeated);
        var deferred = PokemonProfile.Generate(hidden.Seed, "fixture").Enrich(details with { Abilities = [] });
        Assert.Null(deferred.AbilityName);
        var retried = deferred.Enrich(details);
        Assert.Equal(hidden.Moves, retried.Moves);
        Assert.Equal(hidden with { Moves = retried.Moves }, retried);
    }

    [Fact]
    public void MovesAreTheLatestFourLegalLevelUpMovesWithStableTiesAndUpdateOnGrowth()
    {
        var details = ProfileFixture.Details(1) with { Moves =
        [
            new("z", [new("level-up", 1)]), new("a", [new("level-up", 1)]),
            new("b", [new("level-up", 2)]), new("c", [new("level-up", 3)]),
            new("d", [new("level-up", 4)]), new("e", [new("level-up", 50)]),
            new("a", [new("level-up", 1)]), new("tm", [new("machine", 0)]),
        ] };
        var profile = PokemonProfile.Generate(42, "fixture").Enrich(details);
        Assert.Equal(["z", "b", "c", "d"], profile.Moves.Select(m => m.Name));
        var grown = profile.AdvanceGrowth(375_000_000, PokemonRarity.Common).Enrich(details);
        Assert.Equal(52, grown.Level);
        Assert.Equal(["b", "c", "d", "e"], grown.Moves.Select(m => m.Name));
        Assert.Equal(grown, grown.Enrich(details));
        Assert.Equal(52, grown.AdvanceGrowth(0, PokemonRarity.Common).Level);
    }

    [Theory]
    [InlineData(PokemonNature.Hardy, null, null)]
    [InlineData(PokemonNature.Lonely, "attack", "defense")]
    [InlineData(PokemonNature.Brave, "attack", "speed")]
    [InlineData(PokemonNature.Adamant, "attack", "special-attack")]
    [InlineData(PokemonNature.Naughty, "attack", "special-defense")]
    [InlineData(PokemonNature.Bold, "defense", "attack")]
    [InlineData(PokemonNature.Docile, null, null)]
    [InlineData(PokemonNature.Relaxed, "defense", "speed")]
    [InlineData(PokemonNature.Impish, "defense", "special-attack")]
    [InlineData(PokemonNature.Lax, "defense", "special-defense")]
    [InlineData(PokemonNature.Timid, "speed", "attack")]
    [InlineData(PokemonNature.Hasty, "speed", "defense")]
    [InlineData(PokemonNature.Serious, null, null)]
    [InlineData(PokemonNature.Jolly, "speed", "special-attack")]
    [InlineData(PokemonNature.Naive, "speed", "special-defense")]
    [InlineData(PokemonNature.Modest, "special-attack", "attack")]
    [InlineData(PokemonNature.Mild, "special-attack", "defense")]
    [InlineData(PokemonNature.Quiet, "special-attack", "speed")]
    [InlineData(PokemonNature.Bashful, null, null)]
    [InlineData(PokemonNature.Rash, "special-attack", "special-defense")]
    [InlineData(PokemonNature.Calm, "special-defense", "attack")]
    [InlineData(PokemonNature.Gentle, "special-defense", "defense")]
    [InlineData(PokemonNature.Sassy, "special-defense", "speed")]
    [InlineData(PokemonNature.Careful, "special-defense", "special-attack")]
    [InlineData(PokemonNature.Quirky, null, null)]
    public void StatsMatchAllNaturePairsHpFormulaAndIvOrder(PokemonNature nature, string? raised, string? lowered)
    {
        var profile = PokemonProfile.Generate(1, "fixture") with { Level = 50, IVs = new(0, 1, 2, 3, 4, 5) };
        var stats = PokemonStatCalculator.Calculate(ProfileFixture.Details(1), profile, nature);
        Assert.Equal(PokemonStatCalculator.Order, stats.Select(s => s.Name));
        Assert.Equal([0, 1, 2, 3, 4, 5], stats.Select(s => s.Iv));
        foreach (var stat in stats)
        {
            var neutral = (2 * stat.Base + stat.Iv) * 50 / 100;
            var modifier = stat.Name == raised ? 1.1m : stat.Name == lowered ? 0.9m : 1m;
            Assert.Equal(modifier, PokemonStatCalculator.NatureModifier(nature, stat.Name));
            Assert.Equal(stat.Name == "hp" ? neutral + 60 : (int)decimal.Floor((neutral + 5) * modifier), stat.Value);
        }
    }
}

internal sealed class ProfileFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ptb-profile-" + Guid.NewGuid().ToString("N"));
    public const string Date = "2026-09-22";
    public static MonState Mon(int stage = 0, long used = 0, bool boosted = false) => new()
    {
        BaseId = 1, PathIds = Enumerable.Range(1, stage + 1).ToArray(), PlannedPathIds = [1, 2, 3],
        StageIndex = stage, UsedAtStage = used, TotalForms = 3, Rarity = PokemonRarity.Common, HasGrowthBoost = boosted,
    };
    public static CompanionState State(MonState? mon = null) => new()
    {
        Active = mon, InstallBaselineSet = true, LastDate = Date, UsedSinceInstall = 2_000_000_000,
        ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["test"] = 0 },
        Inventory = new Dictionary<string, int> { ["rareCandy"] = 1 },
    };
    public static Task Usage(CompanionStore store, long total) =>
        store.UpdateUsageAsync(new Dictionary<string, long> { ["test"] = total }, Date, true);
    public static PokemonDetails Details(int id) => new()
    {
        SpeciesId = id, Name = $"species-{id}", GenderRate = id == 132 ? -1 : 4,
        BaseStats = PokemonStatCalculator.Order.ToDictionary(s => s, _ => 50),
        Abilities = [new(id == 132 ? "limber" : $"ability-{id}", 1, false)],
        Moves = [new(id == 132 ? "transform" : $"move-{id}", [new("level-up", 1)]), new($"final-{id}", [new("level-up", 100)])],
    };
    public sealed class Memory(CompanionState state) : ICompanionPersistence
    {
        public CompanionState? Value { get; private set; } = state;
        public CompanionState? Load() => Value;
        public void Save(CompanionState value) => Value = value;
        public void Delete() => Value = null;
    }
    public sealed class Settings(double growth) : IAppSettingsPersistence
    {
        public AppSettings Value { get; set; } = AppSettings.Default with { GrowthDifficulty = growth };
        public AppSettings? Load() => Value;
        public void Save(AppSettings value) => Value = value;
    }
    public sealed class Api(int forms = 3) : IPokeApiClient
    {
        public bool Offline { get; set; }
        public Task<EvoLine> GetLineAsync(int id, CancellationToken cancellationToken = default)
        {
            if (Offline) throw new IOException("fixture offline");
            var count = id == 132 ? 1 : forms;
            var node = new EvoNode(id + count - 1, []);
            for (var offset = count - 2; offset >= 0; offset--) node = new(id + offset, [node]);
            return Task.FromResult(new EvoLine(id, node, id == 132 ? PokemonRarity.Rare : PokemonRarity.Common,
                new Dictionary<int, IReadOnlyDictionary<string, string>>()));
        }
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new(1, 255)]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<BaseSpecies?>(new(id, 255));
    }
    public sealed class DetailProvider : IPokemonDetailProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<int> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; set; }
        public bool Fail { get; set; }
        public bool IgnoreCancellation { get; set; }
        public async Task<PokemonDetails> GetPokemonDetailsAsync(int id, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(id);
            Started.TrySetResult();
            if (Release is { } release) await release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken);
            if (Fail) throw new IOException("fixture offline");
            return Details(id);
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
