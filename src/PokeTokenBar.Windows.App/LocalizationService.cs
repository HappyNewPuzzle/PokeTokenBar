using System.ComponentModel;
using System.Globalization;
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
    public string Session => T("세션", "session", "セッション", "sesión", "session", "sessão", "Sitzung");
    public string ClaudeFiveHour => T("Claude 5시간 세션", "Claude 5-hour session", "Claude 5時間セッション", "Sesión de 5 horas de Claude", "Session de 5 h de Claude", "Sessão de 5 horas do Claude", "Claude-5-Stunden-Sitzung");
    public string ClaudeWeekly => T("Claude 주간", "Claude weekly", "Claude 週間", "Semanal de Claude", "Claude hebdo", "Semanal do Claude", "Claude – wöchentlich");
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
    public string DittoRevealTitle => T("메타몽!", "Ditto revealed!", "メタモン！", "¡Era Ditto!", "C'était Métamorph !", "Era Ditto!", "Ditto enthüllt!");
    public string ShinyDittoRevealTitle => T("이로치 메타몽!", "Shiny Ditto revealed!", "色違いのメタモン！", "¡Era un Ditto variocolor!", "C'était un Métamorph chromatique !", "Era um Ditto brilhante!", "Schillerndes Ditto enthüllt!");
    public string GraduationTitle => T("졸업!", "Graduation!", "卒業！", "¡Graduación!", "Diplôme !", "Graduação!", "Abschluss!");
    public string RewardTitle => T("보상 획득", "Reward earned", "報酬獲得", "Recompensa obtenida", "Récompense obtenue", "Recompensa recebida", "Belohnung erhalten");
    public string CompanionEventBody(int? speciesId) => speciesId is int id
        ? $"Pokémon #{id}"
        : "PokeTokenBar";
    public string DittoRevealBody(int? disguiseSpeciesId) => disguiseSpeciesId is int id
        ? T(
            $"Pokémon #{id}의 정체는 메타몽이었어요!",
            $"Pokémon #{id} was Ditto in disguise!",
            $"Pokémon #{id}の正体はメタモンでした！",
            $"¡Pokémon #{id} era Ditto disfrazado!",
            $"Le Pokémon #{id} était Métamorph déguisé !",
            $"Pokémon #{id} era um Ditto disfarçado!",
            $"Pokémon #{id} war ein verkleidetes Ditto!")
        : CompanionEventBody(PokemonOdds.DittoSpeciesId);
    public string RewardBody(int count) => T($"희귀사탕 ×{count}", $"Rare Candy ×{count}", $"ふしぎなアメ ×{count}", $"Caramelo Raro ×{count}", $"Super Bonbon ×{count}", $"Doce Raro ×{count}", $"Sonderbonbon ×{count}");
    public string PercentUsed(string window, double percent) => T(
        $"{window}: {percent:0}% 사용", $"{window}: {percent:0}% used", $"{window}: {percent:0}% 使用",
        $"{window}: {percent:0}% usado", $"{window} : {percent:0}% utilisé", $"{window}: {percent:0}% usado", $"{window}: {percent:0}% verbraucht");

    public string ProviderStatus => T("프로바이더 상태", "Provider status", "プロバイダー状態", "Estado del proveedor", "État du fournisseur", "Status do provedor", "Providerstatus");
    public string Authentication => T("인증", "Authentication", "認証", "Autenticación", "Authentification", "Autenticação", "Authentifizierung");
    public string CredentialAccess => T("자격 증명 접근", "Credential access", "資格情報へのアクセス", "Acceso a credenciales", "Accès aux identifiants", "Acesso às credenciais", "Zugriff auf Anmeldedaten");
    public string CredentialAccessHint => T("Claude 및 Antigravity 공식 한도 자격 증명을 읽습니다.", "Read credentials for Claude and Antigravity official limits.", "Claude と Antigravity の公式上限用資格情報を読み取ります。", "Lee credenciales para los límites oficiales de Claude y Antigravity.", "Lit les identifiants pour les limites officielles Claude et Antigravity.", "Lê credenciais para os limites oficiais do Claude e Antigravity.", "Liest Anmeldedaten für offizielle Claude- und Antigravity-Limits.");
    public string RefreshCredentials => T("한도 자격 증명 새로 고침", "Refresh limit credentials", "上限の資格情報を更新", "Actualizar credenciales de límites", "Actualiser les identifiants des limites", "Atualizar credenciais de limites", "Limit-Anmeldedaten aktualisieren");
    public string Ready => T("준비됨", "Ready", "準備完了", "Listo", "Prêt", "Pronto", "Bereit");
    public string NotInstalled => T("설치되지 않음", "Not installed", "未インストール", "No instalado", "Non installé", "Não instalado", "Nicht installiert");
    public string NoSessions => T("세션 없음", "No sessions", "セッションなし", "Sin sesiones", "Aucune session", "Sem sessões", "Keine Sitzungen");
    public string AuthenticationRequired => T("인증 필요", "Authentication required", "認証が必要", "Autenticación requerida", "Authentification requise", "Autenticação necessária", "Authentifizierung erforderlich");
    public string LocalDataOnly => T("로컬 데이터만", "Local data only", "ローカルデータのみ", "Solo datos locales", "Données locales uniquement", "Somente dados locais", "Nur lokale Daten");
    public string QuotaUnavailable => T("한도 사용 불가", "Quota unavailable", "クォータ利用不可", "Cuota no disponible", "Quota indisponible", "Cota indisponível", "Kontingent nicht verfügbar");
    public string Error => T("오류", "Error", "エラー", "Error", "Erreur", "Erro", "Fehler");
    public string Stale => T("오래된 데이터", "Stale", "古いデータ", "Desactualizado", "Obsolète", "Desatualizado", "Veraltet");
    public string Authenticated => T("인증됨", "Authenticated", "認証済み", "Autenticado", "Authentifié", "Autenticado", "Authentifiziert");
    public string NotApplicable => T("해당 없음", "Not applicable", "対象外", "No aplicable", "Sans objet", "Não aplicável", "Nicht zutreffend");
    public string Credits => T("크레딧", "Credits", "クレジット", "Créditos", "Crédits", "Créditos", "Guthaben");
    public string Spend => T("지출", "Spend", "支出", "Gasto", "Dépenses", "Gasto", "Ausgaben");
    public string BurnRate => T("소진 속도", "Burn rate", "消費速度", "Tasa de consumo", "Taux de consommation", "Taxa de consumo", "Verbrauchsrate");
    public string Forecast => T("예측", "Forecast", "予測", "Previsión", "Prévision", "Previsão", "Prognose");
    public string NoProjection => T("예측 없음", "No projection", "予測なし", "Sin proyección", "Aucune projection", "Sem projeção", "Keine Prognose");
    public string PersonalSpendLimit => T("개인 사용 한도", "Personal spend limit", "個人利用上限", "Límite de gasto personal", "Limite de dépense personnelle", "Limite de gasto pessoal", "Persönliches Ausgabenlimit");
    public string Plan => T("플랜", "Plan", "プラン", "Plan", "Forfait", "Plano", "Tarif");
    public string LimitReached => T("한도 도달", "Limit reached", "上限到達", "Límite alcanzado", "Limite atteinte", "Limite atingido", "Limit erreicht");
    public string Limit => T("한도", "Limit", "上限", "Límite", "Limite", "Limite", "Limit");
    public string CustomRootConfigured => T("사용자 폴더 설정됨", "Custom root configured", "カスタムフォルダー設定済み", "Carpeta personalizada configurada", "Dossier personnalisé configuré", "Pasta personalizada configurada", "Benutzerordner konfiguriert");
    public string DefaultRoots => T("기본 폴더", "Default folders", "既定フォルダー", "Carpetas predeterminadas", "Dossiers par défaut", "Pastas padrão", "Standardordner");

    public string RuntimeStatus(ProviderRuntimeStatus status) => status switch
    {
        ProviderRuntimeStatus.Ready => Ready,
        ProviderRuntimeStatus.NoSessions => NoSessions,
        ProviderRuntimeStatus.LocalDataOnly => LocalDataOnly,
        ProviderRuntimeStatus.Error => Error,
        ProviderRuntimeStatus.Stale => Stale,
        _ => Error,
    };

    public string AuthStatus(ProviderAuthStatus status) => status switch
    {
        ProviderAuthStatus.Authenticated => Authenticated,
        ProviderAuthStatus.QuotaUnavailable => QuotaUnavailable,
        _ => NotApplicable,
    };

    public string HourWindow(int hours) => T($"{hours}시간", $"{hours}-hour", $"{hours}時間", $"{hours} horas", $"{hours} heures", $"{hours} horas", $"{hours} Stunden");
    public string MinuteWindow(int minutes) => T($"{minutes}분", $"{minutes}-minute", $"{minutes}分", $"{minutes} minutos", $"{minutes} minutes", $"{minutes} minutos", $"{minutes} Minuten");

    public string About => T("정보", "About", "情報", "Acerca de", "À propos", "Sobre", "Info");
    public string UpdateNotifications => T("업데이트 알림", "Update notifications", "更新通知", "Avisos de actualización", "Notifications de mise à jour", "Avisos de atualização", "Update-Benachrichtigungen");
    public string CheckForUpdates => T("업데이트 확인", "Check for updates", "アップデートを確認", "Buscar actualizaciones", "Rechercher des mises à jour", "Buscar atualizações", "Nach Updates suchen");
    public string DownloadUpdate => T("릴리스 페이지 열기", "Open release page", "リリースページを開く", "Abrir página de versión", "Ouvrir la page de version", "Abrir página da versão", "Release-Seite öffnen");
    public string Later => T("이 버전 건너뛰기", "Skip this version", "このバージョンをスキップ", "Omitir esta versión", "Ignorer cette version", "Ignorar esta versão", "Diese Version überspringen");
    public string ExportSave => T("세이브 내보내기", "Export save", "セーブを書き出す", "Exportar partida", "Exporter la sauvegarde", "Exportar save", "Spielstand exportieren");
    public string ImportSave => T("세이브 불러오기", "Import save", "セーブを読み込む", "Importar partida", "Importer la sauvegarde", "Importar save", "Spielstand importieren");
    public string CopyDiagnostics => T("진단 정보 복사", "Copy diagnostics", "診断情報をコピー", "Copiar diagnóstico", "Copier le diagnostic", "Copiar diagnóstico", "Diagnose kopieren");

    public string LastUpdated => T("마지막 갱신", "Last updated", "最終更新", "Última actualización", "Dernière actualisation", "Última atualização", "Zuletzt aktualisiert");
    public string CacheWrite => T("캐시 쓰기", "Cache write", "キャッシュ書き込み", "Escritura de caché", "Écriture cache", "Gravação de cache", "Cache-Schreibvorgänge");
    public string CacheRead => T("캐시 읽기", "Cache read", "キャッシュ読み取り", "Lectura de caché", "Lecture cache", "Leitura de cache", "Cache-Lesevorgänge");
    public string UsagePeriods => T("사용 기간", "Usage periods", "使用期間", "Períodos de uso", "Périodes d’utilisation", "Períodos de uso", "Nutzungszeiträume");
    public string ShopIntro => T("사용한 토큰으로 아이템을 살 수 있어요. 구매해도 성장량은 줄지 않아요.", "Spend the tokens you've used on items. Purchases do not reduce growth.", "使ったトークンでアイテムを購入できます。購入しても成長量は減りません。", "Usa los tokens consumidos para comprar objetos. Las compras no reducen el progreso.", "Dépense les tokens consommés en objets. Les achats ne réduisent pas la progression.", "Compre itens com os tokens usados. As compras não reduzem o progresso.", "Kaufe Gegenstände mit deinen verbrauchten Tokens. Käufe verringern den Fortschritt nicht.");
    public string BagIntro => T("아이템은 앱을 다시 시작해도 유지돼요.", "Items persist across restarts.", "アイテムは再起動後も保持されます。", "Los objetos se conservan al reiniciar.", "Les objets sont conservés après redémarrage.", "Os itens continuam disponíveis após reiniciar.", "Gegenstände bleiben nach einem Neustart erhalten.");
    public string Active => T("적용 중", "Active", "適用中", "Activo", "Actif", "Ativo", "Aktiv");
    public string Current => T("현재", "Current", "現在", "Actual", "Actuel", "Atual", "Aktuell");
    public string Representative => T("대표", "Representative", "代表", "Representante", "Représentatif", "Representante", "Repräsentativ");
    public string Caught => T("포획", "Caught", "捕獲済み", "Capturado", "Capturé", "Capturado", "Gefangen");
    public string Shiny => T("이로치", "Shiny", "色違い", "Variocolor", "Chromatique", "Shiny", "Schillernd");
    public string Normal => T("일반", "Normal", "通常", "Normal", "Normal", "Normal", "Normal");
    public string UnknownNature => T("성격 미상", "Unknown nature", "性格不明", "Naturaleza desconocida", "Nature inconnue", "Natureza desconhecida", "Unbekanntes Wesen");
    public string TokenEgg => T("Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg", "Token Egg");
    public string Mint => T("민트", "Mint", "ミント", "Menta", "Menthe", "Menta", "Minze");
    public string RareCandy => T("이상한 사탕", "Rare Candy", "ふしぎなアメ", "Caramelo Raro", "Super Bonbon", "Doce Raro", "Sonderbonbon");
    public string ShinyCharm => T("이로치 부적", "Shiny Charm", "ひかるおまもり", "Amuleto Iris", "Charme Chroma", "Amuleto Shiny", "Schillerpin");
    public string FreshEgg => T("포켓몬 알", "Pokémon Egg", "ポケモンのタマゴ", "Huevo Pokémon", "Œuf Pokémon", "Ovo Pokémon", "Pokémon-Ei");
    public string Tokens(long value) => T($"{value:N0} 토큰", $"{value:N0} tokens", $"{value:N0}トークン", $"{value:N0} tokens", $"{value:N0} tokens", $"{value:N0} tokens", $"{value:N0} Tokens");
    public string RarityEgg(PokemonRarity rarity) => rarity switch
    {
        PokemonRarity.Uncommon => T("고급 알", "Uncommon Egg", "アンコモンのタマゴ", "Huevo poco común", "Œuf peu commun", "Ovo incomum", "Ungewöhnliches Ei"),
        PokemonRarity.Rare => T("희귀 알", "Rare Egg", "レアのタマゴ", "Huevo raro", "Œuf rare", "Ovo raro", "Seltenes Ei"),
        PokemonRarity.Legendary => T("전설 알", "Legendary Egg", "でんせつのタマゴ", "Huevo legendario", "Œuf légendaire", "Ovo lendário", "Legendäres Ei"),
        _ => FreshEgg,
    };
    public string Purchased(string name) => T($"{name} 구매 완료.", $"Purchased {name}.", $"{name}を購入しました。", $"Has comprado {name}.", $"{name} acheté.", $"{name} comprado.", $"{name} gekauft.");
    public string NotEnoughTokens => T("토큰이 부족해요.", "Not enough tokens.", "トークンが足りません。", "No tienes suficientes tokens.", "Pas assez de tokens.", "Tokens insuficientes.", "Nicht genug Tokens.");
    public string AlreadyOwned => T("이미 보유 중이에요.", "Already owned.", "すでに所持しています。", "Ya lo tienes.", "Déjà possédé.", "Você já tem este item.", "Bereits im Beutel.");
    public string PurchaseUnavailable => T("지금은 구매할 수 없어요.", "This purchase is not available now.", "今は購入できません。", "Esta compra no está disponible ahora.", "Cet achat n’est pas disponible maintenant.", "Esta compra não está disponível agora.", "Dieser Kauf ist derzeit nicht verfügbar.");
    public string PurchaseSaveFailed => T("구매를 저장하지 못했어요.", "Could not save the purchase.", "購入を保存できませんでした。", "No se pudo guardar la compra.", "Impossible d’enregistrer l’achat.", "Não foi possível salvar a compra.", "Der Kauf konnte nicht gespeichert werden.");
    public string UnknownProduct => T("알 수 없는 상품이에요.", "Unknown product.", "不明な商品です。", "Producto desconocido.", "Produit inconnu.", "Produto desconhecido.", "Unbekanntes Produkt.");
    public string ProgressIncreased => T("성장량이 올랐어요.", "Progress increased.", "成長しました。", "El progreso aumentó.", "La progression a augmenté.", "O progresso aumentou.", "Der Fortschritt ist gestiegen.");
    public string PokemonEvolved => T("포켓몬이 진화했어요.", "Your Pokémon evolved.", "ポケモンが進化しました。", "Tu Pokémon evolucionó.", "Ton Pokémon a évolué.", "Seu Pokémon evoluiu.", "Dein Pokémon hat sich entwickelt.");
    public string PokemonGraduated => T("포켓몬이 졸업했어요.", "Your Pokémon graduated.", "ポケモンが卒業しました。", "Tu Pokémon se graduó.", "Ton Pokémon a été diplômé.", "Seu Pokémon se formou.", "Dein Pokémon wurde verabschiedet.");
    public string NatureChanged(string nature) => T($"성격이 {nature}(으)로 바뀌었어요.", $"Nature changed to {nature}.", $"性格が{nature}に変わりました。", $"La naturaleza cambió a {nature}.", $"La nature est devenue {nature}.", $"A natureza mudou para {nature}.", $"Das Wesen wurde zu {nature} geändert.");
    public string ItemSaveFailed => T("아이템 사용을 저장하지 못했어요.", "Could not save the item use.", "アイテムの使用を保存できませんでした。", "No se pudo guardar el uso del objeto.", "Impossible d’enregistrer l’utilisation de l’objet.", "Não foi possível salvar o uso do item.", "Die Verwendung konnte nicht gespeichert werden.");
    public string ItemUnavailable => T("지금은 이 아이템을 사용할 수 없어요.", "This item cannot be used now.", "今はこのアイテムを使えません。", "Este objeto no se puede usar ahora.", "Cet objet ne peut pas être utilisé maintenant.", "Este item não pode ser usado agora.", "Dieser Gegenstand kann derzeit nicht verwendet werden.");
    public string RepresentativeUpdated => T("대표 포켓몬을 바꿨어요.", "Representative updated.", "代表ポケモンを変更しました。", "Representante actualizado.", "Pokémon représentatif mis à jour.", "Pokémon representativo atualizado.", "Repräsentatives Pokémon aktualisiert.");
    public string SpeciesNotInCollection => T("해당 종은 컬렉션에 없어요.", "That species is not in the collection.", "その種はコレクションにありません。", "Esa especie no está en la colección.", "Cette espèce n’est pas dans la collection.", "Essa espécie não está na coleção.", "Diese Spezies ist nicht in der Sammlung.");
    public string RepresentativeFollowsCurrent => T("대표 포켓몬이 현재 컴패니언을 따라가요.", "Representative follows the current companion.", "代表ポケモンは現在のコンパニオンに従います。", "El representante sigue al compañero actual.", "Le représentant suit le compagnon actuel.", "O representante segue o companheiro atual.", "Das repräsentative Pokémon folgt dem aktuellen Begleiter.");
    public string Manual => T("수동", "Manual", "手動", "Manual", "Manuel", "Manual", "Manuell");
    public string Minutes(int value) => T($"{value}분", $"{value} minute{(value == 1 ? "" : "s")}", $"{value}分", $"{value} minuto{(value == 1 ? "" : "s")}", $"{value} minute{(value == 1 ? "" : "s")}", $"{value} minuto{(value == 1 ? "" : "s")}", $"{value} Minute{(value == 1 ? "" : "n")}");
    public string InvalidPaths(string paths) => T($"무시된 잘못된 경로: {paths}", $"Ignored invalid paths: {paths}", $"無効なパスを無視しました: {paths}", $"Rutas no válidas ignoradas: {paths}", $"Chemins non valides ignorés : {paths}", $"Caminhos inválidos ignorados: {paths}", $"Ungültige Pfade ignoriert: {paths}");
    public string StartupSettingNotRetained => T("Windows가 요청한 시작 설정을 유지하지 않았어요.", "Windows did not retain the requested startup setting.", "Windowsが要求された起動設定を保持しませんでした。", "Windows no conservó la configuración de inicio solicitada.", "Windows n’a pas conservé le réglage de démarrage demandé.", "O Windows não manteve a configuração de inicialização solicitada.", "Windows hat die gewünschte Autostart-Einstellung nicht übernommen.");
    public string ResetDue => T("재설정 예정 시각 지남", "Reset due", "リセット時刻を過ぎました", "Reinicio pendiente", "Réinitialisation imminente", "Redefinição pendente", "Zurücksetzung fällig");
    public string ResetsIn(string value) => T($"{value} 후 재설정", $"Resets in {value}", $"{value}後にリセット", $"Se reinicia en {value}", $"Réinitialisation dans {value}", $"Redefine em {value}", $"Zurücksetzung in {value}");
    public string JustNow => T("방금", "just now", "たった今", "ahora mismo", "à l’instant", "agora", "gerade eben");
    public string MinutesAgo(int value) => T($"{value}분 전", $"{value} minute{(value == 1 ? "" : "s")} ago", $"{value}分前", $"hace {value} minuto{(value == 1 ? "" : "s")}", $"il y a {value} minute{(value == 1 ? "" : "s")}", $"há {value} minuto{(value == 1 ? "" : "s")}", $"vor {value} Minute{(value == 1 ? "" : "n")}");
    public string HoursAgo(int value) => T($"{value}시간 전", $"{value} hour{(value == 1 ? "" : "s")} ago", $"{value}時間前", $"hace {value} hora{(value == 1 ? "" : "s")}", $"il y a {value} heure{(value == 1 ? "" : "s")}", $"há {value} hora{(value == 1 ? "" : "s")}", $"vor {value} Stunde{(value == 1 ? "" : "n")}");
    public string DaysAgo(int value) => T($"{value}일 전", $"{value} day{(value == 1 ? "" : "s")} ago", $"{value}日前", $"hace {value} día{(value == 1 ? "" : "s")}", $"il y a {value} jour{(value == 1 ? "" : "s")}", $"há {value} dia{(value == 1 ? "" : "s")}", $"vor {value} Tag{(value == 1 ? "" : "en")}");
    public string NotChecked => T("확인하지 않음", "Not checked", "未確認", "Sin comprobar", "Non vérifié", "Não verificado", "Nicht geprüft");
    public string Version(string version) => T($"버전 {version}", $"Version {version}", $"バージョン {version}", $"Versión {version}", $"Version {version}", $"Versão {version}", $"Version {version}");
    public string VersionSkipped => T("이 버전은 다시 표시하지 않아요.", "This version will not be shown again.", "このバージョンは今後表示しません。", "Esta versión no volverá a mostrarse.", "Cette version ne sera plus affichée.", "Esta versão não será exibida novamente.", "Diese Version wird nicht erneut angezeigt.");
    public string DiagnosticsCopied => T("진단 정보를 복사했어요.", "Diagnostics copied.", "診断情報をコピーしました。", "Diagnóstico copiado.", "Diagnostic copié.", "Diagnóstico copiado.", "Diagnose kopiert.");
    public string UpdateAvailable(string version) => T($"버전 {version}을 사용할 수 있어요.", $"Version {version} is available.", $"バージョン {version} が利用可能です。", $"La versión {version} está disponible.", $"La version {version} est disponible.", $"A versão {version} está disponível.", $"Version {version} ist verfügbar.");
    public string UpdateCheckFailed => T("업데이트 확인에 실패했어요. 나중에 다시 시도하세요.", "Update check failed. Try again later.", "アップデートの確認に失敗しました。後でもう一度お試しください。", "No se pudo buscar actualizaciones. Inténtalo más tarde.", "La recherche de mise à jour a échoué. Réessaie plus tard.", "Falha ao buscar atualizações. Tente novamente mais tarde.", "Die Updateprüfung ist fehlgeschlagen. Versuche es später erneut.");
    public string UpToDate(string version) => T($"PokeTokenBar {version}은 최신 버전이에요.", $"PokeTokenBar {version} is up to date.", $"PokeTokenBar {version} は最新です。", $"PokeTokenBar {version} está actualizado.", $"PokeTokenBar {version} est à jour.", $"PokeTokenBar {version} está atualizado.", $"PokeTokenBar {version} ist aktuell.");
    public string UpdateBanner(string version) => T($"PokeTokenBar {version}을 사용할 수 있어요.", $"PokeTokenBar {version} is available.", $"PokeTokenBar {version} が利用可能です。", $"PokeTokenBar {version} está disponible.", $"PokeTokenBar {version} est disponible.", $"PokeTokenBar {version} está disponível.", $"PokeTokenBar {version} ist verfügbar.");
    public string ExportDone => T("PokeTokenBar 세이브를 내보냈어요.", "The PokeTokenBar save was exported.", "PokeTokenBarのセーブを書き出しました。", "La partida de PokeTokenBar se exportó.", "La sauvegarde PokeTokenBar a été exportée.", "O save do PokeTokenBar foi exportado.", "Der PokeTokenBar-Spielstand wurde exportiert.");
    public string ImportDone => T("불러오기를 완료하고 이전 상태를 백업했어요. 가져온 상태를 적용하려면 PokeTokenBar를 다시 시작하세요.", "Import completed and a pre-import backup was saved. Restart PokeTokenBar to load the imported state.", "読み込みが完了し、読み込み前のバックアップを保存しました。反映するにはPokeTokenBarを再起動してください。", "La importación terminó y se guardó una copia previa. Reinicia PokeTokenBar para cargar el estado importado.", "L’importation est terminée et une sauvegarde préalable a été créée. Redémarre PokeTokenBar pour charger l’état importé.", "A importação foi concluída e um backup anterior foi salvo. Reinicie o PokeTokenBar para carregar o estado importado.", "Der Import ist abgeschlossen und eine Sicherung wurde erstellt. Starte PokeTokenBar neu, um den importierten Stand zu laden.");
    public string OperationFailed => T("작업을 완료하지 못했어요.", "The operation could not be completed.", "操作を完了できませんでした。", "No se pudo completar la operación.", "L’opération n’a pas pu être effectuée.", "Não foi possível concluir a operação.", "Der Vorgang konnte nicht abgeschlossen werden.");
    public string ExportDialogTitle => T("PokeTokenBar 세이브 내보내기", "Export PokeTokenBar save", "PokeTokenBarのセーブを書き出す", "Exportar partida de PokeTokenBar", "Exporter la sauvegarde PokeTokenBar", "Exportar save do PokeTokenBar", "PokeTokenBar-Spielstand exportieren");
    public string ImportDialogTitle => T("PokeTokenBar 세이브 불러오기", "Import PokeTokenBar save", "PokeTokenBarのセーブを読み込む", "Importar partida de PokeTokenBar", "Importer la sauvegarde PokeTokenBar", "Importar save do PokeTokenBar", "PokeTokenBar-Spielstand importieren");
    public string SaveFileFilter => T("PokeTokenBar 세이브 (*.json)|*.json", "PokeTokenBar save (*.json)|*.json", "PokeTokenBar セーブ (*.json)|*.json", "Partida de PokeTokenBar (*.json)|*.json", "Sauvegarde PokeTokenBar (*.json)|*.json", "Save do PokeTokenBar (*.json)|*.json", "PokeTokenBar-Spielstand (*.json)|*.json");
    public string ImportConfirm(StateTransferPreview incoming, StateTransferSummary current) => T(
        $"현재 진행을 대체할까요?\n\n불러올 세이브: 도감 {incoming.State.DexCount}종, 누적 {incoming.State.LifetimeTokens:N0} 토큰\n현재: 도감 {current.DexCount}종, 누적 {current.LifetimeTokens:N0} 토큰\n\n먼저 로컬 백업을 만들어요.",
        $"Replace the current progress?\n\nIncoming: {incoming.State.DexCount} Dex, {incoming.State.LifetimeTokens:N0} tokens\nCurrent: {current.DexCount} Dex, {current.LifetimeTokens:N0} tokens\n\nA local backup will be created first.",
        $"現在の進行を置き換えますか？\n\n読み込むセーブ: 図鑑{incoming.State.DexCount}種、累計{incoming.State.LifetimeTokens:N0}トークン\n現在: 図鑑{current.DexCount}種、累計{current.LifetimeTokens:N0}トークン\n\n先にローカルバックアップを作成します。",
        $"¿Reemplazar el progreso actual?\n\nEntrante: {incoming.State.DexCount} en la Pokédex, {incoming.State.LifetimeTokens:N0} tokens\nActual: {current.DexCount} en la Pokédex, {current.LifetimeTokens:N0} tokens\n\nPrimero se creará una copia local.",
        $"Remplacer la progression actuelle ?\n\nÀ importer : {incoming.State.DexCount} au Pokédex, {incoming.State.LifetimeTokens:N0} tokens\nActuel : {current.DexCount} au Pokédex, {current.LifetimeTokens:N0} tokens\n\nUne sauvegarde locale sera d’abord créée.",
        $"Substituir o progresso atual?\n\nA importar: {incoming.State.DexCount} na Pokédex, {incoming.State.LifetimeTokens:N0} tokens\nAtual: {current.DexCount} na Pokédex, {current.LifetimeTokens:N0} tokens\n\nUm backup local será criado primeiro.",
        $"Aktuellen Fortschritt ersetzen?\n\nImport: {incoming.State.DexCount} im Pokédex, {incoming.State.LifetimeTokens:N0} Tokens\nAktuell: {current.DexCount} im Pokédex, {current.LifetimeTokens:N0} Tokens\n\nZuerst wird eine lokale Sicherung erstellt.");
    public string TransferError(StateTransferError reason) => reason switch
    {
        StateTransferError.NotASaveFile => T("유효한 PokeTokenBar 세이브가 아니에요.", "The selected file is not a valid PokeTokenBar save.", "有効なPokeTokenBarのセーブではありません。", "El archivo no es una partida válida de PokeTokenBar.", "Le fichier n’est pas une sauvegarde PokeTokenBar valide.", "O arquivo não é um save válido do PokeTokenBar.", "Die Datei ist kein gültiger PokeTokenBar-Spielstand."),
        StateTransferError.NewerFormat => T("더 새로운 버전에서 만든 세이브예요.", "This save requires a newer PokeTokenBar.", "新しいバージョンのPokeTokenBarが必要です。", "Esta partida requiere una versión más reciente de PokeTokenBar.", "Cette sauvegarde nécessite une version plus récente de PokeTokenBar.", "Este save exige uma versão mais recente do PokeTokenBar.", "Dieser Spielstand benötigt eine neuere PokeTokenBar-Version."),
        StateTransferError.FileTooLarge => T("세이브 파일이 너무 커요.", "The save file is too large.", "セーブファイルが大きすぎます。", "El archivo de partida es demasiado grande.", "Le fichier de sauvegarde est trop volumineux.", "O arquivo de save é grande demais.", "Die Spielstandsdatei ist zu groß."),
        StateTransferError.BackupFailed => T("불러오기 전 백업을 만들지 못했어요.", "Could not create the pre-import backup.", "読み込み前のバックアップを作成できませんでした。", "No se pudo crear la copia previa a la importación.", "Impossible de créer la sauvegarde avant importation.", "Não foi possível criar o backup antes da importação.", "Die Sicherung vor dem Import konnte nicht erstellt werden."),
        StateTransferError.CommitFailed => T("불러오기를 되돌렸어요.", "The import was rolled back.", "読み込みをロールバックしました。", "La importación se revirtió.", "L’importation a été annulée.", "A importação foi revertida.", "Der Import wurde zurückgesetzt."),
        _ => OperationFailed,
    };
    public string LocalDate(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(Language switch
    {
        AppLanguage.Ko => "ko-KR", AppLanguage.Ja => "ja-JP", AppLanguage.Es => "es-ES",
        AppLanguage.Fr => "fr-FR", AppLanguage.Pt => "pt-BR", AppLanguage.De => "de-DE", _ => "en-US",
    }));

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
