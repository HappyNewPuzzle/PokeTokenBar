using System.Globalization;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Windows.Core;

public enum CompanionStateKind
{
    Egg,
    Idle,
    Working,
    Focus,
    Tired,
    Sleep,
    LevelUp,
}

public enum AppLanguage
{
    Ko,
    En,
    Ja,
    Es,
    Fr,
    Pt,
}

public static class AppLanguageRules
{
    public static AppLanguage SystemDefault
    {
        get
        {
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return code switch
            {
                "ko" => AppLanguage.Ko,
                "ja" => AppLanguage.Ja,
                "es" => AppLanguage.Es,
                "fr" => AppLanguage.Fr,
                "pt" => AppLanguage.Pt,
                _ => AppLanguage.En,
            };
        }
    }

    public static IReadOnlyList<string> ApiCodes(this AppLanguage language) =>
        language switch
        {
            AppLanguage.Ko => ["ko"],
            AppLanguage.En => ["en"],
            AppLanguage.Ja => ["ja-Hrkt", "ja"],
            AppLanguage.Es => ["es"],
            AppLanguage.Fr => ["fr"],
            AppLanguage.Pt => ["pt"],
            _ => ["en"],
        };

    public static string? ResolveName(
        this AppLanguage language,
        IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var code in language.ApiCodes())
        {
            if (names.TryGetValue(code, out var name))
            {
                return name;
            }
        }

        return names.GetValueOrDefault("en");
    }
}

public enum PokemonRarity
{
    Common,
    Uncommon,
    Rare,
    Legendary,
}

public static class PokemonRarityRules
{
    public static int SortRank(this PokemonRarity rarity) => (int)rarity;

    public static int? CaptureRateCeiling(this PokemonRarity rarity) =>
        rarity switch
        {
            PokemonRarity.Rare => 45,
            PokemonRarity.Uncommon => 120,
            PokemonRarity.Common => 255,
            PokemonRarity.Legendary => null,
            _ => null,
        };

    public static bool Includes(this PokemonRarity rarity, int captureRate) =>
        rarity.CaptureRateCeiling() is int ceiling && captureRate <= ceiling;

    public static PokemonRarity From(
        int captureRate,
        bool isLegendary,
        bool isMythical)
    {
        if (isLegendary || isMythical)
        {
            return PokemonRarity.Legendary;
        }

        if (PokemonRarity.Rare.Includes(captureRate))
        {
            return PokemonRarity.Rare;
        }

        return PokemonRarity.Uncommon.Includes(captureRate)
            ? PokemonRarity.Uncommon
            : PokemonRarity.Common;
    }
}

public enum PokemonNature
{
    Hardy,
    Lonely,
    Brave,
    Adamant,
    Naughty,
    Bold,
    Docile,
    Relaxed,
    Impish,
    Lax,
    Timid,
    Hasty,
    Serious,
    Jolly,
    Naive,
    Modest,
    Mild,
    Quiet,
    Bashful,
    Rash,
    Calm,
    Gentle,
    Sassy,
    Careful,
    Quirky,
}

public static class PokemonAssets
{
    public const int FirstAnimatedSpeciesId = 1;
    public const int LastAnimatedSpeciesId = 649;

    public static bool HasAnimatedSprite(int speciesId) =>
        speciesId is >= FirstAnimatedSpeciesId and <= LastAnimatedSpeciesId;
}

public static class PokemonOdds
{
    public const int ShinyDenominator = 64;
    public const int DittoDisguiseDenominator = 128;
    public const int DittoSpeciesId = 132;
}

public sealed record BaseSpecies(int Id, int CaptureRate);

public sealed record EvoNode(int SpeciesId, IReadOnlyList<EvoNode> Children)
{
    public int Depth => 1 + (Children.Count == 0 ? 0 : Children.Max(child => child.Depth));

