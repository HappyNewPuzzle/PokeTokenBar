using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalCursorUsageProvider : IUsageProvider
{
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<string> _roots;
    private readonly CursorDashboardClient _dashboard;
    private readonly Func<string?> _credential;
    private readonly TimeSpan _cacheLifetime;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CachedEntries? _cache;

    public LocalCursorUsageProvider()
        : this(SharedHttpClient)
    {
    }

    public LocalCursorUsageProvider(HttpClient httpClient)
        : this(httpClient, GetDefaultRoots(), credential: null)
    {
    }

    public LocalCursorUsageProvider(HttpClient httpClient, IEnumerable<string> roots)
        : this(httpClient, roots, credential: null)
    {
    }

    internal LocalCursorUsageProvider(
        HttpClient httpClient,
        IEnumerable<string> roots,
        Func<string?>? credential,
        TimeSpan? requestTimeout = null,
        TimeSpan? cacheLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(roots);
        _roots = LocalUsageSupport.NormalizeRoots(roots);
        _credential = credential ?? FindCredential;
        _cacheLifetime = cacheLifetime ?? CacheLifetime;
        _dashboard = new CursorDashboardClient(httpClient, requestTimeout);
    }

    public string Id => "cursor";
    public string DisplayName => "Cursor";
    public bool ReportsCost => false;

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
        FetchDailyAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    internal async Task<DailyUsage?> FetchDailyAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var entries = await LoadEntriesAsync(now, timeZone, firstDayOfWeek, cancellationToken)
            .ConfigureAwait(false);
        return LocalUsageSupport.Daily(entries, LocalUsageSupport.LocalDate(now, timeZone));
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken = default) =>
        FetchEnrichmentAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    internal async Task<ProviderEnrichment> FetchEnrichmentAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var entries = await LoadEntriesAsync(now, timeZone, firstDayOfWeek, cancellationToken)
            .ConfigureAwait(false);
        return LocalUsageSupport.Enrichment(entries, now, timeZone, firstDayOfWeek);
    }

    public static IReadOnlyList<string> GetDefaultRoots(string? appData = null)
    {
        var overrideValue = Environment.GetEnvironmentVariable("CURSOR_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return LocalUsageSupport.NormalizeRoots(overrideValue
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return LocalUsageSupport.NormalizeRoots(
        [
            Path.Combine(appData, "Cursor", "User", "globalStorage"),
            Path.Combine(appData, "Cursor Nightly", "User", "globalStorage"),
        ]);
    }

    internal static LocalUsageEntry? ParseBubble(
        string key,
        string json,
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tokenCount", out var tokens) ||
                !TryTimestamp(root, "createdAt", out var timestamp) ||
                timestamp < modifiedSince)
            {
                return null;
            }

            var input = NonNegativeInt64(tokens, "inputTokens");
            var output = NonNegativeInt64(tokens, "outputTokens");
            if (input + output == 0)
            {
                return null;
            }

            return new LocalUsageEntry(
                $"cursor|{key}",
                timestamp,
                LocalUsageSupport.LocalDate(timestamp, timeZone),
                input,
                output,
                CacheWrite: 0,
                CacheRead: 0,
                Cost: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<LocalUsageEntry>> LoadEntriesAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken)
    {
        var since = LocalUsageSupport.EnrichmentScanStart(now, timeZone, firstDayOfWeek);
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null &&
                now - _cache.LoadedAt < _cacheLifetime &&
                _cache.CoveredSince <= since)
            {
                return _cache.Entries.Where(entry => entry.Timestamp >= since).ToArray();
            }

            CursorDashboardResult api;
            if (Environment.GetEnvironmentVariable("CURSOR_USAGE_API") == "0")
            {
                api = CursorDashboardResult.Unavailable;
            }
            else
            {
                var token = _credential()?.Trim();
                api = string.IsNullOrEmpty(token)
                    ? CursorDashboardResult.Unavailable
                    : await _dashboard.FetchAsync(token, since, now, timeZone, cancellationToken)
                        .ConfigureAwait(false);
            }

            LocalLoadResult loaded;
            if (api.IsAuthoritative)
            {
                loaded = new LocalLoadResult(api.Entries, IsAvailable: true);
            }
            else
            {
                loaded = await Task.Run(
                    () => ReadLocalEntries(since, timeZone, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!loaded.IsAvailable && _cache is not null)
            {
                return _cache.Entries.Where(entry => entry.Timestamp >= since).ToArray();
            }

            var entries = LocalUsageSupport.Deduplicate(loaded.Entries);
            _cache = new CachedEntries(now, since, entries);
            return entries;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private string? FindCredential()
    {
        var environment = Environment.GetEnvironmentVariable("CURSOR_SESSION_TOKEN")?.Trim();
        if (!string.IsNullOrEmpty(environment))
        {
            return environment;
        }

        foreach (var root in _roots)
        {
            var database = DatabasePath(root);
            if (!File.Exists(database))
            {
                continue;
            }

            try
            {
                var token = WithDatabaseCopy(database, connection =>
                    connection.ReadTextScalar(
                        "SELECT value FROM ItemTable WHERE key = 'cursorAuth/accessToken' LIMIT 1"));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token.Trim();
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Authentication discovery is best effort; SQLite usage still remains available.
            }
        }

        return null;
    }

    private LocalLoadResult ReadLocalEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        var opened = false;
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = DatabasePath(root);
            if (!File.Exists(database))
            {
                continue;
            }

            try
            {
                var rows = WithDatabaseCopy(database, connection => connection.ReadTextRows(
                    "SELECT key, value FROM cursorDiskKV WHERE key GLOB 'bubbleId:*'",
                    2,
                    prepareErrorIsFailure: true));
                opened = true;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row[0] is { } key && row[1] is { } json &&
                        ParseBubble(key, json, modifiedSince, timeZone) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // One bad/locked installation must not hide another root or provider.
            }
        }

        return new LocalLoadResult(LocalUsageSupport.Deduplicate(entries), opened);
    }

    private static string DatabasePath(string root) =>
        string.Equals(Path.GetExtension(root), ".vscdb", StringComparison.OrdinalIgnoreCase)
            ? root
            : Path.Combine(root, "state.vscdb");

    internal static T WithDatabaseCopy<T>(string database, Func<SqliteConnection, T> action)
    {
        var temporary = Directory.CreateTempSubdirectory("PokeTokenBar-Cursor-");
        try
        {
            var copy = Path.Combine(temporary.FullName, Path.GetFileName(database));
            File.Copy(database, copy);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(database + suffix))
                {
                    File.Copy(database + suffix, copy + suffix);
                }
            }

            using var connection = SqliteConnection.OpenReadOnly(copy);
            return action(connection);
        }
        finally
        {
            try
            {
                temporary.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked temporary copy is harmless and never touches Cursor's database.
            }
        }
    }

    internal static bool TryTimestamp(JsonElement parent, string property, out DateTimeOffset value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            return TryEpoch(number, out value);
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = element.GetString();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? TryEpoch(number, out value)
            : DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value);
    }

    private static bool TryEpoch(double raw, out DateTimeOffset value)
    {
        value = default;
        if (!double.IsFinite(raw))
        {
            return false;
        }

        try
        {
            if (raw > 1_000_000_000_000)
            {
                value = DateTimeOffset.FromUnixTimeMilliseconds((long)raw);
                return true;
            }

            if (raw >= 1_000_000_000)
            {
                value = DateTimeOffset.FromUnixTimeSeconds((long)raw);
                return true;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return false;
    }

    internal static long NonNegativeInt64(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var value))
        {
            return 0;
        }

        long result;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result))
        {
            return Math.Max(0, result);
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(
                value.GetString()?.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result))
        {
            return Math.Max(0, result);
        }

        return 0;
    }

    private sealed record CachedEntries(
        DateTimeOffset LoadedAt,
        DateTimeOffset CoveredSince,
        IReadOnlyList<LocalUsageEntry> Entries);

    private sealed record LocalLoadResult(
        IReadOnlyList<LocalUsageEntry> Entries,
        bool IsAvailable);
}

