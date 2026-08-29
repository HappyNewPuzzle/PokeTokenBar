using System.Net;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed record PokemonSpriteAsset(
    ReadOnlyMemory<byte> Data,
    Uri SourceUri,
    string ContentType,
    bool IsAnimated,
    bool IsShiny);

public sealed class PokemonSpriteLoader
{
    private const string SpriteBaseUrl =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon";
    private const int LastAnimatedSpeciesId = 649;

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly int _memoryLimit;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, byte[]> _memoryCache = [];
    private readonly LinkedList<string> _memoryOrder = [];

    public PokemonSpriteLoader(
        HttpClient httpClient,
        string? cacheDirectory = null,
        int memoryLimit = 64)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = Path.GetFullPath(cacheDirectory ?? GetDefaultCacheDirectory());
        if (memoryLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryLimit));
        }

        _memoryLimit = memoryLimit;
    }

    public string CacheDirectory => _cacheDirectory;

    public static string GetDefaultCacheDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "PokeTokenBar", "sprites");
    }

    public Task<PokemonSpriteAsset?> LoadAsync(
        int pokemonId,
        bool shiny,
        CancellationToken cancellationToken = default) =>
        LoadAsync(pokemonId, preferAnimated: true, shiny, cancellationToken);

    public async Task<PokemonSpriteAsset?> LoadAsync(
        int pokemonId,
        bool preferAnimated,
        bool shiny,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in BuildCandidates(pokemonId, preferAnimated, shiny))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await LoadCandidateBytesAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (bytes is null || !HasExpectedImageSignature(bytes, candidate.IsAnimated))
            {
                continue;
            }

            return new PokemonSpriteAsset(
                bytes,
                candidate.SourceUri,
                candidate.IsAnimated ? "image/gif" : "image/png",
                candidate.IsAnimated,
                candidate.IsShiny);
        }

        return null;
    }

    public static string GetCacheKey(int pokemonId, bool animated, bool shiny) =>
        $"{pokemonId}-{(shiny ? "sh" : string.Empty)}{(animated ? "a" : "s")}";

    public static Uri GetSourceUri(int pokemonId, bool animated, bool shiny)
    {
        var relative = (animated, shiny) switch
        {
            (true, false) => $"versions/generation-v/black-white/animated/{pokemonId}.gif",
            (true, true) => $"versions/generation-v/black-white/animated/shiny/{pokemonId}.gif",
            (false, false) => $"{pokemonId}.png",
            (false, true) => $"shiny/{pokemonId}.png",
        };
        return new Uri($"{SpriteBaseUrl}/{relative}");
    }

    private static IEnumerable<SpriteCandidate> BuildCandidates(
        int pokemonId,
        bool preferAnimated,
        bool shiny)
    {
        var supportsAnimation = pokemonId is >= 1 and <= LastAnimatedSpeciesId;
        if (preferAnimated && supportsAnimation)
        {
            yield return Candidate(pokemonId, animated: true, shiny);
        }

        yield return Candidate(pokemonId, animated: false, shiny);

        if (!shiny)
        {
            yield break;
        }

        if (preferAnimated && supportsAnimation)
        {
            yield return Candidate(pokemonId, animated: true, shiny: false);
        }

        yield return Candidate(pokemonId, animated: false, shiny: false);
    }

    private async Task<byte[]?> LoadCandidateBytesAsync(
        SpriteCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (TryGetMemory(candidate.CacheKey, out var memoryBytes))
        {
            return memoryBytes;
        }

        var filePath = Path.Combine(
            _cacheDirectory,
            $"{candidate.CacheKey}.{(candidate.IsAnimated ? "gif" : "png")}");
        try
        {
            if (File.Exists(filePath))
            {
                var diskBytes = await File.ReadAllBytesAsync(filePath, cancellationToken)
                    .ConfigureAwait(false);
                Remember(candidate.CacheKey, diskBytes);
                return diskBytes;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            // A cache read failure is a cache miss, matching Swift's try? behavior.
        }
        catch (UnauthorizedAccessException)
        {
            // A cache read failure is a cache miss, matching Swift's try? behavior.
        }

        byte[] downloaded;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate.SourceUri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            downloaded = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (downloaded.Length == 0)
            {
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        TryWriteDiskCache(filePath, downloaded);
        Remember(candidate.CacheKey, downloaded);
        return downloaded;
    }

    private bool TryGetMemory(string key, out byte[] bytes)
    {
        lock (_cacheLock)
        {
            if (!_memoryCache.TryGetValue(key, out bytes!))
            {
                return false;
            }

            Touch(key);
            return true;
        }
    }

    private void Remember(string key, byte[] bytes)
    {
        lock (_cacheLock)
        {
            _memoryCache[key] = bytes;
            Touch(key);
            while (_memoryOrder.Count > _memoryLimit)
            {
                var oldest = _memoryOrder.First!.Value;
                _memoryOrder.RemoveFirst();
                _memoryCache.Remove(oldest);
            }
        }
    }

    private void Touch(string key)
    {
        var existing = _memoryOrder.Find(key);
        if (existing is not null)
        {
            _memoryOrder.Remove(existing);
        }

        _memoryOrder.AddLast(key);
    }

    private static void TryWriteDiskCache(string filePath, byte[] bytes)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, filePath, overwrite: true);
            temporaryPath = null;
        }
        catch (IOException)
        {
            // Sprite cache persistence is best effort in the Swift implementation.
        }
        catch (UnauthorizedAccessException)
        {
            // Sprite cache persistence is best effort in the Swift implementation.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup only.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static bool HasExpectedImageSignature(byte[] bytes, bool animated)
    {
        if (animated)
        {
            return bytes.Length >= 6 &&
                   bytes[0] == (byte)'G' &&
                   bytes[1] == (byte)'I' &&
                   bytes[2] == (byte)'F' &&
                   bytes[3] == (byte)'8' &&
                   (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') &&
                   bytes[5] == (byte)'a';
        }

        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.AsSpan().StartsWith(pngSignature);
    }

    private static SpriteCandidate Candidate(int pokemonId, bool animated, bool shiny) =>
        new(
            GetCacheKey(pokemonId, animated, shiny),
            GetSourceUri(pokemonId, animated, shiny),
            animated,
            shiny);

    private sealed record SpriteCandidate(
        string CacheKey,
        Uri SourceUri,
        bool IsAnimated,
        bool IsShiny);
}
