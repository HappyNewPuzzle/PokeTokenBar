using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class JsonAppSettingsPersistence : IAppSettingsPersistence
{
    private bool _writeBlocked;

    internal static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public JsonAppSettingsPersistence(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    internal void BlockWritesUntilRestart() => _writeBlocked = true;

    public static string GetDefaultFilePath()
    {
        return Path.Combine(PokeTokenBarDataPaths.Root, "settings.json");
    }

    public AppSettings? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _writeBlocked |= !AtomicFile.Quarantine(FilePath, "settings");
                return null;
            }

            var recovered = false;
            var settings = ReadSettings(document.RootElement, ref recovered);
            var normalized = Normalize(settings);
            if (recovered || settings != normalized)
                ReliabilityEventLog.RecordRecovery("settings", "invalid-fields-defaulted");
            return normalized;
        }
        catch (JsonException)
        {
            _writeBlocked |= !AtomicFile.Quarantine(FilePath, "settings");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _writeBlocked = true;
            ReliabilityEventLog.RecordError("settings", exception);
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_writeBlocked)
            throw new IOException("Settings writes are disabled until PokeTokenBar restarts.");
        if (!IsValid(settings)) throw new ArgumentException("Settings contain invalid values.", nameof(settings));
        AtomicFile.Write(FilePath, stream =>
            JsonSerializer.Serialize(stream, settings, SerializerOptions));
    }

    internal static bool IsValid(AppSettings settings) =>
        Enum.IsDefined(settings.RefreshInterval) &&
        (settings.Language is null || Enum.IsDefined(settings.Language.Value)) &&
        Enum.IsDefined(settings.LimitDisplayMode) &&
        Enum.IsDefined(settings.AnimationQuality) &&
        double.IsFinite(settings.WarningThreshold) &&
        double.IsFinite(settings.CriticalThreshold) &&
        settings.WarningThreshold is >= 50 and <= 95 &&
        settings.CriticalThreshold is >= 55 and <= 100 &&
        settings.WarningThreshold < settings.CriticalThreshold &&
        double.IsFinite(settings.FloatingPetSize) &&
        settings.FloatingPetSize is >= 48 and <= 192 &&
        (settings.FloatingPetPosition is not { } position ||
         (double.IsFinite(position.Left) && double.IsFinite(position.Top)));

    private static AppSettings ReadSettings(JsonElement root, ref bool recovered)
    {
        var defaults = AppSettings.Default;
        return defaults with
        {
            FloatingPetEnabled = Read(root, "floatingPetEnabled", defaults.FloatingPetEnabled, ref recovered),
            FloatingPetPosition = Read(root, "floatingPetPosition", defaults.FloatingPetPosition, ref recovered),
            LaunchAtStartup = Read(root, "launchAtStartup", defaults.LaunchAtStartup, ref recovered),
            RefreshInterval = Read(root, "refreshInterval", defaults.RefreshInterval, ref recovered),
            Language = Read(root, "language", defaults.Language, ref recovered),
            LimitNotificationsEnabled = Read(root, "limitNotificationsEnabled", defaults.LimitNotificationsEnabled, ref recovered),
            CompanionNotificationsEnabled = Read(root, "companionNotificationsEnabled", defaults.CompanionNotificationsEnabled, ref recovered),
            WarningThreshold = Read(root, "warningThreshold", defaults.WarningThreshold, ref recovered),
            CriticalThreshold = Read(root, "criticalThreshold", defaults.CriticalThreshold, ref recovered),
            LimitDisplayMode = Read(root, "limitDisplayMode", defaults.LimitDisplayMode, ref recovered),
            FloatingPetSize = Read(root, "floatingPetSize", defaults.FloatingPetSize, ref recovered),
            AnimationQuality = Read(root, "animationQuality", defaults.AnimationQuality, ref recovered),
            FloatingBubbleAlertsEnabled = Read(root, "floatingBubbleAlertsEnabled", defaults.FloatingBubbleAlertsEnabled, ref recovered),
            CustomProviderRoots = Read(root, "customProviderRoots", defaults.CustomProviderRoots, ref recovered),
            NotificationTiers = Read(root, "notificationTiers", defaults.NotificationTiers, ref recovered),
            SelectedProviderId = Read(root, "selectedProviderId", defaults.SelectedProviderId, ref recovered),
            UpdateNotificationsEnabled = Read(root, "updateNotificationsEnabled", defaults.UpdateNotificationsEnabled, ref recovered),
            SkippedUpdateVersion = Read(root, "skippedUpdateVersion", defaults.SkippedUpdateVersion, ref recovered),
            CredentialAccessEnabled = Read(root, "credentialAccessEnabled", defaults.CredentialAccessEnabled, ref recovered),
        };
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        var warning = double.IsFinite(settings.WarningThreshold) && settings.WarningThreshold is >= 50 and <= 95
            ? settings.WarningThreshold : defaults.WarningThreshold;
        var critical = double.IsFinite(settings.CriticalThreshold) && settings.CriticalThreshold is >= 55 and <= 100
            ? settings.CriticalThreshold : defaults.CriticalThreshold;
        if (warning >= critical)
        {
            warning = defaults.WarningThreshold;
            critical = defaults.CriticalThreshold;
        }

        return settings with
        {
            RefreshInterval = Enum.IsDefined(settings.RefreshInterval) ? settings.RefreshInterval : defaults.RefreshInterval,
            Language = settings.Language is null || Enum.IsDefined(settings.Language.Value) ? settings.Language : defaults.Language,
            WarningThreshold = warning,
            CriticalThreshold = critical,
            LimitDisplayMode = Enum.IsDefined(settings.LimitDisplayMode) ? settings.LimitDisplayMode : defaults.LimitDisplayMode,
            FloatingPetSize = double.IsFinite(settings.FloatingPetSize) && settings.FloatingPetSize is >= 48 and <= 192
                ? settings.FloatingPetSize : defaults.FloatingPetSize,
            AnimationQuality = Enum.IsDefined(settings.AnimationQuality) ? settings.AnimationQuality : defaults.AnimationQuality,
            FloatingPetPosition = settings.FloatingPetPosition is { } position &&
                                  (!double.IsFinite(position.Left) || !double.IsFinite(position.Top))
                ? defaults.FloatingPetPosition : settings.FloatingPetPosition,
        };
    }

    private static T Read<T>(JsonElement root, string name, T fallback, ref bool recovered)
    {
        if (!root.TryGetProperty(name, out var property)) return fallback;
        try { return property.Deserialize<T>(SerializerOptions) ?? fallback; }
        catch (JsonException)
        {
            recovered = true;
            return fallback;
        }
    }
}
