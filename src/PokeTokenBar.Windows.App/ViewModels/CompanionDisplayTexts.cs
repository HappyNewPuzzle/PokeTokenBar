using PokeTokenBar.Windows.App.Formatting;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

internal static class CompanionDisplayTexts
{
    public static string EggName(AppLanguage language) =>
        Pick(language, "Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg");

    public static string Rarity(PokemonRarity rarity, AppLanguage language) =>
        rarity switch
        {
            PokemonRarity.Common => Pick(language, "일반", "Common", "ノーマル", "Común", "Commun", "Comum", "Gewöhnlich"),
            PokemonRarity.Uncommon => Pick(language, "고급", "Uncommon", "アンコモン", "Poco común", "Peu commun", "Incomum", "Ungewöhnlich"),
            PokemonRarity.Rare => Pick(language, "희귀", "Rare", "レア", "Raro", "Rare", "Raro", "Selten"),
            PokemonRarity.Legendary => Pick(language, "전설", "Legendary", "伝説", "Legendario", "Légendaire", "Lendário", "Legendär"),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };

    public static string Stage(int stageNumber, int totalForms, bool isFinal, AppLanguage language) =>
        isFinal
            ? Pick(language, "최종 진화체", "Final form", "最終進化", "Forma final", "Forme finale", "Forma final", "Letzte Entwicklungsstufe")
            : Pick(
                language,
                $"진화 단계 {stageNumber} / {totalForms}",
                $"Stage {stageNumber} / {totalForms}",
                $"進化段階 {stageNumber} / {totalForms}",
                $"Etapa {stageNumber} / {totalForms}",
                $"Stade {stageNumber} / {totalForms}",
                $"Estágio {stageNumber} / {totalForms}",
                $"Entwicklungsstufe {stageNumber} / {totalForms}");

    public static string Progress(
        bool isEgg,
        bool isFinal,
        long remainingTokens,
        AppLanguage language)
    {
        var amount = UsageValueFormatter.Compact(remainingTokens);
        if (isEgg)
        {
            return Pick(
                language,
                $"부화까지 {amount}",
                $"{amount} to hatch",
                $"孵化まで {amount}",
                $"{amount} para eclosionar",
                $"{amount} avant l'éclosion",
                $"{amount} para chocar",
                $"{amount} bis zum Schlüpfen");
        }

        return isFinal
            ? Pick(
                language,
                $"졸업까지 {amount}",
                $"{amount} to graduation",
                $"卒業まで {amount}",
                $"{amount} para graduarse",
                $"{amount} avant le diplôme",
                $"{amount} para se formar",
                $"{amount} bis zum Abschied")
            : Pick(
                language,
                $"다음 진화까지 {amount}",
                $"{amount} to next evolution",
                $"次の進化まで {amount}",
                $"{amount} para la siguiente evolución",
                $"{amount} avant la prochaine évolution",
                $"{amount} para a próxima evolução",
                $"{amount} bis zur nächsten Entwicklung");
    }

    public static string Status(CompanionStateKind state, AppLanguage language) =>
        state switch
        {
            CompanionStateKind.Egg => Pick(language, "곧 깨어나요.", "Hatching soon.", "もうすぐ孵化します。", "Está a punto de eclosionar.", "Bientôt l'éclosion.", "Vai chocar logo.", "Schlüpft bald."),
            CompanionStateKind.Idle => Pick(language, "오늘은 조용히 자리를 지켜요.", "Keeping quiet today.", "今日は静かにしています。", "Hoy se mantiene tranquilo.", "Tranquille aujourd'hui.", "Hoje está quietinho.", "Ist heute ganz ruhig."),
            CompanionStateKind.Working => Pick(language, "오늘의 작업 흔적이 쌓이고 있어요.", "Today's work is piling up.", "本日の作業が積み重なっています。", "El trabajo de hoy se va acumulando.", "Le travail du jour s'accumule.", "O trabalho de hoje está se acumulando.", "Heute kommt einiges an Arbeit zusammen."),
            CompanionStateKind.Focus => Pick(language, "지금은 집중 모드예요.", "In focus mode now.", "今は集中モードです。", "Ahora está en modo concentración.", "En mode concentration.", "Agora está em modo foco.", "Gerade voll konzentriert."),
            CompanionStateKind.Tired => Pick(language, "한도에 가까워요. 잠깐 쉬어도 괜찮아요.", "Close to the limit. A short break is fine.", "上限が近いです。少し休んでも大丈夫。", "Está cerca del límite. Un pequeño descanso no vendría mal.", "Proche de la limite. Une petite pause ne fait pas de mal.", "Está perto do limite. Uma pausa cai bem.", "Fast am Limit. Eine kurze Pause tut gut."),
            CompanionStateKind.Sleep => Pick(language, "지금은 자고 있어요.", "Sleeping now.", "今は眠っています。", "Ahora está durmiendo.", "En train de dormir.", "Agora está dormindo.", "Schläft gerade."),
            CompanionStateKind.LevelUp => Pick(language, "성장했어요!", "It grew!", "成長しました！", "¡Ha crecido!", "Il a grandi !", "Cresceu!", "Ist gewachsen!"),
            _ => string.Empty,
        };

    public static string Hatching(AppLanguage language) =>
        Pick(language, "부화 중…", "Hatching…", "孵化中…", "Eclosionando…", "Éclosion…", "Chocando…", "Schlüpft…");

    private static string Pick(
        AppLanguage language,
        string ko,
        string en,
        string ja,
        string es,
        string fr,
        string pt,
        string de) =>
        language switch
        {
            AppLanguage.Ko => ko,
            AppLanguage.En => en,
            AppLanguage.Ja => ja,
            AppLanguage.Es => es,
            AppLanguage.Fr => fr,
            AppLanguage.Pt => pt,
            AppLanguage.De => de,
            _ => en,
        };
}
