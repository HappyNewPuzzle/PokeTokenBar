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

    public string ProviderStatus => T("프로바이더 상태", "Provider status", "プロバイダー状態", "Estado del proveedor", "État du fournisseur", "Status do provedor", "Providerstatus");
    public string Authentication => T("인증", "Authentication", "認証", "Autenticación", "Authentification", "Autenticação", "Authentifizierung");
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
