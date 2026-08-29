using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class CompanionModelsTests
{
    [Fact]
    public void FreshState_IsAnEggWithoutADefaultPokemon()
    {
        var state = new CompanionState();

        Assert.Null(state.Active);
        Assert.Null(state.RepresentativeSpeciesId);
        Assert.Empty(state.Dex);
        Assert.Empty(state.CollectedFinals);
    }

    [Fact]
    public void BaseSpecies_HasValueEquality()
    {
        Assert.Equal(new BaseSpecies(1, 45), new BaseSpecies(1, 45));
        Assert.NotEqual(new BaseSpecies(1, 45), new BaseSpecies(2, 45));
    }

    [Theory]
    [InlineData(3, false, false, PokemonRarity.Rare)]
    [InlineData(45, false, false, PokemonRarity.Rare)]
    [InlineData(46, false, false, PokemonRarity.Uncommon)]
    [InlineData(120, false, false, PokemonRarity.Uncommon)]
    [InlineData(121, false, false, PokemonRarity.Common)]
    [InlineData(255, true, false, PokemonRarity.Legendary)]
    [InlineData(255, false, true, PokemonRarity.Legendary)]
    public void Rarity_MatchesCaptureRateRules(
        int captureRate,
        bool legendary,
        bool mythical,
        PokemonRarity expected)
    {
        Assert.Equal(expected, PokemonRarityRules.From(captureRate, legendary, mythical));
    }

    [Fact]
    public void EvoLine_FiltersSpeciesWithoutAnimatedAssets()
    {
        var line = new EvoLine(
            1,
            new EvoNode(1, [new EvoNode(2, []), new EvoNode(650, [])]),
            PokemonRarity.Common,
            new Dictionary<int, IReadOnlyDictionary<string, string>>());

        Assert.Equal([2], line.Tree.Children.Select(child => child.SpeciesId));
    }

    [Fact]
    public void LocalizedName_UsesLanguageThenEnglishThenNumber()
    {
        var line = new EvoLine(
            1,
            new EvoNode(1, []),
            PokemonRarity.Common,
            new Dictionary<int, IReadOnlyDictionary<string, string>>
            {
                [1] = new Dictionary<string, string> { ["ko"] = "이상해씨", ["en"] = "Bulbasaur" },
                [2] = new Dictionary<string, string> { ["en"] = "Ivysaur" },
            });

        Assert.Equal("이상해씨", line.LocalizedName(1, AppLanguage.Ko));
        Assert.Equal("Ivysaur", line.LocalizedName(2, AppLanguage.Ko));
        Assert.Equal("#3", line.LocalizedName(3, AppLanguage.Ko));
    }

    [Fact]
    public void MonState_CurrentIdClampsStage()
    {
        var mon = new MonState { BaseId = 1, PathIds = [1, 2, 3], StageIndex = 99 };

        Assert.Equal(3, mon.CurrentId);
    }

    [Fact]
    public void Ownership_UsesDexAndReachedActiveStagesOnly()
    {
        var state = new CompanionState
        {
            Active = new MonState
            {
                BaseId = 1,
                PathIds = [1, 2, 3],
                StageIndex = 1,
                IsShiny = true,
            },
            Dex =
            [
                new DexEntry { BaseId = 4, FinalId = 6, ChainOrder = [4, 5, 6], Rarity = PokemonRarity.Common },
            ],
        };

        Assert.True(state.OwnsSpecies(2));
        Assert.False(state.OwnsSpecies(3));
        Assert.True(state.OwnsSpecies(5));
        Assert.True(state.OwnsShinySpecies(2));
    }
}
