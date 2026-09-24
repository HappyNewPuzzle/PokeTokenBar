using PokeTokenBar.Windows.Core;
using static PokeTokenBar.Windows.Tests.ProfileFixture;

namespace PokeTokenBar.Windows.Tests;

public sealed class ProfileProgressionTests
{
    [Fact]
    public async Task UnsupportedLegacySpeciesCannotMakeOptionalWarmupFailProgression()
    {
        var details = new DetailProvider();
        using var store = new CompanionStore(new Api { Offline = true }, new Memory(State(Mon() with
            { BaseId = 1000, PathIds = [1000], PlannedPathIds = [1000] })), detailProvider: details);
        Assert.False(await store.LoadCurrentLineAsync());
        await store.PreparePokemonProfilesAsync();
        Assert.Empty(details.Requests);
        Assert.NotNull(store.State.Active!.Profile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateMetadataCannotResurrectResetOrWriteAfterDoubleDispose(bool dispose)
    {
        var details = new DetailProvider { Release = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        var persistence = new Memory(State(Mon()));
        using var store = new CompanionStore(new Api(), persistence, detailProvider: details);
        var pending = store.PreparePokemonProfilesAsync();
        await details.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var original = store.State.Active!.Profile;
        if (dispose) { store.Dispose(); store.Dispose(); }
        else store.Reset();
        details.Release.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose) Assert.Equal(original, persistence.Value!.Active!.Profile);
        else { Assert.Null(store.State.Active); Assert.Null(persistence.Value); }
    }

    [Theory]
    [InlineData(0.1, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0.1, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task EveryDifficultyAndRepeatCombinationNormalizesGrowthAndGraduatesAt100(double difficulty, bool repeat)
    {
        for (var forms = 1; forms <= 3; forms++)
        {
            using var store = new CompanionStore(new Api(forms), new Memory(State() with
            { CollectedFinals = repeat ? new HashSet<string> { $"1:{forms}" } : new HashSet<string>() }),
                settingsPersistence: new Settings(difficulty));
            Assert.True(await store.HatchAsync(1));
            var original = store.State.Active!.Profile!;
            Assert.Equal(5, original.Level);
            Assert.Equal(0, original.GrowthTokens);
            Assert.Equal(repeat, store.State.Active.HasGrowthBoost);
            long usage = 0;
            long completed = 0;
            for (var stage = 0; stage < forms; stage++)
            {
                var threshold = store.StageThreshold(store.State.Active!);
                var standard = PokemonBalance.PhaseThreshold(PokemonRarity.Common, forms, stage);
                await Usage(store, usage + threshold / 2);
                Assert.Equal(completed + standard / 2, store.State.Active!.Profile!.GrowthTokens);
                Assert.Equal(original.IVs, store.State.Active.Profile.IVs);
                await Usage(store, usage += threshold);
                completed += standard;
            }
            Assert.Null(store.State.Active);
            var saved = Assert.Single(store.State.Dex).Profile!;
            Assert.Equal(100, saved.Level);
            Assert.Equal(750_000_000, saved.GrowthTokens);
            Assert.Equal(original.InstanceId, saved.InstanceId);
            Assert.Equal(original.Seed, saved.Seed);
            Assert.Equal(original.IVs, saved.IVs);
        }
    }

    [Fact]
    public async Task SmallChunksEqualSingleDeltaAtHardDifficulty()
    {
        using var small = new CompanionStore(new Api(), new Memory(State()), settingsPersistence: new Settings(2));
        using var large = new CompanionStore(new Api(), new Memory(State()), settingsPersistence: new Settings(2));
        await small.HatchAsync(1);
        await large.HatchAsync(1);
        for (var i = 1; i <= 20; i++) await Usage(small, i);
        await Usage(large, 20);
        Assert.Equal(10, small.State.Active!.Profile!.GrowthTokens);
        Assert.Equal(large.State.Active!.Profile!.GrowthTokens, small.State.Active.Profile.GrowthTokens);
    }

    [Fact]
    public async Task DifficultySavePreservesProfileExactlyAndNextRealUsageGrowsIt()
    {
        var settings = new Settings(1);
        var memory = new Memory(State(Mon(boosted: true)));
        using var store = new CompanionStore(new Api(), memory, settingsPersistence: settings);
        await Usage(store, 31_250_000);
        var before = store.State.Active!.Profile!;
        var events = 0;
        store.GameEventOccurred += (_, _) => events++;
        await store.SaveDifficultyAsync(0.5, 1, () => settings.Save(settings.Value with { GrowthDifficulty = 0.5 }));
        Assert.Equal(before, store.State.Active!.Profile);
        await Usage(store, 31_250_000);
        Assert.Equal(before, store.State.Active!.Profile);
        Assert.Equal(0, events);
        using var restart = new CompanionStore(new Api(), memory, settingsPersistence: settings);
        Assert.Equal(before, restart.State.Active!.Profile);
        await Usage(restart, 31_250_001);
        Assert.Equal(before.GrowthTokens + 4, restart.State.Active!.Profile!.GrowthTokens);
    }

    [Fact]
    public async Task OfflineOverflowRecoversAndCandyPreservesIdentityThroughGraduation()
    {
        var api = new Api { Offline = true };
        using var offline = new CompanionStore(api, new Memory(State(Mon(boosted: true))), settingsPersistence: new Settings(0.1));
        var original = offline.State.Active!.Profile!;
        await Usage(offline, 37_500_000);
        Assert.NotNull(offline.State.Active);
        api.Offline = false;
        await offline.LoadCurrentLineAsync();
        Assert.Equal(100, Assert.Single(offline.State.Dex).Profile!.Level);
        Assert.Equal(original.InstanceId, offline.State.Dex[0].Profile!.InstanceId);

        using var candy = new CompanionStore(new Api(), new Memory(State(Mon(boosted: true))), settingsPersistence: new Settings(0.1));
        var before = candy.State.Active!.Profile!;
        await candy.LoadCurrentLineAsync();
        Assert.Equal(ItemUseResult.Graduated, (await candy.UseItemAsync(CompanionItemKind.RareCandy)).Result);
        var caught = Assert.Single(candy.State.Dex).Profile!;
        Assert.Equal(100, caught.Level);
        Assert.Equal(before.InstanceId, caught.InstanceId);
        Assert.Equal(before.IVs, caught.IVs);
    }

    [Fact]
    public async Task ReleasedIndividualKeepsExactProfileAndDoesNotBecomeCollectedFinal()
    {
        using var store = new CompanionStore(new Api(), new Memory(State()));
        await store.HatchAsync(1);
        await Usage(store, 10_000_000);
        var before = store.State.Active!.Profile;
        Assert.Equal(PurchaseResult.Success, await store.PurchaseAsync("egg.basic"));
        var released = Assert.Single(store.State.Dex);
        Assert.True(released.IsReleased);
        Assert.Equal(before, released.Profile);
        Assert.InRange(released.Profile!.Level, 5, 99);
        Assert.Empty(store.State.CollectedFinals);
    }

    [Fact]
    public async Task DittoRebasesGrowthAndClearsSpeciesMetadataWithoutChangingIndividual()
    {
        var details = new DetailProvider();
        using var store = new CompanionStore(new Api(), new Memory(State(Mon(boosted: true) with { DittoDisguise = 1 })),
            settingsPersistence: new Settings(0.1), detailProvider: details);
        await store.LoadPokemonDetailsAsync(1);
        var original = store.State.Active!.Profile!;
        details.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Usage(store, 7_250_000); // 6.25M reveal + 1M overflow.
        var revealed = store.State.Active!;
        Assert.True(revealed.HasGrowthBoost);
        Assert.Equal(132, revealed.CurrentId);
        Assert.Equal(1_000_000, revealed.UsedAtStage);
        Assert.Equal(original.InstanceId, revealed.Profile!.InstanceId);
        Assert.Equal(original.Seed, revealed.Profile.Seed);
        Assert.Equal(original.IVs, revealed.Profile.IVs);
        Assert.Equal(500_000_000, revealed.Profile.GrowthTokens);
        Assert.Equal(20, revealed.Profile.Level);
        Assert.Null(revealed.Profile.Gender);
        Assert.Null(revealed.Profile.AbilitySlot);
        Assert.Null(revealed.Profile.AbilityName);
        Assert.Empty(revealed.Profile.Moves);
        details.Release.SetResult();
        await store.LoadPokemonDetailsAsync(132);
        Assert.Equal(PokemonGender.Genderless, store.State.Active!.Profile!.Gender);
        Assert.Equal("limber", store.State.Active.Profile.AbilityName);
        Assert.Equal("transform", Assert.Single(store.State.Active.Profile.Moves).Name);
        Assert.Equal(20, store.State.Active.Profile.Level);
    }

    [Fact]
    public async Task StartupOnlyWarmsActiveAndSuspendedMetadataDoesNotHoldMutationGate()
    {
        var details = new DetailProvider { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var seed = State(Mon()) with { Dex = Enumerable.Range(10, 100).Select(id => new DexEntry
            { Id = $"old-{id}", BaseId = id, FinalId = id, ChainOrder = [id] }).ToArray() };
        using var store = new CompanionStore(new Api(), new Memory(seed), detailProvider: details);
        Assert.Empty(details.Requests);
        var warmup = store.PreparePokemonProfilesAsync();
        await details.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1], details.Requests.ToArray());
        Assert.Contains(1, store.LoadingPokemonDetailIds);
        await Usage(store, 1).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, store.State.Active!.UsedAtStage);
        Assert.True(await store.HatchAsync(10).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(store.IsHatching);
        details.Release.SetResult();
        await warmup;
        await store.LoadPokemonDetailsAsync(10);
        Assert.Equal(10, store.State.Active!.CurrentId);
        Assert.Equal("ability-10", store.State.Active.Profile!.AbilityName);
        Assert.DoesNotContain(details.Requests, id => id is not (1 or 10));
    }

    [Fact]
    public async Task DetailFailureCanRetryAndEvolutionRefreshesSpeciesFieldsNotIdentity()
    {
        var details = new DetailProvider { Fail = true };
        using var store = new CompanionStore(new Api(), new Memory(State(Mon())), detailProvider: details);
        var original = store.State.Active!.Profile!;
        await store.LoadPokemonDetailsAsync(1);
        Assert.Contains(1, store.FailedPokemonDetailIds);
        Assert.Empty(store.LoadingPokemonDetailIds);
        Assert.Equal(original, store.State.Active.Profile);
        details.Fail = false;
        await store.LoadPokemonDetailsAsync(1);
        var gender = store.State.Active.Profile!.Gender;
        await Usage(store, 125_000_000);
        await store.LoadPokemonDetailsAsync(2);
        var evolved = store.State.Active!.Profile!;
        Assert.Equal(original.InstanceId, evolved.InstanceId);
        Assert.Equal(original.IVs, evolved.IVs);
        Assert.Equal(gender, evolved.Gender);
        Assert.Equal("ability-2", evolved.AbilityName);
        Assert.Equal("move-2", Assert.Single(evolved.Moves).Name);
        Assert.Equal(125_000_000, evolved.GrowthTokens);
    }
}
