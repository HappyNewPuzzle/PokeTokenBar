namespace PokeTokenBar.Windows.Core;

/// <summary>Floating window origin stored in WPF device-independent pixels (DIPs).</summary>
public sealed record FloatingPetPosition(double Left, double Top);

public enum RefreshIntervalMode
{
    Manual = 0,
    OneMinute = 60,
    TwoMinutes = 120,
    FiveMinutes = 300,
    FifteenMinutes = 900,
}

public enum LimitDisplayMode
{
    Used,
    Remaining,
}

public enum AnimationQuality
{
    PowerSaver,
    Balanced,
    Smooth,
}

public static class FloatingPetSizeRules
{
    public const double Minimum = 48;
    public const double Default = 96;
    public const double Maximum = 384;
    public const double Step = 8;

    public static bool IsSupported(double value) =>
        double.IsFinite(value) && value is >= Minimum and <= Maximum;

    public static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Default;

    public static double Snap(double value) =>
        Clamp(Math.Round(value / Step) * Step);
}

public sealed record AppSettings(
    bool FloatingPetEnabled = false,
    FloatingPetPosition? FloatingPetPosition = null,
    bool LaunchAtStartup = false,
    RefreshIntervalMode RefreshInterval = RefreshIntervalMode.TwoMinutes,
    AppLanguage? Language = null,
    bool LimitNotificationsEnabled = true,
    bool CompanionNotificationsEnabled = true,
    double WarningThreshold = 80,
    double CriticalThreshold = 95,
    LimitDisplayMode LimitDisplayMode = LimitDisplayMode.Remaining,
    double FloatingPetSize = FloatingPetSizeRules.Default,
    AnimationQuality AnimationQuality = AnimationQuality.PowerSaver,
    bool FloatingBubbleAlertsEnabled = true,
    IReadOnlyDictionary<string, string>? CustomProviderRoots = null,
    IReadOnlyDictionary<string, int>? NotificationTiers = null,
    string? SelectedProviderId = null,
    bool UpdateNotificationsEnabled = true,
    string? SkippedUpdateVersion = null,
    bool CredentialAccessEnabled = true)
{
    public static AppSettings Default { get; } = new();
}