    public EvoNode? Find(int speciesId)
    {
        if (SpeciesId == speciesId)
        {
            return this;
        }

        foreach (var child in Children)
        {
            var found = child.Find(speciesId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public IReadOnlyList<int> FinalIds =>
        Children.Count == 0
            ? [SpeciesId]
            : Children.SelectMany(child => child.FinalIds).ToArray();

    public EvoNode? KeepingAnimatedSprites()
    {
        if (!PokemonAssets.HasAnimatedSprite(SpeciesId))
        {
            return null;
        }

        return new EvoNode(
            SpeciesId,
            Children
                .Select(child => child.KeepingAnimatedSprites())
                .Where(child => child is not null)
                .Cast<EvoNode>()
                .ToArray());
    }
}

public sealed record EvoLine
{
    public EvoLine(
        int baseId,
        EvoNode tree,
        PokemonRarity rarity,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> names)
    {
        BaseId = baseId;
        Tree = tree.KeepingAnimatedSprites() ?? new EvoNode(baseId, []);
        Rarity = rarity;
        Names = names;
    }

    public int BaseId { get; }
    public EvoNode Tree { get; }
    public PokemonRarity Rarity { get; }
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Names { get; }
    public int TotalForms => Tree.Depth;

    public string LocalizedName(int speciesId, AppLanguage language) =>
        Names.TryGetValue(speciesId, out var names)
            ? language.ResolveName(names) ?? $"#{speciesId}"
            : $"#{speciesId}";
}

public sealed record MonState
{
    [JsonPropertyName("baseID")]
    public int BaseId { get; init; }

    [JsonPropertyName("pathIDs")]
    public IReadOnlyList<int> PathIds { get; init; } = Array.Empty<int>();

    [JsonPropertyName("plannedPathIDs")]
    public IReadOnlyList<int> PlannedPathIds { get; init; } = Array.Empty<int>();

    public int StageIndex { get; init; }
    public long UsedAtStage { get; init; }
    public PokemonRarity Rarity { get; init; }
    public int TotalForms { get; init; }
    public bool IsShiny { get; init; }
    public PokemonNature? Nature { get; init; }
    public int? DittoDisguise { get; init; }
    public bool DittoRevealed { get; init; }

    [JsonIgnore]
    public int CurrentId =>
        PathIds.Count == 0
            ? BaseId
            : PathIds[Math.Clamp(StageIndex, 0, PathIds.Count - 1)];
}

public sealed record DexEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("baseID")]
    public int BaseId { get; init; }

    [JsonPropertyName("finalID")]
    public int FinalId { get; init; }

    public IReadOnlyList<int> ChainOrder { get; init; } = Array.Empty<int>();
    public PokemonRarity Rarity { get; init; }
    public DateTimeOffset? CaughtAt { get; init; }
    public bool IsShiny { get; init; }
    public PokemonNature? Nature { get; init; }
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>? Names { get; init; }
}

public sealed record CompanionState
{
    public bool InstallBaselineSet { get; init; }
    public long UsedSinceInstall { get; init; }
    public long SpentTokens { get; init; }
    public long EggUsage { get; init; }
    public PokemonRarity? EggTier { get; init; }

    [JsonPropertyName("pendingHatchID")]
    public int? PendingHatchId { get; init; }

    public IReadOnlyDictionary<string, long>? ClaimedTodayTokensByProvider { get; init; }
    public string LastDate { get; init; } = string.Empty;
    public MonState? Active { get; init; }

    [JsonPropertyName("representativeSpeciesID")]
    public int? RepresentativeSpeciesId { get; init; }

    public IReadOnlyList<DexEntry> Dex { get; init; } = Array.Empty<DexEntry>();
    public IReadOnlySet<string> CollectedFinals { get; init; } = new HashSet<string>();
    public AppLanguage Language { get; init; } = AppLanguageRules.SystemDefault;
    public IReadOnlyDictionary<string, int> Inventory { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> CandyGrantTier { get; init; } = new Dictionary<string, int>();
    public bool CandyFeatureSeeded { get; init; }

    public bool OwnsSpecies(int speciesId)
    {
        if (Dex.Any(entry => entry.ChainOrder.Contains(speciesId)))
        {
            return true;
        }

        if (Active is null || Active.PathIds.Count == 0)
        {
            return false;
        }

        return Active.PathIds
            .Take(Math.Min(Active.StageIndex + 1, Active.PathIds.Count))
            .Contains(speciesId);
    }

    public bool OwnsShinySpecies(int speciesId)
    {
        if (Dex.Any(entry => entry.IsShiny && entry.ChainOrder.Contains(speciesId)))
        {
            return true;
        }

        if (Active is null || !Active.IsShiny ||
            !Active.PathIds.Take(Math.Min(Active.StageIndex + 1, Active.PathIds.Count)).Contains(speciesId))
        {
            return false;
        }

        return Active.DittoDisguise is null || Active.DittoRevealed;
    }
}

public readonly record struct RepresentativeSubject(int? SpeciesId, bool IsShiny);
