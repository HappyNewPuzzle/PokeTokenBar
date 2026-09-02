using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

internal static class PokemonNatureDisplayNames
{
    public static string GetName(PokemonNature nature, AppLanguage language)
    {
        var names = nature switch
        {
            PokemonNature.Hardy => ("노력", "Hardy", "がんばりや", "Fuerte", "Hardi", "Esforçada", "Robust"),
            PokemonNature.Lonely => ("외로움", "Lonely", "さみしがり", "Huraña", "Solo", "Carente", "Solo"),
            PokemonNature.Brave => ("용감", "Brave", "ゆうかん", "Audaz", "Brave", "Corajosa", "Mutig"),
            PokemonNature.Adamant => ("고집", "Adamant", "いじっぱり", "Firme", "Rigide", "Teimosa", "Hart"),
            PokemonNature.Naughty => ("개구쟁이", "Naughty", "やんちゃ", "Pícara", "Mauvais", "Levada", "Frech"),
            PokemonNature.Bold => ("대담", "Bold", "ずぶとい", "Osada", "Assuré", "Ousada", "Kühn"),
            PokemonNature.Docile => ("온순", "Docile", "すなお", "Dócil", "Docile", "Dócil", "Sanft"),
            PokemonNature.Relaxed => ("무사태평", "Relaxed", "のんき", "Plácida", "Relax", "Descontraída", "Locker"),
            PokemonNature.Impish => ("장난꾸러기", "Impish", "わんぱく", "Agitada", "Malin", "Travessa", "Pfiffig"),
            PokemonNature.Lax => ("촐랑", "Lax", "のうてんき", "Floja", "Lâche", "Despreocupada", "Lasch"),
            PokemonNature.Timid => ("겁쟁이", "Timid", "おくびょう", "Miedosa", "Timide", "Medrosa", "Scheu"),
            PokemonNature.Hasty => ("성급", "Hasty", "せっかち", "Activa", "Pressé", "Apressada", "Hastig"),
            PokemonNature.Serious => ("성실", "Serious", "まじめ", "Seria", "Sérieux", "Séria", "Ernst"),
            PokemonNature.Jolly => ("명랑", "Jolly", "ようき", "Alegre", "Jovial", "Alegre", "Froh"),
            PokemonNature.Naive => ("천진난만", "Naive", "むじゃき", "Ingenua", "Naïf", "Ingênua", "Naiv"),
            PokemonNature.Modest => ("조심", "Modest", "ひかえめ", "Modesta", "Modeste", "Modesta", "Mäßig"),
            PokemonNature.Mild => ("의젓", "Mild", "おっとり", "Afable", "Doux", "Meiga", "Mild"),
            PokemonNature.Quiet => ("냉정", "Quiet", "れいせい", "Mansa", "Discret", "Discreta", "Ruhig"),
            PokemonNature.Bashful => ("수줍음", "Bashful", "てれや", "Tímida", "Pudique", "Tímida", "Zaghaft"),
            PokemonNature.Rash => ("덜렁", "Rash", "うっかりや", "Alocada", "Foufou", "Impulsiva", "Hitzig"),
            PokemonNature.Calm => ("차분", "Calm", "おだやか", "Serena", "Calme", "Calma", "Still"),
            PokemonNature.Gentle => ("얌전", "Gentle", "おとなしい", "Amable", "Gentil", "Gentil", "Zart"),
            PokemonNature.Sassy => ("건방", "Sassy", "なまいき", "Grosera", "Malpoli", "Atrevida", "Forsch"),
            PokemonNature.Careful => ("신중", "Careful", "しんちょう", "Cauta", "Prudent", "Cautelosa", "Sacht"),
            PokemonNature.Quirky => ("변덕", "Quirky", "きまぐれ", "Rara", "Bizarre", "Excêntrica", "Kauzig"),
            _ => throw new ArgumentOutOfRangeException(nameof(nature)),
        };

        return language switch
        {
            AppLanguage.Ko => names.Item1,
            AppLanguage.En => names.Item2,
            AppLanguage.Ja => names.Item3,
            AppLanguage.Es => names.Item4,
            AppLanguage.Fr => names.Item5,
            AppLanguage.Pt => names.Item6,
            AppLanguage.De => names.Item7,
            _ => names.Item2,
        };
    }
}
