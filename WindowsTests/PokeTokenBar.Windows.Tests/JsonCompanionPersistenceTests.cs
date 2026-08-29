using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class JsonCompanionPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-CompanionPersistence-{Guid.NewGuid():N}");

    private string StatePath => Path.Combine(_directory, "companion-state.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFile_ReturnsNullWithoutCreatingDirectory()
    {
        var persistence = new JsonCompanionPersistence(StatePath);

        Assert.Null(persistence.Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void SaveLoad_RoundTripsState()
    {
        var persistence = new JsonCompanionPersistence(StatePath);
        var state = State(1, 2);

        persistence.Save(state);
        var loaded = Assert.IsType<CompanionState>(persistence.Load());

        Assert.Equal(42, loaded.UsedSinceInstall);
        Assert.Equal(AppLanguage.Ko, loaded.Language);
        Assert.Equal(2, loaded.Active!.CurrentId);
        Assert.True(loaded.Active.IsShiny);
        Assert.Equal(1, Assert.Single(loaded.Dex).BaseId);
        Assert.Contains("1:3", loaded.CollectedFinals);
    }

    [Fact]
    public void CaughtAt_UsesSwiftJsonEncoderReferenceDateNumber()
    {
        var persistence = new JsonCompanionPersistence(StatePath);
        var caughtAt = new DateTimeOffset(2001, 1, 1, 0, 1, 0, TimeSpan.Zero);
        var state = State(1, 2) with
        {
            Dex = [State(1, 2).Dex[0] with { CaughtAt = caughtAt }],
        };

        persistence.Save(state);

        Assert.Contains("\"caughtAt\":60", File.ReadAllText(StatePath));
        Assert.Equal(caughtAt, persistence.Load()!.Dex[0].CaughtAt);
    }

    [Fact]
    public void Save_OverwritesPreviousValueAtomically()
    {
        var persistence = new JsonCompanionPersistence(StatePath);
        persistence.Save(State(1, 1));

        persistence.Save(State(4, 5));

        Assert.Equal(5, persistence.Load()!.Active!.CurrentId);
        Assert.DoesNotContain(Directory.EnumerateFiles(_directory), path => path.EndsWith(".tmp"));
    }

    [Fact]
    public void MalformedJson_IsBackedUpAndReturnsFreshSignal()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, "{not json");
        var persistence = new JsonCompanionPersistence(StatePath);

        Assert.Null(persistence.Load());
        Assert.False(File.Exists(StatePath));
        Assert.True(File.Exists($"{StatePath}.corrupt"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    public void NonObjectRoot_IsBackedUp(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, json);
        var persistence = new JsonCompanionPersistence(StatePath);

        Assert.Null(persistence.Load());
        Assert.True(File.Exists($"{StatePath}.corrupt"));
    }

    [Fact]
    public void PartiallyMalformedState_UsesDefaultsWithoutCorruptBackup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, """
            {
              "usedSinceInstall": "bad",
              "active": { "baseID": 1, "pathIDs": [] },
              "language": "not-a-language",
              "inventory": false
            }
            """);
        var persistence = new JsonCompanionPersistence(StatePath);

        var state = Assert.IsType<CompanionState>(persistence.Load());

        Assert.Equal(0, state.UsedSinceInstall);
        Assert.Null(state.Active);
        Assert.Empty(state.Inventory);
        Assert.True(File.Exists(StatePath));
        Assert.False(File.Exists($"{StatePath}.corrupt"));
    }

    [Fact]
    public void MalformedDexEntry_IsDroppedButValidEntrySurvives()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, """
            {
              "dex": [
                { "baseID": 1, "finalID": 3, "chainOrder": [1,2,3], "rarity": "common" },
                { "baseID": "bad", "finalID": 6, "chainOrder": [4,5,6], "rarity": "common" }
              ]
            }
            """);

        var loaded = new JsonCompanionPersistence(StatePath).Load()!;

        Assert.Equal(1, Assert.Single(loaded.Dex).BaseId);
    }

    [Fact]
    public void MissingClaimedProviderMap_RemainsNullForMigration()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, "{}");

        Assert.Null(new JsonCompanionPersistence(StatePath).Load()!.ClaimedTodayTokensByProvider);
    }

    [Fact]
    public void Delete_RemovesSavedStateAndIsIdempotent()
    {
        var persistence = new JsonCompanionPersistence(StatePath);
        persistence.Save(State(1, 1));

        persistence.Delete();
        persistence.Delete();

        Assert.False(File.Exists(StatePath));
        Assert.Null(persistence.Load());
    }

    [Fact]
    public void InjectedPath_NeverTouchesDefaultAppData()
    {
        var persistence = new JsonCompanionPersistence(StatePath);
        persistence.Save(State(1, 1));

        Assert.Equal(Path.GetFullPath(StatePath), persistence.FilePath);
        Assert.StartsWith(Path.GetFullPath(_directory), persistence.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private static CompanionState State(int baseId, int currentId) =>
        new()
        {
            UsedSinceInstall = 42,
            Language = AppLanguage.Ko,
            Active = new MonState
            {
                BaseId = baseId,
                PathIds = [baseId, currentId],
                PlannedPathIds = [baseId, currentId],
                StageIndex = 1,
                UsedAtStage = 8,
                Rarity = PokemonRarity.Common,
                TotalForms = 2,
                IsShiny = true,
                Nature = PokemonNature.Jolly,
            },
            Dex =
            [
                new DexEntry
                {
                    BaseId = 1,
                    FinalId = 3,
                    ChainOrder = [1, 2, 3],
                    Rarity = PokemonRarity.Common,
                },
            ],
            CollectedFinals = new HashSet<string> { "1:3" },
            Inventory = new Dictionary<string, int> { ["rareCandy"] = 2 },
        };
}
