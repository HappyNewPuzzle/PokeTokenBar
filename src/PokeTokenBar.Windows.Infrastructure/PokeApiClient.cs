using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class PokeApiClient : IPokeApiClient
{
    private static readonly Uri RestBaseUri = new("https://pokeapi.co/api/v2/");
    private static readonly Uri GraphQlUri = new("https://graphql.pokeapi.co/v1beta2");
    private static readonly HashSet<string> SupportedLanguageCodes =
        ["ko", "en", "ja-Hrkt", "ja", "es", "fr", "pt"];

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;
    private readonly string _baseIndexCachePath;
    private readonly TimeProvider _timeProvider;
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, SpeciesDto> _speciesCache = [];
    private readonly Dictionary<int, EvoLine> _lineCache = [];
    private IReadOnlyList<BaseSpecies>? _baseIndexCache;
    private Task<IReadOnlyList<BaseSpecies>>? _baseIndexRefreshTask;

    private static readonly TimeSpan BaseIndexFreshness = TimeSpan.FromDays(30);

    public PokeApiClient(
        HttpClient httpClient,
        TimeSpan? requestTimeout = null,
        string? baseIndexCachePath = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        _baseIndexCachePath = baseIndexCachePath ?? GetDefaultBaseIndexCachePath();
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public async Task<EvoLine> GetLineAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (_lineCache.TryGetValue(baseSpeciesId, out var cached))
            {
                return cached;
            }
        }

        var baseSpecies = await GetSpeciesAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
        if (!TryValidateEvolutionChainUri(baseSpecies.EvolutionChain.Url, out var chainUri))
        {
            throw new InvalidDataException("The evolution-chain URL was not a trusted PokeAPI URL.");
        }

        var chain = await GetJsonAsync<ChainDto>(chainUri, cancellationToken).ConfigureAwait(false);
        var tree = ToNode(chain.Chain);
        var names = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        foreach (var id in AllIds(tree).Distinct())
        {
            var species = await GetSpeciesAsync(id, cancellationToken).ConfigureAwait(false);
            names[id] = species.Names
                .Where(name => SupportedLanguageCodes.Contains(name.Language.Name))
                .GroupBy(name => name.Language.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Name, StringComparer.Ordinal);
        }

        var line = new EvoLine(
            baseSpeciesId,
            tree,
            PokemonRarityRules.From(
                baseSpecies.CaptureRate,
                baseSpecies.IsLegendary,
                baseSpecies.IsMythical),
            names);

        lock (_cacheLock)
        {
            _lineCache[baseSpeciesId] = line;
        }

        return line;
    }

    public async Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (_baseIndexCache is not null)
            {
                return _baseIndexCache;
            }
        }

        var disk = await TryReadBaseIndexCacheAsync(cancellationToken).ConfigureAwait(false);
        if (disk is not null)
        {
            lock (_cacheLock)
            {
                _baseIndexCache = disk.Entries;
            }

            if (_timeProvider.GetUtcNow() - disk.FetchedAt >= BaseIndexFreshness)
            {
                StartBackgroundBaseIndexRefresh();
            }

            return disk.Entries;
        }

        return await GetOrStartBaseIndexRefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<BaseSpecies>> GetOrStartBaseIndexRefreshAsync(
        CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_baseIndexRefreshTask is null)
            {
                _baseIndexRefreshTask = Task.Run(
                    () => FetchAndCacheBaseIndexAsync(cancellationToken),
                    CancellationToken.None);
                _ = ClearCompletedBaseIndexRefreshAsync(_baseIndexRefreshTask);
            }

            return _baseIndexRefreshTask;
        }
    }

    private void StartBackgroundBaseIndexRefresh()
    {
        Task<IReadOnlyList<BaseSpecies>> refresh;
        lock (_cacheLock)
        {
            if (_baseIndexRefreshTask is null)
            {
                _baseIndexRefreshTask = Task.Run(
                    () => FetchAndCacheBaseIndexAsync(CancellationToken.None),
                    CancellationToken.None);
                _ = ClearCompletedBaseIndexRefreshAsync(_baseIndexRefreshTask);
            }

            refresh = _baseIndexRefreshTask;
        }

        _ = ObserveBackgroundRefreshAsync(refresh);
    }

    private async Task ClearCompletedBaseIndexRefreshAsync(Task<IReadOnlyList<BaseSpecies>> refresh)
    {
        try
        {
            await refresh.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (_cacheLock)
            {
                if (ReferenceEquals(_baseIndexRefreshTask, refresh))
                {
                    _baseIndexRefreshTask = null;
                }
            }
        }
    }

    private static async Task ObserveBackgroundRefreshAsync(Task<IReadOnlyList<BaseSpecies>> refresh)
    {
        try
        {
            await refresh.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A stale snapshot remains usable. A background cache refresh must not crash the app.
        }
    }

    private async Task<IReadOnlyList<BaseSpecies>> FetchAndCacheBaseIndexAsync(
        CancellationToken cancellationToken)
    {
        var entries = await FetchBaseIndexAsync(cancellationToken).ConfigureAwait(false);
        lock (_cacheLock)
        {
            _baseIndexCache = entries;
        }

        try
        {
            await WriteBaseIndexCacheAsync(
                    new BaseIndexSnapshot(_timeProvider.GetUtcNow(), entries),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The network result is still valid in memory even if the optional disk cache cannot be written.
        }

        return entries;
    }

    private async Task<IReadOnlyList<BaseSpecies>> FetchBaseIndexAsync(
        CancellationToken cancellationToken)
    {
        var query =
            $"{{ pokemonspecies(where: {{evolves_from_species_id: {{_is_null: true}}, id: {{_lte: {PokemonAssets.LastAnimatedSpeciesId}, _neq: {PokemonOdds.DittoSpeciesId}}}}}, order_by: {{id: asc}}) {{ id capture_rate }} }}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["query"] = query });
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUri)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var timeout = CreateTimeoutSource(cancellationToken);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content
            .ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        var decoded = await JsonSerializer
            .DeserializeAsync<GraphQlBaseResponse>(responseStream, JsonOptions, timeout.Token)
            .ConfigureAwait(false)
            ?? throw new JsonException("PokeAPI returned an empty GraphQL response.");
        var entries = decoded.Data.PokemonSpecies
            .Select(row => new BaseSpecies(row.Id, row.CaptureRate))
            .ToArray();
        if (entries.Length == 0)
        {
            throw new InvalidDataException("PokeAPI returned an empty base-species index.");
        }

        return entries;
    }

    private async Task<BaseIndexSnapshot?> TryReadBaseIndexCacheAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                _baseIndexCachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<BaseIndexSnapshot>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return snapshot is { Entries.Count: > 0 } ? snapshot : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task WriteBaseIndexCacheAsync(
        BaseIndexSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_baseIndexCachePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _baseIndexCachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _baseIndexCachePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string GetDefaultBaseIndexCachePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeTokenBar",
            "base-index.json");

    public async Task<BaseSpecies?> GetBaseSpeciesAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id == PokemonOdds.DittoSpeciesId)
        {
            return null;
        }

        var species = await GetSpeciesAsync(id, cancellationToken).ConfigureAwait(false);
        return species.EvolvesFromSpecies is null
            ? new BaseSpecies(id, species.CaptureRate)
            : null;
    }

    public static bool TryValidateEvolutionChainUri(string? raw, out Uri uri)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out var parsed) &&
            parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            parsed.Host.Equals("pokeapi.co", StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private async Task<SpeciesDto> GetSpeciesAsync(int id, CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_speciesCache.TryGetValue(id, out var cached))
            {
                return cached;
            }
        }

        var species = await GetJsonAsync<SpeciesDto>(
                new Uri(RestBaseUri, $"pokemon-species/{id}"),
                cancellationToken)
            .ConfigureAwait(false);
        lock (_cacheLock)
        {
            _speciesCache[id] = species;
        }

        return species;
    }

    private async Task<T> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var timeout = CreateTimeoutSource(cancellationToken);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content
            .ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions, timeout.Token)
            .ConfigureAwait(false)
            ?? throw new JsonException("PokeAPI returned an empty response.");
    }

    private CancellationTokenSource CreateTimeoutSource(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        return timeout;
    }

    private static EvoNode ToNode(ChainLink link) =>
        new(ParseSpeciesId(link.Species.Url), link.EvolvesTo.Select(ToNode).ToArray());

    private static IEnumerable<int> AllIds(EvoNode node)
    {
        yield return node.SpeciesId;
        foreach (var child in node.Children)
        {
            foreach (var id in AllIds(child))
            {
                yield return id;
            }
        }
    }

    private static int ParseSpeciesId(string? speciesUrl)
    {
        if (Uri.TryCreate(speciesUrl, UriKind.Absolute, out var uri) &&
            int.TryParse(uri.Segments.LastOrDefault()?.Trim('/'), out var id))
        {
            return id;
        }

        throw new JsonException("Evolution-chain species URL does not contain an ID.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private sealed record SpeciesDto
    {
        [JsonPropertyName("capture_rate")]
        public required int CaptureRate { get; init; }

        [JsonPropertyName("is_legendary")]
        public required bool IsLegendary { get; init; }

        [JsonPropertyName("is_mythical")]
        public required bool IsMythical { get; init; }

        [JsonPropertyName("names")]
        public required IReadOnlyList<NameDto> Names { get; init; }

        [JsonPropertyName("evolution_chain")]
        public required UrlReference EvolutionChain { get; init; }

        [JsonPropertyName("evolves_from_species")]
        public NamedReference? EvolvesFromSpecies { get; init; }
    }

    private sealed record NameDto
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("language")]
        public required NamedReference Language { get; init; }
    }

    private sealed record NamedReference
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }

    private sealed record UrlReference
    {
        [JsonPropertyName("url")]
        public required string Url { get; init; }
    }

    private sealed record ChainDto
    {
        [JsonPropertyName("chain")]
        public required ChainLink Chain { get; init; }
    }

    private sealed record ChainLink
    {
        [JsonPropertyName("species")]
        public required NamedReference Species { get; init; }

        [JsonPropertyName("evolves_to")]
        public required IReadOnlyList<ChainLink> EvolvesTo { get; init; }
    }

    private sealed record GraphQlBaseResponse
    {
        [JsonPropertyName("data")]
        public required GraphQlData Data { get; init; }
    }

    private sealed record GraphQlData
    {
        [JsonPropertyName("pokemonspecies")]
        public required IReadOnlyList<GraphQlRow> PokemonSpecies { get; init; }
    }

    private sealed record GraphQlRow
    {
        [JsonPropertyName("id")]
        public required int Id { get; init; }

        [JsonPropertyName("capture_rate")]
        public required int CaptureRate { get; init; }
    }

    private sealed record BaseIndexSnapshot(
        DateTimeOffset FetchedAt,
        IReadOnlyList<BaseSpecies> Entries);
}
