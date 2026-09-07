using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class JsonCompanionPersistence : ICompanionPersistence
{
    private bool _writeBlocked;

    internal static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public JsonCompanionPersistence(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    internal string BackupPath => $"{FilePath}.bak";

    internal void BlockWritesUntilRestart() => _writeBlocked = true;

    public static string GetDefaultFilePath()
    {
        return Path.Combine(PokeTokenBarDataPaths.Root, "companion-state.json");
    }

    public CompanionState? Load()
    {
        var primaryStatus = TryRead(FilePath, out var primary);
        if (primaryStatus == ReadStatus.Valid)
        {
            return primary;
        }

        if (primaryStatus == ReadStatus.Unavailable) _writeBlocked = true;
        if (primaryStatus == ReadStatus.Corrupt)
        {
            _writeBlocked |= !AtomicFile.Quarantine(FilePath, "companion");
        }

        var backupStatus = TryRead(BackupPath, out var backup);
        if (backupStatus == ReadStatus.Valid)
        {
            if (!_writeBlocked)
            {
                try
                {
                    AtomicFile.WriteBytes(FilePath, File.ReadAllBytes(BackupPath));
                    ReliabilityEventLog.RecordRecovery("companion", "last-known-good-restored");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReliabilityEventLog.RecordError("companion", exception);
                }
            }
            else ReliabilityEventLog.RecordRecovery("companion", "last-known-good-read-only");
            return backup;
        }
        if (backupStatus == ReadStatus.Corrupt) AtomicFile.Quarantine(BackupPath, "companion-backup");
        if (backupStatus == ReadStatus.Unavailable) _writeBlocked = true;
        return null;
    }

    public void Save(CompanionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_writeBlocked)
            throw new IOException("Companion writes are disabled until PokeTokenBar restarts.");

        var primaryStatus = TryRead(FilePath, out _);
        if (primaryStatus == ReadStatus.Unavailable)
        {
            _writeBlocked = true;
            throw new IOException("The existing companion state could not be read safely.");
        }
        if (primaryStatus == ReadStatus.Corrupt && !AtomicFile.Quarantine(FilePath, "companion"))
        {
            _writeBlocked = true;
            throw new IOException("The damaged companion state could not be isolated safely.");
        }
        if (primaryStatus == ReadStatus.Valid)
        {
            try { AtomicFile.WriteBytes(BackupPath, File.ReadAllBytes(FilePath)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReliabilityEventLog.RecordError("companion-backup", exception);
            }
        }

        AtomicFile.Write(FilePath, stream =>
            JsonSerializer.Serialize(stream, state, SerializerOptions));
    }

    public void Delete()
    {
        if (_writeBlocked)
            throw new IOException("Companion deletes are disabled until persistence is safely reloaded.");
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReliabilityEventLog.RecordError("companion", exception);
            throw;
        }
    }

    private static ReadStatus TryRead(string path, out CompanionState? state)
    {
        state = null;
        if (!File.Exists(path)) return ReadStatus.Missing;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return ReadStatus.Corrupt;
            state = ReadState(document.RootElement);
            return ReadStatus.Valid;
        }
        catch (JsonException)
        {
            return ReadStatus.Corrupt;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReliabilityEventLog.RecordError("companion", exception);
            return ReadStatus.Unavailable;
        }
    }

    private enum ReadStatus { Missing, Valid, Corrupt, Unavailable }

    internal static CompanionState ReadState(JsonElement root)
    {
        var lastDate = Read(root, "lastDate", string.Empty);
        var claimed = ReadClaimedTokens(root);
        if (claimed is not null && !DateOnly.TryParseExact(
                lastDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            claimed = null;
            lastDate = string.Empty;
        }

        return new()
        {
            InstallBaselineSet = Read(root, "installBaselineSet", false),
            UsedSinceInstall = Read(root, "usedSinceInstall", 0L),
            SpentTokens = Read(root, "spentTokens", 0L),
            EggUsage = Read(root, "eggUsage", 0L),
            EggTier = ReadNullable<PokemonRarity>(root, "eggTier"),
            PendingHatchId = ReadNullable<int>(root, "pendingHatchID"),
            ClaimedTodayTokensByProvider = claimed,
            LastDate = lastDate,
            Active = ReadActive(root),
            RepresentativeSpeciesId = ReadNullable<int>(root, "representativeSpeciesID"),
            Dex = ReadDex(root),
            CollectedFinals = Read(root, "collectedFinals", new HashSet<string>()),
            Language = Read(root, "language", AppLanguageRules.SystemDefault),
            Inventory = Read(root, "inventory", new Dictionary<string, int>()),
            CandyGrantTier = Read(root, "candyGrantTier", new Dictionary<string, int>()),
            CandyFeatureSeeded = Read(root, "candyFeatureSeeded", false),
        };
    }

    private static MonState? ReadActive(JsonElement root)
    {
        if (!root.TryGetProperty("active", out var active) || active.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (active.ValueKind != JsonValueKind.Object ||
            !HasProperties(
                active,
                "baseID",
                "pathIDs",
                "stageIndex",
                "usedAtStage",
                "rarity",
                "totalForms"))
        {
            return null;
        }

        try
        {
            var decoded = active.Deserialize<MonState>(SerializerOptions);
            if (decoded?.PathIds is not { Count: > 0 })
            {
                return null;
            }

            return decoded with
            {
                PlannedPathIds = decoded.PlannedPathIds is not { Count: > 0 }
                    ? decoded.PathIds
                    : decoded.PlannedPathIds,
                StageIndex = Math.Clamp(decoded.StageIndex, 0, decoded.PathIds.Count - 1),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<DexEntry> ReadDex(JsonElement root)
    {
        if (!root.TryGetProperty("dex", out var dex) || dex.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DexEntry>();
        }

        var entries = new List<DexEntry>();
        foreach (var element in dex.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !HasProperties(element, "baseID", "finalID", "chainOrder", "rarity"))
            {
                continue;
            }

            try
            {
                var entry = element.Deserialize<DexEntry>(SerializerOptions);
                if (entry?.ChainOrder is { Count: > 0 })
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Swift's Lossy<DexEntry> drops only the malformed array item.
            }
        }

        return entries;
    }

    private static IReadOnlyDictionary<string, long>? ReadClaimedTokens(JsonElement root)
    {
        if (!root.TryGetProperty("claimedTodayTokensByProvider", out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var claimed = Read(
            root,
            "claimedTodayTokensByProvider",
            new Dictionary<string, long>());
        return claimed.Values.All(value => value >= 0) ? claimed : null;
    }

    private static T Read<T>(JsonElement root, string propertyName, T fallback)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        try
        {
            return property.Deserialize<T>(SerializerOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static T? ReadNullable<T>(JsonElement root, string propertyName)
        where T : struct
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            return property.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasProperties(JsonElement element, params string[] names) =>
        names.All(name => element.TryGetProperty(name, out _));

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new AppleReferenceDateTimeOffsetConverter());
        return options;
    }

    private sealed class AppleReferenceDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly DateTimeOffset ReferenceDate =
            new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number ||
                !reader.TryGetDouble(out var seconds) ||
                !double.IsFinite(seconds))
            {
                throw new JsonException("A Swift-compatible Date number was expected.");
            }

            try { return ReferenceDate.AddSeconds(seconds); }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new JsonException("The Swift-compatible Date is out of range.", exception);
            }
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.ToUniversalTime() - ReferenceDate).TotalSeconds);
    }
}
