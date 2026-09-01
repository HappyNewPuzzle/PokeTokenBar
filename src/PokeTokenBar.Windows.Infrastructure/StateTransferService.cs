using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class StateTransferService
{
    public const string FormatId = "poketokenbar.save";
    public const int FormatVersion = 1;
    public const int MaxFileBytes = 8 * 1024 * 1024;
    public const long MaxTokenValue = 1_000_000_000_000_000;

    private readonly JsonAppSettingsPersistence _settings;
    private readonly JsonCompanionPersistence _companion;
    private readonly string _appVersion;
    private readonly TimeProvider _timeProvider;
    private readonly Action<int>? _beforeCommitStep;

    public StateTransferService(
        JsonAppSettingsPersistence settings,
        JsonCompanionPersistence companion,
        string appVersion,
        TimeProvider? timeProvider = null,
        Action<int>? beforeCommitStep = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _companion = companion ?? throw new ArgumentNullException(nameof(companion));
        _appVersion = appVersion;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _beforeCommitStep = beforeCommitStep;
    }

    public string SuggestedFileName =>
        $"PokeTokenBar-Save-{_timeProvider.GetUtcNow():yyyy-MM-dd}.json";

    public StateTransferSummary CurrentSummary
    {
        get
        {
            var state = _companion.Load() ?? new CompanionState();
            return new(state.Dex.Count, state.UsedSinceInstall);
        }
    }

    public byte[] Export()
    {
        var state = _companion.Load() ?? new CompanionState();
        var settings = _settings.Load() ?? AppSettings.Default;
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(
            FormatId,
            FormatVersion,
            _appVersion,
            _timeProvider.GetUtcNow(),
            Environment.MachineName,
            JsonSerializer.SerializeToElement(settings, JsonAppSettingsPersistence.SerializerOptions),
            JsonSerializer.SerializeToElement(state, JsonCompanionPersistence.SerializerOptions)),
            EnvelopeOptions);
    }

    public void ExportTo(string path) => WriteAtomic(Path.GetFullPath(path), Export());

    public StateTransferPreview Preview(ReadOnlySpan<byte> data)
    {
        var candidate = Decode(data);
        return new(candidate.FormatVersion, candidate.AppVersion, candidate.ExportedAtUtc,
            candidate.SourceDevice, new(candidate.State.Dex.Count, candidate.State.UsedSinceInstall));
    }

    public void Import(
        ReadOnlySpan<byte> data,
        IReadOnlyDictionary<string, long>? todayTokensByProvider = null,
        string? todayDate = null,
        bool hasUsageData = false)
    {
        var candidate = Decode(data);
        var currentState = _companion.Load() ?? new CompanionState();
        var currentSettings = _settings.Load() ?? AppSettings.Default;
        var importedState = Rebase(candidate.State, currentState, todayTokensByProvider,
            todayDate ?? DateTimeOffset.Now.ToString("yyyy-MM-dd"), hasUsageData);
        var importedSettings = candidate.Settings ?? currentSettings;

        var settingsBytes = JsonSerializer.SerializeToUtf8Bytes(
            importedSettings, JsonAppSettingsPersistence.SerializerOptions);
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(
            importedState, JsonCompanionPersistence.SerializerOptions);
        var oldSettings = ReadExisting(_settings.FilePath);
        var oldState = ReadExisting(_companion.FilePath);

        try
        {
            WriteBackup(Export());
        }
        catch (Exception exception)
        {
            throw new StateTransferException(StateTransferError.BackupFailed,
                $"Could not create the pre-import backup: {exception.Message}");
        }

        try
        {
            _beforeCommitStep?.Invoke(1);
            WriteAtomic(_settings.FilePath, settingsBytes);
            _beforeCommitStep?.Invoke(2);
            WriteAtomic(_companion.FilePath, stateBytes);
        }
        catch (Exception exception)
        {
            Restore(_settings.FilePath, oldSettings);
            Restore(_companion.FilePath, oldState);
            throw new StateTransferException(StateTransferError.CommitFailed,
                $"The import was rolled back: {exception.Message}");
        }
    }

    private Candidate Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
            throw new StateTransferException(StateTransferError.FileTooLarge, "The save file is too large.");
        try
        {
            using var document = JsonDocument.Parse(data.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format) || format.GetString() != FormatId ||
                !root.TryGetProperty("formatVersion", out var version) &&
                !root.TryGetProperty("schema", out version))
                throw Invalid();
            var formatVersion = version.GetInt32();
            if (formatVersion > FormatVersion)
                throw new StateTransferException(StateTransferError.NewerFormat,
                    $"Save format {formatVersion} requires a newer PokeTokenBar.");
            if (!root.TryGetProperty("state", out var stateElement) ||
                stateElement.ValueKind != JsonValueKind.Object) throw Invalid();

            var state = Sanitize(JsonCompanionPersistence.ReadState(stateElement));
            AppSettings? settings = null;
            if (root.TryGetProperty("settings", out var settingsElement) &&
                settingsElement.ValueKind != JsonValueKind.Null)
            {
                settings = settingsElement.Deserialize<AppSettings>(JsonAppSettingsPersistence.SerializerOptions);
                if (settings is null || !JsonAppSettingsPersistence.IsValid(settings)) throw Invalid();
            }

            return new(formatVersion,
                root.TryGetProperty("appVersion", out var app) ? app.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("exportedAtUtc", out var exportedAt)
                    ? exportedAt.GetDateTimeOffset()
                    : root.TryGetProperty("exportedAt", out exportedAt)
                        ? exportedAt.GetDateTimeOffset() : DateTimeOffset.MinValue,
                root.TryGetProperty("sourceDevice", out var device) ? device.GetString() ?? "unknown" : "unknown",
                settings, state);
        }
        catch (StateTransferException) { throw; }
        catch (Exception) { throw Invalid(); }
    }

    private static CompanionState Sanitize(CompanionState state)
    {
        static long Token(long value) => Math.Clamp(value, 0, MaxTokenValue);
        var active = state.Active;
        if (active is not null)
        {
            var count = Math.Max(1, active.PathIds.Count);
            active = active with
            {
                UsedAtStage = Token(active.UsedAtStage),
                TotalForms = Math.Clamp(active.TotalForms, 1, 12),
                StageIndex = Math.Clamp(active.StageIndex, 0, count - 1),
            };
        }
        var result = state with
        {
            UsedSinceInstall = Token(state.UsedSinceInstall),
            SpentTokens = Token(state.SpentTokens),
            EggUsage = Token(state.EggUsage),
            Active = active,
            EggTier = active is not null || state.EggTier == PokemonRarity.Legendary ? null : state.EggTier,
            PendingHatchId = active is not null ? null : state.PendingHatchId,
            ClaimedTodayTokensByProvider = state.ClaimedTodayTokensByProvider?
                .ToDictionary(pair => pair.Key, pair => Token(pair.Value)),
            Inventory = state.Inventory.ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 0, 1_000_000)),
            CandyGrantTier = state.CandyGrantTier.ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 0, 2)),
        };
        return result.RepresentativeSpeciesId is int id && !result.OwnsSpecies(id)
            ? result with { RepresentativeSpeciesId = null }
            : result;
    }

    private static CompanionState Rebase(
        CompanionState imported,
        CompanionState current,
        IReadOnlyDictionary<string, long>? today,
        string todayDate,
        bool hasUsage)
    {
        var tiers = imported.CandyGrantTier.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in current.CandyGrantTier)
            tiers[pair.Key] = Math.Max(tiers.GetValueOrDefault(pair.Key), pair.Value);
        var seed = hasUsage && today is { Count: > 0 };
        return imported with
        {
            Language = current.Language,
            CandyGrantTier = tiers,
            CandyFeatureSeeded = imported.CandyFeatureSeeded || current.CandyFeatureSeeded,
            InstallBaselineSet = seed,
            ClaimedTodayTokensByProvider = seed ? new Dictionary<string, long>(today!) : null,
            LastDate = seed ? todayDate : string.Empty,
        };
    }

    private void WriteBackup(byte[] data)
    {
        var directory = Path.Combine(Path.GetDirectoryName(_companion.FilePath)!, "backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"PokeTokenBar-PreImport-{_timeProvider.GetUtcNow():yyyy-MM-dd-HHmmss-fffffff}.json");
        WriteAtomic(path, data);
        foreach (var stale in Directory.GetFiles(directory, "PokeTokenBar-PreImport-*.json")
                     .OrderByDescending(value => value).Skip(5)) File.Delete(stale);
    }

    private static byte[]? ReadExisting(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void Restore(string path, byte[]? data)
    {
        if (data is null) { if (File.Exists(path)) File.Delete(path); }
        else WriteAtomic(path, data);
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }

    private static StateTransferException Invalid() =>
        new(StateTransferError.NotASaveFile, "The selected file is not a valid PokeTokenBar save.");

    private sealed record Envelope(
        string Format,
        int Schema,
        string AppVersion,
        DateTimeOffset ExportedAt,
        string SourceDevice,
        JsonElement Settings,
        JsonElement State);

    private sealed record Candidate(
        int FormatVersion,
        string AppVersion,
        DateTimeOffset ExportedAtUtc,
        string SourceDevice,
        AppSettings? Settings,
        CompanionState State);

    private static readonly JsonSerializerOptions EnvelopeOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
