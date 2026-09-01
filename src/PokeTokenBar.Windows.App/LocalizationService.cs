using System.ComponentModel;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private AppLanguage _language;

    public LocalizationService(AppLanguage language) => _language = language;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public string Home => T("홈", "Home", "ホーム", "Inicio", "Accueil", "Início", "Start");
    public string Shop => T("상점", "Shop", "ショップ", "Tienda", "Boutique", "Loja", "Shop");
    public string Bag => T("가방", "Bag", "バッグ", "Bolsa", "Sac", "Bolsa", "Beutel");
    public string Collection => T("컬렉션", "Collection", "コレクション", "Colección", "Collection", "Coleção", "Sammlung");
    public string Settings => T("설정", "Settings", "設定", "Ajustes", "Réglages", "Configurações", "Einstellungen");
    public string Companion => T("컴패니언", "Companion", "コンパニオン", "Compañero", "Compagnon", "Companheiro", "Begleiter");
    public string NoUsageData => T("사용량 데이터 없음", "No usage data", "使用量データなし", "Sin datos de uso", "Aucune donnée", "Sem dados de uso", "Keine Nutzungsdaten");
    public string Refresh => T("새로고침", "Refresh", "更新", "Actualizar", "Actualiser", "Atualizar", "Aktualisieren");
    public string Refreshing => T("새로고침 중…", "Refreshing…", "更新中…", "Actualizando…", "Actualisation…", "Atualizando…", "Wird aktualisiert…");
    public string Usage => T("사용량", "Usage", "使用量", "Uso", "Utilisation", "Uso", "Nutzung");
    public string Today => T("오늘", "Today", "今日", "Hoy", "Aujourd’hui", "Hoje", "Heute");
    public string RecentFiveHours => T("최근 5시간", "Recent 5 hours", "直近5時間", "Últimas 5 horas", "5 dernières heures", "Últimas 5 horas", "Letzte 5 Stunden");
    public string ThisWeek => T("이번 주", "This week", "今週", "Esta semana", "Cette semaine", "Esta semana", "Diese Woche");
    public string ThisMonth => T("이번 달", "This month", "今月", "Este mes", "Ce mois", "Este mês", "Dieser Monat");
    public string Input => T("입력", "Input", "入力", "Entrada", "Entrée", "Entrada", "Eingabe");
    public string Output => T("출력", "Output", "出力", "Salida", "Sortie", "Saída", "Ausgabe");
    public string Cache => T("캐시", "Cache", "キャッシュ", "Caché", "Cache", "Cache", "Cache");
    public string Cost => T("비용", "Cost", "コスト", "Coste", "Coût", "Custo", "Kosten");
    public string OfficialLimits => T("공식 한도", "Limits (official)", "公式上限", "Límites oficiales", "Limites officielles", "Limites oficiais", "Offizielle Limits");
    public string FiveHourSession => T("5시간 세션", "5-hour session", "5時間セッション", "Sesión de 5 horas", "Session de 5 heures", "Sessão de 5 horas", "5-Stunden-Sitzung");
    public string Weekly => T("주간", "Weekly", "週間", "Semanal", "Hebdomadaire", "Semanal", "Wöchentlich");
    public string ShowFloating => T("플로팅 Pokémon 표시", "Show Floating Pokémon", "フローティングPokémonを表示", "Mostrar Pokémon flotante", "Afficher le Pokémon flottant", "Mostrar Pokémon flutuante", "Schwebendes Pokémon anzeigen");
    public string LaunchAtStartup => T("시작 시 실행", "Launch at startup", "起動時に実行", "Iniciar al arrancar", "Lancer au démarrage", "Iniciar com o sistema", "Beim Start ausführen");
    public string RefreshInterval => T("새로고침 간격", "Refresh interval", "更新間隔", "Intervalo de actualización", "Intervalle d’actualisation", "Intervalo de atualização", "Aktualisierungsintervall");
    public string ResetFloatingPosition => T("플로팅 위치 초기화", "Reset Floating Position", "位置をリセット", "Restablecer posición", "Réinitialiser la position", "Redefinir posição", "Position zurücksetzen");
    public string LanguageLabel => T("언어", "Language", "言語", "Idioma", "Langue", "Idioma", "Sprache");
    public string Notifications => T("알림", "Notifications", "通知", "Notificaciones", "Notifications", "Notificações", "Benachrichtigungen");
    public string LimitNotifications => T("한도 알림", "Limit notifications", "上限通知", "Avisos de límite", "Alertes de limite", "Alertas de limite", "Limit-Warnungen");
    public string CompanionNotifications => T("컴패니언 이벤트 알림", "Companion event notifications", "コンパニオン通知", "Avisos del compañero", "Notifications du compagnon", "Alertas do companheiro", "Begleiter-Ereignisse");
    public string Warning => T("경고", "Warning", "警告", "Aviso", "Avertissement", "Aviso", "Warnung");
    public string Critical => T("임박", "Critical", "切迫", "Crítico", "Critique", "Crítico", "Kritisch");
    public string LimitDisplay => T("한도 표시", "Limit display", "上限の表示", "Visualización del límite", "Affichage de la limite", "Exibição do limite", "Limit-Anzeige");
    public string Used => T("사용량", "Used", "使用量", "Usado", "Utilisé", "Usado", "Verbraucht");
    public string Remaining => T("남은 양", "Remaining", "残量", "Restante", "Restant", "Restante", "Verbleibend");
    public string FloatingSize => T("플로팅 크기", "Floating size", "フローティングサイズ", "Tamaño flotante", "Taille flottante", "Tamanho flutuante", "Schwebende Größe");
    public string AnimationQuality => T("애니메이션 품질", "Animation quality", "アニメーション品質", "Calidad de animación", "Qualité d’animation", "Qualidade da animação", "Animationsqualität");
    public string PowerSaver => T("절전", "Power saver", "省電力", "Ahorro", "Économie", "Economia", "Energiesparen");
    public string Balanced => T("균형", "Balanced", "バランス", "Equilibrado", "Équilibré", "Equilibrado", "Ausgeglichen");
    public string Smooth => T("부드럽게", "Smooth", "スムーズ", "Suave", "Fluide", "Suave", "Flüssig");
    public string BubbleAlerts => T("말풍선으로 한도 알림", "Show limit alerts as bubbles", "上限通知を吹き出しで表示", "Mostrar alertas en globos", "Afficher les alertes en bulles", "Mostrar alertas em balões", "Limit-Warnungen als Sprechblase");
    public string ProviderRoots => T("프로바이더 스캔 폴더", "Provider scan folders", "プロバイダースキャンフォルダ", "Carpetas del proveedor", "Dossiers du fournisseur", "Pastas do provedor", "Provider-Scanordner");
    public string CustomRootsHint => T("기본 폴더에 추가됩니다. 한 줄에 하나씩 입력하세요.", "Added to default folders. Enter one path per line.", "既定フォルダに追加。1行に1つ。", "Se añaden a las carpetas predeterminadas. Una ruta por línea.", "Ajoutés aux dossiers par défaut. Un chemin par ligne.", "Adicionados às pastas padrão. Um caminho por linha.", "Zusätzlich zu Standardordnern. Ein Pfad pro Zeile.");
    public string Balance => T("잔액", "Balance", "残高", "Saldo", "Solde", "Saldo", "Guthaben");
    public string Buy => T("구매", "Buy", "購入", "Comprar", "Acheter", "Comprar", "Kaufen");
    public string Use => T("사용", "Use", "使う", "Usar", "Utiliser", "Usar", "Verwenden");
    public string FollowCurrent => T("현재 Pokémon 따라가기", "Follow current", "現在のPokémonを追従", "Seguir al actual", "Suivre l’actuel", "Seguir o atual", "Aktuellem folgen");
    public string Represent => T("대표로 설정", "Represent", "代表に設定", "Representar", "Représenter", "Representar", "Als Vertreter");
    public string Open => T("열기", "Open", "開く", "Abrir", "Ouvrir", "Abrir", "Öffnen");
    public string Exit => T("종료", "Exit", "終了", "Salir", "Quitter", "Sair", "Beenden");
    public string HideFloating => T("플로팅 Pokémon 숨기기", "Hide Floating Pokémon", "フローティングPokémonを隠す", "Ocultar Pokémon flotante", "Masquer le Pokémon flottant", "Ocultar Pokémon flutuante", "Schwebendes Pokémon ausblenden");
    public string NotificationWarningTitle => T("한도 경고", "Limit warning", "上限警告", "Aviso de límite", "Alerte de limite", "Aviso de limite", "Limit-Warnung");
    public string NotificationCriticalTitle => T("한도 임박", "Limit critical", "上限切迫", "Límite crítico", "Limite critique", "Limite crítico", "Limit kritisch");
    public string HatchTitle => T("부화!", "Hatched!", "孵化！", "¡Eclosionó!", "Éclosion !", "Chocou!", "Geschlüpft!");
    public string EvolutionTitle => T("진화!", "Evolution!", "進化！", "¡Evolución!", "Évolution !", "Evolução!", "Entwicklung!");
    public string GraduationTitle => T("졸업!", "Graduation!", "卒業！", "¡Graduación!", "Diplôme !", "Graduação!", "Abschluss!");
    public string RewardTitle => T("보상 획득", "Reward earned", "報酬獲得", "Recompensa obtenida", "Récompense obtenue", "Recompensa recebida", "Belohnung erhalten");
    public string CompanionEventBody(int? speciesId) => speciesId is int id
        ? $"Pokémon #{id}"
        : "PokeTokenBar";
    public string RewardBody(int count) => T($"희귀사탕 ×{count}", $"Rare Candy ×{count}", $"ふしぎなアメ ×{count}", $"Caramelo Raro ×{count}", $"Super Bonbon ×{count}", $"Doce Raro ×{count}", $"Sonderbonbon ×{count}");
    public string PercentUsed(string window, double percent) => T(
        $"{window}: {percent:0}% 사용", $"{window}: {percent:0}% used", $"{window}: {percent:0}% 使用",
        $"{window}: {percent:0}% usado", $"{window} : {percent:0}% utilisé", $"{window}: {percent:0}% usado", $"{window}: {percent:0}% verbraucht");

    private string T(string ko, string en, string ja, string es, string fr, string pt, string de) =>
        Language switch
        {
            AppLanguage.Ko => ko,
            AppLanguage.Ja => ja,
            AppLanguage.Es => es,
            AppLanguage.Fr => fr,
            AppLanguage.Pt => pt,
            AppLanguage.De => de,
            _ => en,
        };
}
