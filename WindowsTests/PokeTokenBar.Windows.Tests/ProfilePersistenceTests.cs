using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;
using static PokeTokenBar.Windows.Tests.ProfileFixture;

namespace PokeTokenBar.Windows.Tests;

public sealed class ProfilePersistenceTests : IDisposable
{
    private readonly ProfileFixture _fixture = new();
    private string PathFor(string name) => Path.Combine(_fixture.DirectoryPath, name);
    private void Write(string path, string content)
    {
        Directory.CreateDirectory(_fixture.DirectoryPath);
        File.WriteAllText(path, content);
    }

    [Theory]
    [InlineData(1, false, 125_000_000)]
    [InlineData(0.1, false, 12_500_000)]
    [InlineData(2, false, 250_000_000)]
    [InlineData(1, true, 62_500_000)]
    [InlineData(0.1, true, 6_250_000)]
    public void LegacyMigrationReconstructsStandardGrowthAndBacksUpOriginalExactlyOnce(double difficulty, bool boost, long used)
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        var legacy = State(Mon(stage: 1, used: used, boosted: boost)) with
        {
            Dex = [new() { Id = "graduate", BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3] },
                new() { Id = "released", BaseId = 1, FinalId = 2, ChainOrder = [1, 2], ReleasedAt = DateTimeOffset.UnixEpoch }],
        };
        var original = JsonSerializer.Serialize(legacy, JsonCompanionPersistence.SerializerOptions);
        Write(persistence.FilePath, original);
        using var store = new CompanionStore(new Api(), persistence, settingsPersistence: new Settings(difficulty));
        var profile = store.State.Active!.Profile!;
        Assert.Equal(250_000_000, profile.GrowthTokens);
        Assert.Equal(36, profile.Level);
        Assert.Equal(1, store.State.Active.StageIndex);
        Assert.Equal(used, store.State.Active.UsedAtStage);
        Assert.Equal(100, store.State.Dex[0].Profile!.Level);
        Assert.Equal(750_000_000, store.State.Dex[0].Profile!.GrowthTokens);
        Assert.Equal(250_000_000, store.State.Dex[1].Profile!.GrowthTokens);
        Assert.Equal(36, store.State.Dex[1].Profile!.Level);
        Assert.True(store.State.Dex[1].IsReleased);
        Assert.Empty(store.State.CollectedFinals);
        Assert.Equal(original, File.ReadAllText(persistence.PreProfilesBackupPath));
        var backupTime = File.GetLastWriteTimeUtc(persistence.PreProfilesBackupPath);
        var saved = File.ReadAllBytes(persistence.FilePath);
        using var restart = new CompanionStore(new Api(), persistence, settingsPersistence: new Settings(difficulty));
        Assert.Equal(profile.InstanceId, restart.State.Active!.Profile!.InstanceId);
        Assert.Equal(profile.IVs, restart.State.Active.Profile.IVs);
        Assert.Equal(saved, File.ReadAllBytes(persistence.FilePath));
        Assert.Equal(backupTime, File.GetLastWriteTimeUtc(persistence.PreProfilesBackupPath));
        Assert.Equal(original, File.ReadAllText(persistence.PreProfilesBackupPath));
        var duplicatePath = PathFor("second.json");
        Write(duplicatePath, original);
        using var sameLegacy = new CompanionStore(new Api(), new JsonCompanionPersistence(duplicatePath), settingsPersistence: new Settings(difficulty));
        Assert.Equal(profile.InstanceId, sameLegacy.State.Active!.Profile!.InstanceId);
        Assert.Equal(profile.Seed, sameLegacy.State.Active.Profile.Seed);
    }

    [Theory]
    [InlineData("\"broken\"")]
    [InlineData("[]")]
    [InlineData("{\"seed\":1,\"ivs\":{\"hp\":\"bad\"}}")]
    [InlineData("{\"seed\":1,\"ivs\":null}")]
    [InlineData("{\"seed\":1,\"ivs\":{},\"moves\":[{\"learnedAtLevel\":\"bad\"}]}")]
    public void MalformedNestedProfileKeepsActiveAndEveryDexEntryAndRepairsDeterministically(string malformed)
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        var json = $$"""
            {"active":{"baseID":1,"pathIDs":[1],"stageIndex":0,"usedAtStage":10,"rarity":"common","totalForms":3,"profile":{{malformed}}},
             "dex":[{"id":"kept","baseID":1,"finalID":3,"chainOrder":[1,2,3],"rarity":"common","profile":{{malformed}}},
                    {"baseID":10,"finalID":12,"chainOrder":[10,11,12],"rarity":"common"}]}
            """;
        Write(persistence.FilePath, json);
        var raw = persistence.Load()!;
        Assert.Equal(1, raw.Active!.CurrentId);
        Assert.Null(raw.Active.Profile);
        Assert.Equal(2, raw.Dex.Count);
        Assert.Null(raw.Dex[0].Profile);
        Assert.Equal(raw.Dex[1].Id, persistence.Load()!.Dex[1].Id); // Missing legacy ID is stable too.
        using var store = new CompanionStore(new Api(), persistence);
        Assert.NotNull(store.State.Active!.Profile);
        Assert.All(store.State.Dex, entry => Assert.NotNull(entry.Profile));
        Assert.Equal("kept", store.State.Dex[0].Id);
        Assert.Equal(json, File.ReadAllText(persistence.PreProfilesBackupPath));
    }

    [Fact]
    public void FreshInstallsAndProfileAwareStatesNeedNoMigrationBackup()
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        using var fresh = new CompanionStore(new Api(), persistence);
        Assert.False(Directory.Exists(_fixture.DirectoryPath));
        persistence.Save(State(Mon() with { Profile = PokemonProfile.Generate(1, "aware") }));
        using var aware = new CompanionStore(new Api(), persistence);
        Assert.False(File.Exists(persistence.PreProfilesBackupPath));
        Assert.Equal("aware", aware.State.Active!.Profile!.InstanceId);
    }

    [Fact]
    public void FailedMigrationBackupBlocksOverwriteOfOriginal()
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        persistence.Save(State(Mon()));
        var original = File.ReadAllBytes(persistence.FilePath);
        Directory.CreateDirectory(persistence.PreProfilesBackupPath); // deterministic backup destination failure
        using var store = new CompanionStore(new Api(), persistence);
        Assert.NotNull(store.State.Active!.Profile);
        Assert.Equal(original, File.ReadAllBytes(persistence.FilePath));
        Assert.Throws<IOException>(() => persistence.Save(store.State));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StateTransferAcceptsV1AndV2AndPreservesEarnedProfileOnHarderDevice(int version)
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        var settings = new JsonAppSettingsPersistence(PathFor("settings.json"));
        settings.Save(AppSettings.Default with { GrowthDifficulty = 0.1 });
        using (var source = new CompanionStore(new Api(1), new Memory(State()), settingsPersistence: new Settings(0.1)))
        {
            await source.HatchAsync(1);
            await Usage(source, 37_500_000);
            persistence.Save(source.State);
        }
        var transfer = new StateTransferService(settings, persistence, "2.5.7");
        var data = transfer.Export();
        Assert.Equal(2, transfer.Preview(data).FormatVersion);
        var originalProfile = persistence.Load()!.Active!.Profile!;
        var envelope = JsonNode.Parse(data)!;
        envelope["schema"] = version;
        if (version == 1) envelope["state"]!["active"]!.AsObject().Remove("profile");
        data = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        settings.Save(AppSettings.Default with { GrowthDifficulty = 2, ShopDifficulty = 1.5 });
        persistence.Save(State());
        transfer.Import(data);
        Assert.Equal(2, settings.Load()!.GrowthDifficulty);
        Assert.Equal(1.5, settings.Load()!.ShopDifficulty);
        using var destination = new CompanionStore(new Api(1), new JsonCompanionPersistence(persistence.FilePath), settingsPersistence: settings);
        var imported = destination.State.Active!.Profile!;
        Assert.Equal(52, imported.Level);
        Assert.Equal(375_000_000, imported.GrowthTokens);
        if (version == 2)
        {
            Assert.Equal(originalProfile.InstanceId, imported.InstanceId);
            Assert.Equal(originalProfile.IVs, imported.IVs);
            Assert.Equal(originalProfile.Seed, imported.Seed);
        }
        else Assert.True(File.Exists(persistence.PreProfilesBackupPath));
        await destination.LoadCurrentLineAsync();
        Assert.Equal(imported.Level, destination.State.Active!.Profile!.Level);
        envelope["schema"] = 3;
        Assert.Throws<StateTransferException>(() => transfer.Preview(Encoding.UTF8.GetBytes(envelope.ToJsonString())));
    }

    [Fact]
    public void Schema2ExportUsesExactInstanceIDKey()
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        persistence.Save(State(Mon() with { Profile = PokemonProfile.Generate(42, "exported-individual") }));
        var transfer = new StateTransferService(new JsonAppSettingsPersistence(PathFor("settings.json")), persistence, "2.5.7");

        using var document = JsonDocument.Parse(transfer.Export());
        Assert.Equal(2, document.RootElement.GetProperty("schema").GetInt32());
        var profile = document.RootElement.GetProperty("state").GetProperty("active").GetProperty("profile");
        Assert.Equal("exported-individual", profile.GetProperty("instanceID").GetString());
        Assert.False(profile.TryGetProperty("instanceId", out _));
    }

    [Fact]
    public void MacCompatibleInstanceIDDeserializesAndImportsWithoutChangingIdentity()
    {
        const string profileJson = """
            {"instanceID":"Mac-Individual-AbC123","seed":42,"ivs":{"hp":1,"attack":2,"defense":3,"specialAttack":4,"specialDefense":5,"speed":6}}
            """;
        var profile = JsonSerializer.Deserialize<PokemonProfile>(profileJson, JsonCompanionPersistence.SerializerOptions);
        Assert.Equal("Mac-Individual-AbC123", profile!.InstanceId);

        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        var transfer = new StateTransferService(new JsonAppSettingsPersistence(PathFor("settings.json")), persistence, "2.5.7");
        var envelope = JsonNode.Parse("""
            {"format":"poketokenbar.save","schema":2,"state":{"active":{"baseID":1,"pathIDs":[1],"stageIndex":0,"usedAtStage":0,"rarity":"common","totalForms":3}}}
            """)!;
        envelope["state"]!["active"]!["profile"] = JsonNode.Parse(profileJson);
        transfer.Import(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        Assert.Equal("Mac-Individual-AbC123", persistence.Load()!.Active!.Profile!.InstanceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileTrustBoundariesClampActiveAndDexAndRoundTripAllFields(bool belowMinimum)
    {
        var persistence = new JsonCompanionPersistence(PathFor("companion-state.json"));
        var settings = new JsonAppSettingsPersistence(PathFor("settings.json"));
        var hostile = PokemonProfile.Generate(42, "") with
        {
            Gender = PokemonGender.Female, AbilitySlot = 3, AbilityName = "hidden", AbilityIsHidden = true,
            IVs = new(999, -5, 31, 64, -1, 500), Level = belowMinimum ? -99 : 999,
            GrowthTokens = belowMinimum ? -1 : long.MaxValue,
            Moves = Enumerable.Range(0, 400).Select(i => new PokemonKnownMove(new string('x', 200), i % 2 == 0 ? 999 : -1)).ToArray(),
        };
        persistence.Save(State(Mon() with { Profile = hostile }) with { Dex = [new() { Id = "dex", BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3], Profile = hostile }] });
        var loaded = persistence.Load()!;
        foreach (var profile in new[] { loaded.Active!.Profile!, loaded.Dex[0].Profile! })
        {
            Assert.Equal(belowMinimum ? 5 : 100, profile.Level);
            Assert.Equal(belowMinimum ? 0 : PokemonProfile.MaxGrowthTokens, profile.GrowthTokens);
            Assert.Equal(new PokemonIVs(31, 0, 31, 31, 0, 31), profile.IVs);
            Assert.NotEmpty(profile.InstanceId);
            Assert.Equal(4, profile.Moves.Count);
            Assert.All(profile.Moves, m => { Assert.Equal(80, m.Name.Length); Assert.InRange(m.LearnedAtLevel, 0, 100); });
        }
        var transfer = new StateTransferService(settings, persistence, "2.5.7");
        var bytes = transfer.Export();
        var envelope = JsonNode.Parse(bytes)!;
        envelope["state"]!["active"]!["profile"] = JsonSerializer.SerializeToNode(hostile, JsonCompanionPersistence.SerializerOptions);
        envelope["state"]!["dex"]![0]!["profile"] = JsonSerializer.SerializeToNode(hostile, JsonCompanionPersistence.SerializerOptions);
        bytes = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        transfer.Import(bytes);
        var restored = persistence.Load()!;
        Assert.Equal(JsonSerializer.Serialize(loaded.Active!.Profile, JsonCompanionPersistence.SerializerOptions),
            JsonSerializer.Serialize(restored.Active!.Profile, JsonCompanionPersistence.SerializerOptions));
        Assert.Equal(JsonSerializer.Serialize(loaded.Dex[0].Profile, JsonCompanionPersistence.SerializerOptions),
            JsonSerializer.Serialize(restored.Dex[0].Profile, JsonCompanionPersistence.SerializerOptions));
    }

    public void Dispose() => _fixture.Dispose();
}
