using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

internal static class PokemonNatureDisplayNames
{
    public static string GetName(PokemonNature nature, AppLanguage language)
    {
        var names = nature switch
        {
            PokemonNature.Hardy => ("노력", "Hardy", "がんばりや", "Fuerte", "Hardi", "Esforçada"),
            PokemonNature.Lonely => ("외로움", "Lonely", "さみしがり", "Huraña", "Solo", "Carente"),
            PokemonNature.Brave => ("용감", "Brave", "ゆうかん", "Audaz", "Brave", "Corajosa"),
            PokemonNature.Adamant => ("고집", "Adamant", "いじっぱり", "Firme", "Rigide", "Teimosa"),
            PokemonNature.Naughty => ("개구쟁이", "Naughty", "やんちゃ", "Pícara", "Mauvais", "Levada"),
            PokemonNature.Bold => ("대담", "Bold", "ずぶとい", "Osada", "Assuré", "Ousada"),
            PokemonNature.Docile => ("온순", "Docile", "すなお", "Dócil", "Docile", "Dócil"),
            PokemonNature.Relaxed => ("무사태평", "Relaxed", "のんき", "Plácida", "Relax", "Descontraída"),
            PokemonNature.Impish => ("장난꾸러기", "Impish", "わんぱく", "Agitada", "Malin", "Travessa"),
            PokemonNature.Lax => ("촐랑", "Lax", "のうてんき", "Floja", "Lâche", "Despreocupada"),
            PokemonNature.Timid => ("겁쟁이", "Timid", "おくびょう", "Miedosa", "Timide", "Medrosa"),
            PokemonNature.Hasty => ("성급", "Hasty", "せっかち", "Activa", "Pressé", "Apressada"),
            PokemonNature.Serious => ("성실", "Serious", "まじめ", "Seria", "Sérieux", "Séria"),
            PokemonNature.Jolly => ("명랑", "Jolly", "ようき", "Alegre", "Jovial", "Alegre"),
            PokemonNature.Naive => ("천진난만", "Naive", "むじゃき", "Ingenua", "Naïf", "Ingênua"),
            PokemonNature.Modest => ("조심", "Modest", "ひかえめ", "Modesta", "Modeste", "Modesta"),
            PokemonNature.Mild => ("의젓", "Mild", "おっとり", "Afable", "Doux", "Meiga"),
            PokemonNature.Quiet => ("냉정", "Quiet", "れいせい", "Mansa", "Discret", "Discreta"),
            PokemonNature.Bashful => ("수줍음", "Bashful", "てれや", "Tímida", "Pudique", "Tímida"),
            PokemonNature.Rash => ("덜렁", "Rash", "うっかりや", "Alocada", "Foufou", "Impulsiva"),
            PokemonNature.Calm => ("차분", "Calm", "おだやか", "Serena", "Calme", "Calma"),
            PokemonNature.Gentle => ("얌전", "Gentle", "おとなしい", "Amable", "Gentil", "Gentil"),
            PokemonNature.Sassy => ("건방", "Sassy", "なまいき", "Grosera", "Malpoli", "Atrevida"),
            PokemonNature.Careful => ("신중", "Careful", "しんちょう", "Cauta", "Prudent", "Cautelosa"),
            PokemonNature.Quirky => ("변덕", "Quirky", "きまぐれ", "Rara", "Bizarre", "Excêntrica"),
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
            _ => names.Item2,
        };
    }
}
