using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public sealed record RefreshIntervalOption(RefreshIntervalMode Value, string Label);
    public sealed record LanguageOption(AppLanguage Value, string Label);
    public sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;
    public sealed record ProviderRootOption(string Id, string Label);

    private static readonly IReadOnlyList<RefreshIntervalOption> IntervalOptions =
    [
        new(RefreshIntervalMode.Manual, "Manual"),
        new(RefreshIntervalMode.OneMinute, "1 minute"),
        new(RefreshIntervalMode.TwoMinutes, "2 minutes"),
        new(RefreshIntervalMode.FiveMinutes, "5 minutes"),
        new(RefreshIntervalMode.FifteenMinutes, "15 minutes"),
    ];

    private readonly IAppSettingsPersistence _persistence;
    private readonly IAutoStartService _autoStart;
    private AppSettings _settings;
    private bool _isFloatingPetEnabled;
    private bool _isLaunchAtStartupEnabled;
    private RefreshIntervalMode _selectedRefreshInterval;
    private AppLanguage _selectedLanguage;
    private LimitDisplayMode _selectedLimitDisplayMode;
    private AnimationQuality _selectedAnimationQuality;
    private double _floatingPetSize;
    private double _warningThreshold;
    private double _criticalThreshold;
    private bool _limitNotificationsEnabled;
    private bool _companionNotificationsEnabled;
    private bool _floatingBubbleAlertsEnabled;
    private string _selectedRootProviderId = "codex";
    private string _customRootText = "";
    private string? _customRootStatus;
    private string? _errorMessage;

    public SettingsViewModel(
        IAppSettingsPersistence persistence,
        IAutoStartService autoStartService,
        AppLanguage? fallbackLanguage = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _autoStart = autoStartService ?? throw new ArgumentNullException(nameof(autoStartService));
        _settings = LoadSettings();
        _isFloatingPetEnabled = _settings.FloatingPetEnabled;
        _selectedRefreshInterval = _settings.RefreshInterval;
        _selectedLanguage = _settings.Language ?? fallbackLanguage ?? AppLanguageRules.SystemDefault;
        _selectedLimitDisplayMode = _settings.LimitDisplayMode;
        _selectedAnimationQuality = _settings.AnimationQuality;
        _floatingPetSize = _settings.FloatingPetSize;
        _warningThreshold = _settings.WarningThreshold;
        _criticalThreshold = _settings.CriticalThreshold;
        _limitNotificationsEnabled = _settings.LimitNotificationsEnabled;
        _companionNotificationsEnabled = _settings.CompanionNotificationsEnabled;
        _floatingBubbleAlertsEnabled = _settings.FloatingBubbleAlertsEnabled;
        Localization = new LocalizationService(_selectedLanguage);
        _customRootText = CustomRootValue(_selectedRootProviderId);
        _isLaunchAtStartupEnabled = ReadAutoStartState();
        _settings = _settings with { LaunchAtStartup = _isLaunchAtStartupEnabled };
        ResetFloatingPetPositionCommand = new AsyncCommand(_ =>
        {
            ResetFloatingPetPosition();
            return Task.CompletedTask;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? FloatingPetPositionResetRequested;

    internal event Action<RefreshIntervalMode>? RefreshIntervalChanged;

    internal event Action<AppLanguage>? LanguageChanged;

    internal event EventHandler? CustomRootsChanged;

    public LocalizationService Localization { get; }

    public IReadOnlyList<RefreshIntervalOption> RefreshIntervalOptions => IntervalOptions;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.Ko, "한국어"), new(AppLanguage.En, "English"),
        new(AppLanguage.Ja, "日本語"), new(AppLanguage.Es, "Español"),
        new(AppLanguage.Fr, "Français"), new(AppLanguage.Pt, "Português"),
        new(AppLanguage.De, "Deutsch"),
    ];

    public IReadOnlyList<EnumOption<LimitDisplayMode>> LimitDisplayOptions =>
    [
        new(LimitDisplayMode.Used, Localization.Used),
        new(LimitDisplayMode.Remaining, Localization.Remaining),
    ];

    public IReadOnlyList<EnumOption<AnimationQuality>> AnimationQualityOptions =>
    [
        new(AnimationQuality.PowerSaver, Localization.PowerSaver),
        new(AnimationQuality.Balanced, Localization.Balanced),
        new(AnimationQuality.Smooth, Localization.Smooth),
    ];

    public IReadOnlyList<ProviderRootOption> ProviderRootOptions { get; } =
    [
        new("codex", "Codex"), new("claude_code", "Claude Code"),
        new("gemini", "Gemini"), new("antigravity", "Antigravity"),
        new("cursor", "Cursor"), new("opencode", "OpenCode"),
        new("hermes", "Hermes Agent"), new("grok", "Grok"),
        new("copilot", "GitHub Copilot"), new("kiro", "Kiro"),
        new("pi", "Pi"), new("omp", "omp"),
    ];

    public RefreshIntervalMode SelectedRefreshInterval
    {
        get => _selectedRefreshInterval;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (!SetField(ref _selectedRefreshInterval, value))
            {
                return;
            }

            _settings = _settings with { RefreshInterval = value };
            SaveSettings();
            RefreshIntervalChanged?.Invoke(value);
        }
    }

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetField(ref _selectedLanguage, value)) return;
            Localization.Language = value;
            _settings = _settings with { Language = value };
            SaveSettings();
            OnPropertyChanged(nameof(LimitDisplayOptions));
            OnPropertyChanged(nameof(AnimationQualityOptions));
            LanguageChanged?.Invoke(value);
        }
    }

    public LimitDisplayMode SelectedLimitDisplayMode
    {
        get => _selectedLimitDisplayMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetField(ref _selectedLimitDisplayMode, value)) return;
            _settings = _settings with { LimitDisplayMode = value };
            SaveSettings();
        }
    }

    public AnimationQuality SelectedAnimationQuality
    {
        get => _selectedAnimationQuality;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetField(ref _selectedAnimationQuality, value)) return;
            _settings = _settings with { AnimationQuality = value };
            SaveSettings();
        }
    }

    public double FloatingPetSize
    {
        get => _floatingPetSize;
        set
        {
            value = Math.Clamp(Math.Round(value / 8) * 8, 48, 192);
            if (!SetField(ref _floatingPetSize, value)) return;
            _settings = _settings with { FloatingPetSize = value };
            SaveSettings();
        }
    }

    public bool LimitNotificationsEnabled
    {
        get => _limitNotificationsEnabled;
        set
        {
            if (!SetField(ref _limitNotificationsEnabled, value)) return;
            _settings = _settings with { LimitNotificationsEnabled = value };
            SaveSettings();
        }
    }

    public bool CompanionNotificationsEnabled
    {
        get => _companionNotificationsEnabled;
        set
        {
            if (!SetField(ref _companionNotificationsEnabled, value)) return;
            _settings = _settings with { CompanionNotificationsEnabled = value };
            SaveSettings();
        }
    }

    public bool FloatingBubbleAlertsEnabled
    {
        get => _floatingBubbleAlertsEnabled;
        set
        {
            if (!SetField(ref _floatingBubbleAlertsEnabled, value)) return;
            _settings = _settings with { FloatingBubbleAlertsEnabled = value };
            SaveSettings();
        }
    }

    public double WarningThreshold
    {
        get => _warningThreshold;
        set
        {
            value = Math.Clamp(Math.Round(value / 5) * 5, 50, 95);
            if (!SetField(ref _warningThreshold, value)) return;
            if (_criticalThreshold <= value)
            {
                SetField(ref _criticalThreshold, Math.Min(100, value + 5), nameof(CriticalThreshold));
            }
            SaveThresholds();
        }
    }

    public double CriticalThreshold
    {
        get => _criticalThreshold;
        set
        {
            value = Math.Clamp(Math.Round(value / 5) * 5, 55, 100);
            if (!SetField(ref _criticalThreshold, value)) return;
            if (_warningThreshold >= value)
            {
                SetField(ref _warningThreshold, Math.Max(50, value - 5), nameof(WarningThreshold));
            }
            SaveThresholds();
        }
    }

    public string SelectedRootProviderId
    {
        get => _selectedRootProviderId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SetField(ref _selectedRootProviderId, value)) return;
            CustomRootText = CustomRootValue(value);
        }
    }

    public string CustomRootText
    {
        get => _customRootText;
        set
        {
            value ??= "";
            if (!SetField(ref _customRootText, value)) return;
            var roots = new Dictionary<string, string>(
                _settings.CustomProviderRoots ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(value)) roots.Remove(SelectedRootProviderId);
            else roots[SelectedRootProviderId] = value;
            _settings = _settings with { CustomProviderRoots = roots };
            CustomRootStatus = InvalidRoots(value) is { Count: > 0 } invalid
                ? $"Ignored invalid paths: {string.Join(", ", invalid)}"
                : null;
            SaveSettings();
            CustomRootsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? CustomRootStatus
    {
        get => _customRootStatus;
        private set => SetField(ref _customRootStatus, value);
    }

    internal IReadOnlyList<string> CustomRoots(string providerId) =>
        ParseRoots(CustomRootValue(providerId));

    internal string? SelectedProviderId => _settings.SelectedProviderId;

    internal void SaveSelectedProvider(string? providerId)
    {
        if (string.Equals(_settings.SelectedProviderId, providerId, StringComparison.Ordinal)) return;
        _settings = _settings with { SelectedProviderId = providerId };
        SaveSettings();
    }

    internal IReadOnlyDictionary<string, int> NotificationTiers =>
        _settings.NotificationTiers ?? new Dictionary<string, int>();

    internal void SaveNotificationTiers(IReadOnlyDictionary<string, int> tiers)
    {
        if (tiers.OrderBy(pair => pair.Key).SequenceEqual(
                NotificationTiers.OrderBy(pair => pair.Key))) return;
        _settings = _settings with
        {
            NotificationTiers = new Dictionary<string, int>(tiers, StringComparer.Ordinal),
        };
        SaveSettings();
    }

    public bool IsFloatingPetEnabled
    {
        get => _isFloatingPetEnabled;
        set
        {
            if (!SetField(ref _isFloatingPetEnabled, value))
            {
                return;
            }

            _settings = _settings with { FloatingPetEnabled = value };
            SaveSettings();
        }
    }

    public bool IsLaunchAtStartupAvailable => _autoStart.IsAvailable;

    public bool IsLaunchAtStartupEnabled
    {
        get => _isLaunchAtStartupEnabled;
        set
        {
            if (value == _isLaunchAtStartupEnabled)
            {
                return;
            }

            try
            {
                _autoStart.SetEnabled(value);
                var actual = _autoStart.IsEnabled;
                SetField(ref _isLaunchAtStartupEnabled, actual);
                _settings = _settings with { LaunchAtStartup = actual };
                ErrorMessage = actual == value
                    ? null
                    : "Windows did not retain the requested startup setting.";
                SaveSettings();
            }
            catch (Exception exception)
            {
                SetField(ref _isLaunchAtStartupEnabled, ReadAutoStartState());
                _settings = _settings with
                {
                    LaunchAtStartup = _isLaunchAtStartupEnabled,
                };
                ErrorMessage = exception.Message;
            }
        }
    }

    public FloatingPetPosition? SavedFloatingPetPosition =>
        _settings.FloatingPetPosition;

    public AsyncCommand ResetFloatingPetPositionCommand { get; }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    internal void SaveFloatingPetPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        _settings = _settings with
        {
            FloatingPetPosition = new FloatingPetPosition(left, top),
        };
        OnPropertyChanged(nameof(SavedFloatingPetPosition));
        SaveSettings();
    }

    private void ResetFloatingPetPosition()
    {
        _settings = _settings with { FloatingPetPosition = null };
        OnPropertyChanged(nameof(SavedFloatingPetPosition));
        SaveSettings();
        FloatingPetPositionResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveThresholds()
    {
        _settings = _settings with
        {
            WarningThreshold = _warningThreshold,
            CriticalThreshold = _criticalThreshold,
        };
        SaveSettings();
    }

    private string CustomRootValue(string providerId) =>
        _settings.CustomProviderRoots?.GetValueOrDefault(providerId) ?? "";

    internal static IReadOnlyList<string> ParseRoots(string? raw) =>
        (raw ?? "").Split(['\r', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRoot)
            .Where(path => path is not null && Directory.Exists(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> InvalidRoots(string? raw) =>
        (raw ?? "").Split(['\r', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(path =>
            {
                return NormalizeRoot(path) is not { } normalized || !Directory.Exists(normalized);
            })
            .ToArray();

    private static string? NormalizeRoot(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (expanded == "~" || expanded.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                expanded = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    expanded.Length == 1 ? "" : expanded[2..]);
            }
            return Path.GetFullPath(expanded);
        }
        catch (Exception) { return null; }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return _persistence.Load() ?? AppSettings.Default;
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            return AppSettings.Default;
        }
    }

    private bool ReadAutoStartState()
    {
        try
        {
            return _autoStart.IsEnabled;
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            return false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _persistence.Save(_settings);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
