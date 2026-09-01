using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class JsonCompanionPersistence : ICompanionPersistence
{
    internal static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public JsonCompanionPersistence(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath()
    {
        return Path.Combine(PokeTokenBarDataPaths.Root, "companion-state.json");
    }

    public CompanionState? Load()
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
                BackupCorruptFile();
                return null;
            }

            return ReadState(document.RootElement);
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return null;
        }
    }

    public void Save(CompanionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The companion-state path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    private void BackupCorruptFile()
    {
        var backupPath = $"{FilePath}.corrupt";
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        if (File.Exists(FilePath))
        {
            File.Move(FilePath, backupPath);
        }
    }

    internal static CompanionState ReadState(JsonElement root) =>
        new()
        {
            InstallBaselineSet = Read(root, "installBaselineSet", false),
            UsedSinceInstall = Read(root, "usedSinceInstall", 0L),
            SpentTokens = Read(root, "spentTokens", 0L),
            EggUsage = Read(root, "eggUsage", 0L),
            EggTier = ReadNullable<PokemonRarity>(root, "eggTier"),
            PendingHatchId = ReadNullable<int>(root, "pendingHatchID"),
            ClaimedTodayTokensByProvider = ReadClaimedTokens(root),
            LastDate = Read(root, "lastDate", string.Empty),
            Active = ReadActive(root),
            RepresentativeSpeciesId = ReadNullable<int>(root, "representativeSpeciesID"),
            Dex = ReadDex(root),
            CollectedFinals = Read(root, "collectedFinals", new HashSet<string>()),
            Language = Read(root, "language", AppLanguageRules.SystemDefault),
            Inventory = Read(root, "inventory", new Dictionary<string, int>()),
            CandyGrantTier = Read(root, "candyGrantTier", new Dictionary<string, int>()),
            CandyFeatureSeeded = Read(root, "candyFeatureSeeded", false),
        };

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
            if (decoded is null || decoded.PathIds.Count == 0)
            {
                return null;
            }

            return decoded with
            {
                PlannedPathIds = decoded.PlannedPathIds.Count == 0
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
                if (entry is not null)
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

        return Read(
            root,
            "claimedTodayTokensByProvider",
            new Dictionary<string, long>());
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
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var seconds))
            {
                throw new JsonException("A Swift-compatible Date number was expected.");
            }

            return ReferenceDate.AddSeconds(seconds);
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.ToUniversalTime() - ReferenceDate).TotalSeconds);
    }
}