internal sealed record CursorDashboardResult(
    IReadOnlyList<LocalUsageEntry> Entries,
    bool IsAuthoritative)
{
    public static CursorDashboardResult Unavailable { get; } = new([], false);
}

internal sealed class CursorDashboardClient
{
    internal static readonly Uri Endpoint = new(
        "https://cursor.com/api/dashboard/get-filtered-usage-events");
    private const int PageSize = 100;
    private const int MaxPages = 200;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FetchDeadline = TimeSpan.FromSeconds(120);
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public CursorDashboardClient(HttpClient httpClient, TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    public async Task<CursorDashboardResult> FetchAsync(
        string token,
        DateTimeOffset modifiedSince,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var collected = new List<LocalUsageEntry>();
        var started = DateTimeOffset.UtcNow;
        var authMode = CursorAuthMode.Cookie;
        var globalIndex = 0;
        for (var page = 1; page <= MaxPages;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - started >= FetchDeadline)
            {
                return CursorDashboardResult.Unavailable;
            }

            using var request = CreateRequest(token, authMode, modifiedSince, now, page);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CursorDashboardResult.Unavailable;
            }
            catch (HttpRequestException)
            {
                return CursorDashboardResult.Unavailable;
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (authMode == CursorAuthMode.Cookie)
                    {
                        authMode = CursorAuthMode.Bearer;
                        continue;
                    }

                    return CursorDashboardResult.Unavailable;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CursorDashboardResult.Unavailable;
                }

                JsonDocument document;
                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                        .ConfigureAwait(false);
                    document = await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return CursorDashboardResult.Unavailable;
                }
                catch (Exception exception) when (
                    exception is JsonException or HttpRequestException or IOException)
                {
                    return CursorDashboardResult.Unavailable;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!TryEvents(root, out var events))
                    {
                        return CursorDashboardResult.Unavailable;
                    }

