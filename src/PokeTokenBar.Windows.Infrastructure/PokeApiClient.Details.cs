using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed partial class PokeApiClient
{
    private readonly string _detailsCacheDirectory;
    private readonly Dictionary<int, PokemonDetails> _detailsCache = [];
    private static readonly JsonSerializerOptions DetailJsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record DetailSnapshot(DateTimeOffset FetchedAt, PokemonDetails Details);

    public async Task<PokemonDetails> GetPokemonDetailsAsync(int speciesId, CancellationToken cancellationToken = default)
    {
        if (!PokemonAssets.HasAnimatedSprite(speciesId)) throw new ArgumentOutOfRangeException(nameof(speciesId));
        lock (_cacheLock)
            if (_detailsCache.TryGetValue(speciesId, out var cached)) return cached;
        var path = Path.Combine(_detailsCacheDirectory, speciesId.ToString(CultureInfo.InvariantCulture) + ".json");
        DetailSnapshot? disk = null;
        try
        {
            if (File.Exists(path))
            {
                var snapshot = JsonSerializer.Deserialize<DetailSnapshot>(await File.ReadAllBytesAsync(path, cancellationToken), DetailJsonOptions);
                if (snapshot is not null) disk = snapshot with { Details = ValidateDetails(snapshot.Details, speciesId) };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }

        PokemonDetails details;
        if (disk is not null && _timeProvider.GetUtcNow() - disk.FetchedAt is var age && age >= TimeSpan.Zero && age < TimeSpan.FromDays(30))
            details = disk.Details;
        else
        {
            try
            {
                var pokemon = await GetJsonAsync<JsonElement>(new Uri(RestBaseUri, $"pokemon/{speciesId}"), cancellationToken).ConfigureAwait(false);
                var species = await GetJsonAsync<JsonElement>(new Uri(RestBaseUri, $"pokemon-species/{speciesId}"), cancellationToken).ConfigureAwait(false);
                details = ParseDetails(pokemon, species, speciesId);
                try { AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(new DetailSnapshot(_timeProvider.GetUtcNow(), details), DetailJsonOptions)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Optional cache; keep the network result. */ }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested && disk is not null)
            {
                details = disk.Details;
            }
        }
        lock (_cacheLock) _detailsCache[speciesId] = details;
        return details;
    }

    private static PokemonDetails ParseDetails(JsonElement pokemon, JsonElement species, int id)
    {
        if (Number(pokemon, "id") != id || Number(species, "id") != id || string.IsNullOrWhiteSpace(Text(pokemon, "name")))
            throw new InvalidDataException("Invalid Pokémon detail response.");
        var stats = Rows(pokemon, "stats").Where(row => PokemonStatCalculator.Order.Contains(Name(row, "stat")))
            .GroupBy(row => Name(row, "stat")!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Math.Clamp(Number(group.Last(), "base_stat") ?? 0, 0, 1000));
        var moves = Rows(pokemon, "moves").Where(row => !string.IsNullOrWhiteSpace(Name(row, "move")))
            .Select(row => new PokemonMoveOption(Name(row, "move")!, Rows(row, "version_group_details")
                .Where(method => Name(method, "version_group") == PokemonDetails.PreferredVersionGroup && !string.IsNullOrWhiteSpace(Name(method, "move_learn_method")))
                .Select(method => new PokemonMoveLearnMethod(Name(method, "move_learn_method")!, Math.Clamp(Number(method, "level_learned_at") ?? 0, 0, 100)))
                .Distinct().ToArray()))
            .Where(move => move.LearnMethods.Count > 0).GroupBy(move => move.Name, StringComparer.Ordinal)
            .Select(group => new PokemonMoveOption(group.Key, group.SelectMany(move => move.LearnMethods).Distinct()
                .OrderBy(method => method.Level).ThenBy(method => method.Method, StringComparer.Ordinal).ToArray()))
            .OrderBy(move => move.Name, StringComparer.Ordinal).ToArray();
        return ValidateDetails(new PokemonDetails
        {
            SpeciesId = id, Name = Text(pokemon, "name")!, Height = Number(pokemon, "height") ?? 0,
            Weight = Number(pokemon, "weight") ?? 0, BaseExperience = Number(pokemon, "base_experience"),
            GenderRate = Number(species, "gender_rate") ?? -1,
            Types = Rows(pokemon, "types").OrderBy(row => Number(row, "slot")).Select(row => Name(row, "type"))
                .OfType<string>().Distinct().ToArray(), BaseStats = stats,
            Abilities = Rows(pokemon, "abilities").Where(row => !string.IsNullOrWhiteSpace(Name(row, "ability")))
                .Select(row => new PokemonAbilityOption(Name(row, "ability")!, Number(row, "slot") ?? 1,
                    row.TryGetProperty("is_hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True))
                .Distinct().OrderBy(option => option.Slot).ToArray(), Moves = moves,
        }, id);
    }

    private static PokemonDetails ValidateDetails(PokemonDetails? details, int id)
    {
        if (details is null || details.SpeciesId != id || string.IsNullOrWhiteSpace(details.Name) ||
            details.Types is null || details.BaseStats is null || details.Abilities is null || details.Moves is null ||
            details.Abilities.Any(a => a is null || string.IsNullOrWhiteSpace(a.Name)) ||
            details.Moves.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name) || m.LearnMethods is null ||
                m.LearnMethods.Any(method => method is null || string.IsNullOrWhiteSpace(method.Method))))
            throw new InvalidDataException("Invalid Pokémon detail cache.");
        return details with
        {
            GenderRate = Math.Clamp(details.GenderRate, -1, 8), Height = Math.Max(0, details.Height), Weight = Math.Max(0, details.Weight),
            BaseStats = details.BaseStats.Where(pair => PokemonStatCalculator.Order.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 0, 1000)),
            Abilities = details.Abilities.Where(a => a.Slot is > 0 and <= 3).Distinct().ToArray(),
            Moves = details.Moves.Select(m => m with { LearnMethods = m.LearnMethods.Select(method => method with
                { Level = Math.Clamp(method.Level, 0, 100) }).Distinct().ToArray() }).ToArray(),
        };
    }

    private static IEnumerable<JsonElement> Rows(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object) : [];
    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string? Name(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? Text(value, "name") : null;
}
