namespace PokeTokenBar.Windows.Core;

public enum CompanionItemKind
{
    Mint,
    RareCandy,
    ShinyCharm,
}

public static class CompanionEconomyRules
{
    public const long MintPrice = 100_000_000;
    public const long RareCandyPrice = 500_000_000;
    public const long RareCandyExperience = 100_000_000;
    public const int WeeklyCandyGrant = 5;
    public const long ShinyCharmPrice = 3_000_000_000;
    public const int ShinyCharmDenominator = 48;
    public const long FreshEggPrice = 1_000_000_000;

    public static string Key(this CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.Mint => "mint",
        CompanionItemKind.RareCandy => "rareCandy",
        CompanionItemKind.ShinyCharm => "shinyCharm",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static long Price(this CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.Mint => MintPrice,
        CompanionItemKind.RareCandy => RareCandyPrice,
        CompanionItemKind.ShinyCharm => ShinyCharmPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsPassive(this CompanionItemKind kind) =>
        kind == CompanionItemKind.ShinyCharm;

    public static IReadOnlyList<PokemonRarity?> EggTiers { get; } =
        [null, PokemonRarity.Uncommon, PokemonRarity.Rare];

    public static long EggPrice(PokemonRarity? guaranteedRarity)
    {
        if (guaranteedRarity is null)
        {
            return FreshEggPrice;
        }

        return (long)Math.Round(
            FreshEggPrice *
            ((double)PokemonBalance.GraduationTotal(guaranteedRarity.Value) /
             PokemonBalance.GraduationTotal(PokemonRarity.Common)),
            MidpointRounding.AwayFromZero);
    }

    public static bool RollsShiny(int roll, bool charmOwned) =>
        roll % (charmOwned ? ShinyCharmDenominator : PokemonOdds.ShinyDenominator) == 0;
}

public enum ShopProductKind
{
    Item,
    Egg,
}

public sealed record ShopProduct(
    string Id,
    ShopProductKind ProductKind,
    long Price,
    CompanionItemKind? ItemKind = null,
    PokemonRarity? GuaranteedRarity = null);

public sealed record InventoryStack(CompanionItemKind Kind, int Count);

public enum PurchaseResult
{
    Success,
    InsufficientFunds,
    InvalidProduct,
    AlreadyOwned,
    NotAllowed,
    PersistenceFailed,
}

public enum ItemUseResult
{
    Progressed,
    Evolved,
    Graduated,
    NatureChanged,
    Unavailable,
    PersistenceFailed,
}

public sealed record ItemUseOutcome(ItemUseResult Result, PokemonNature? Nature = null);

public enum LimitWindowClass
{
    Session,
    Weekly,
}

public sealed record CandyWindow(
    string Key,
    string Name,
    LimitWindowClass Kind,
    double Utilization);

public sealed record CandyGrant(string WindowKey, string WindowName, int Count);