                    foreach (var item in events.EnumerateArray())
                    {
                        if (ParseEvent(item, globalIndex, modifiedSince, timeZone) is { } entry)
                        {
                            collected.Add(entry);
                        }

                        globalIndex++;
                    }

                    if (!HasNextPage(root, page, events.GetArrayLength()))
                    {
                        return new CursorDashboardResult(
                            LocalUsageSupport.Deduplicate(collected),
                            IsAuthoritative: true);
                    }

                    if (events.GetArrayLength() == 0)
                    {
                        return CursorDashboardResult.Unavailable;
                    }
                }
            }

            page++;
        }

        return CursorDashboardResult.Unavailable;
    }

    internal static HttpRequestMessage CreateRequest(
        string token,
        CursorAuthMode authMode,
        DateTimeOffset modifiedSince,
        DateTimeOffset now,
        int page)
    {
        var json = JsonSerializer.Serialize(new
        {
            teamId = 0,
            startDate = modifiedSince.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            endDate = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            page,
            pageSize = PageSize,
        });
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Referrer = new Uri("https://cursor.com/dashboard/usage");
        request.Headers.TryAddWithoutValidation("Origin", "https://cursor.com");
        if (authMode == CursorAuthMode.Cookie)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                $"WorkosCursorSessionToken={WorkosSessionCookie(token)}");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }

    internal static LocalUsageEntry? ParseEvent(
        JsonElement item,
        int rowIndex,
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone)
    {
        if (!LocalCursorUsageProvider.TryTimestamp(item, "timestamp", out var timestamp) ||
            timestamp < modifiedSince)
        {
            return null;
        }

        var usage = item.TryGetProperty("tokenUsage", out var value) &&
                    value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var input = usage.ValueKind == JsonValueKind.Object
            ? LocalCursorUsageProvider.NonNegativeInt64(usage, "inputTokens")
            : 0;
        var output = usage.ValueKind == JsonValueKind.Object
            ? LocalCursorUsageProvider.NonNegativeInt64(usage, "outputTokens")
            : 0;
        var cacheWrite = usage.ValueKind == JsonValueKind.Object
            ? LocalCursorUsageProvider.NonNegativeInt64(usage, "cacheWriteTokens")
            : 0;
        var cacheRead = usage.ValueKind == JsonValueKind.Object
            ? LocalCursorUsageProvider.NonNegativeInt64(usage, "cacheReadTokens")
            : 0;
        if (input + output + cacheWrite + cacheRead == 0)
        {
            return null;
        }

        var model = StringValue(item, "model") ?? "unknown";
        var stableId = StringValue(item, "id") ??
                       StringValue(item, "eventId") ??
                       StringValue(item, "requestId");
        var stamp = StringValue(item, "timestamp") ??
                    timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var id = stableId is null
            ? $"cursor|api|{stamp}|{model}|{rowIndex}"
            : $"cursor|api|{stableId}";
        var cost = 0d;
        if (usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty("totalCents", out var cents) &&
            TryDouble(cents, out var parsedCost) &&
            double.IsFinite(parsedCost))
        {
            cost = parsedCost / 100;
        }

        return new LocalUsageEntry(
            id,
            timestamp,
            LocalUsageSupport.LocalDate(timestamp, timeZone),
            input,
            output,
            cacheWrite,
            cacheRead,
            cost);
    }

    internal static bool HasNextPage(JsonElement root, int page, int eventCount)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("pagination", out var pagination) &&
            pagination.ValueKind == JsonValueKind.Object)
        {
            if (pagination.TryGetProperty("hasNextPage", out var hasNext) &&
                hasNext.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return hasNext.GetBoolean();
            }

            if (pagination.TryGetProperty("numPages", out var pages) &&
                pages.TryGetInt32(out var count))
            {
                return page < count;
            }
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("totalUsageEventsCount", out var total) &&
            total.TryGetInt32(out var totalCount))
        {
            return page * PageSize < totalCount;
        }

        return eventCount >= PageSize;
    }

    internal static string WorkosSessionCookie(string accessToken)
    {
        var decoded = Uri.UnescapeDataString(accessToken);
        if (decoded.Contains("::", StringComparison.Ordinal))
        {
            return accessToken;
        }

        var subject = JwtSubject(accessToken);
        return subject is null ? accessToken : $"{subject}::{accessToken}";
    }

    internal static string? JwtSubject(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return StringValue(document.RootElement, "sub");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool TryEvents(JsonElement root, out JsonElement events)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            events = default;
            return false;
        }

        foreach (var key in new[] { "usageEventsDisplay", "usageEvents", "events" })
        {
            if (root.TryGetProperty(key, out events) && events.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        events = default;
        return false;
    }

    private static string? StringValue(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var value))
        {
            return null;
        }

        var result = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static bool TryDouble(JsonElement value, out double result) =>
        value.ValueKind == JsonValueKind.Number
            ? value.TryGetDouble(out result)
            : double.TryParse(
                value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
}

internal enum CursorAuthMode
{
    Cookie,
    Bearer,
}
