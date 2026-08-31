using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalCursorUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Cursor-{Guid.NewGuid():N}");

    public LocalCursorUsageProviderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MetadataMatchesMacOSProvider()
    {
        var provider = Provider(new QueueHandler(), credential: null);
        Assert.Equal("cursor", provider.Id);
        Assert.Equal("Cursor", provider.DisplayName);
        Assert.False(provider.ReportsCost);
    }

    [Fact]
    public void DefaultRootsCoverStableAndNightlyGlobalStorage()
    {
        var appData = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester", "AppData", "Roaming");
        Assert.Equal(
            [
                Path.Combine(appData, "Cursor", "User", "globalStorage"),
                Path.Combine(appData, "Cursor Nightly", "User", "globalStorage"),
            ],
            LocalCursorUsageProvider.GetDefaultRoots(appData));
    }

    [Fact]
    public async Task MissingDatabaseAndCredentialAreUnavailableWithoutFailure()
    {
        var handler = new QueueHandler();
        Assert.Null(await Daily(Provider(handler, credential: null)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void BubbleMapsInputOutputAndStableIdentity()
    {
        var entry = ParseBubble("bubbleId:one", Bubble(Now, 12, 34, "gpt"));
        Assert.Equal("cursor|bubbleId:one", entry!.Id);
        Assert.Equal(12, entry.Input);
        Assert.Equal(34, entry.Output);
        Assert.Equal(46, entry.TotalTokens);
        Assert.Equal(0, entry.CacheRead);
        Assert.Equal(0, entry.Cost);
    }

    [Fact]
    public void BubbleAcceptsIsoMillisecondsAndSecondsTimestamps()
    {
        foreach (var timestamp in new[]
                 {
                     "2026-08-30T10:00:00.123Z",
                     "1788084000000",
                     "1788084000",
                 })
        {
            var json = $$"""{"tokenCount":{"inputTokens":1},"createdAt":"{{timestamp}}"}""";
            Assert.NotNull(ParseBubble("bubbleId:time", json));
        }
    }

    [Fact]
    public void BubbleMissingModelUsesNoInventedTokenFields()
    {
        var entry = ParseBubble("bubbleId:unknown", Bubble(Now, 7, 0, model: null));
        Assert.NotNull(entry);
        Assert.Equal(0, entry!.CacheWrite);
        Assert.Equal(0, entry.CacheRead);
    }

    [Fact]
    public void MalformedIncompleteOrZeroBubbleIsIgnored()
    {
        string[] invalid =
        [
            "not json",
            "{}",
            "{\"tokenCount\":{},\"createdAt\":\"2026-08-30T10:00:00Z\"}",
            "{\"tokenCount\":{\"inputTokens\":1}}",
        ];
        Assert.All(invalid, json => Assert.Null(ParseBubble("bubbleId:bad", json)));
    }

    [Fact]
    public void BubbleBeforeRequestedWindowIsIgnored() =>
        Assert.Null(LocalCursorUsageProvider.ParseBubble(
            "bubbleId:old", Bubble(Now.AddMonths(-1), 1, 0), Now.AddDays(-1), Utc));

    [Fact]
    public async Task DashboardRequestMatchesEndpointMethodHeadersAndEpochRange()
    {
        using var request = CursorDashboardClient.CreateRequest(
            "opaque", CursorAuthMode.Cookie, Now.AddDays(-1), Now, 3);
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(CursorDashboardClient.Endpoint, request.RequestUri);
        Assert.Equal("https://cursor.com", Assert.Single(request.Headers.GetValues("Origin")));
        Assert.Equal("https://cursor.com/dashboard/usage", request.Headers.Referrer!.ToString());
        Assert.Equal(Now.AddDays(-1).ToUnixTimeMilliseconds().ToString(), body.GetProperty("startDate").GetString());
        Assert.Equal(Now.ToUnixTimeMilliseconds().ToString(), body.GetProperty("endDate").GetString());
        Assert.Equal(3, body.GetProperty("page").GetInt32());
        Assert.Equal(100, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public void JwtAccessTokenBecomesWorkosSubjectCookie()
    {
        var payload = Base64Url("{\"sub\":\"user_01TEST\"}");
        var jwt = $"header.{payload}.signature";
        Assert.Equal($"user_01TEST::{jwt}", CursorDashboardClient.WorkosSessionCookie(jwt));
    }

    [Fact]
    public void ExistingOrOpaqueCookieIsPreserved()
    {
        foreach (var token in new[] { "opaque-token", "user::jwt", "user%3A%3Ajwt" })
        {
            Assert.Equal(token, CursorDashboardClient.WorkosSessionCookie(token));
        }
    }

    [Fact]
    public void DashboardEventMapsFourBucketsCostAndTimestamp()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            timestamp = Now.ToUnixTimeMilliseconds().ToString(),
            model = "composer",
            tokenUsage = new
            {
                inputTokens = 126,
                outputTokens = 450,
                cacheWriteTokens = 6112,
                cacheReadTokens = 11964,
                totalCents = "250.5",
            },
        }));
        var entry = CursorDashboardClient.ParseEvent(document.RootElement, 0, Now.AddDays(-1), Utc)!;
        Assert.Equal(126, entry.Input);
        Assert.Equal(450, entry.Output);
        Assert.Equal(6112, entry.CacheWrite);
        Assert.Equal(11964, entry.CacheRead);
        Assert.Equal(2.505, entry.Cost, 3);
        Assert.Equal(Now, entry.Timestamp);
    }

    [Fact]
    public void DashboardEventPrefersAvailableStableId()
    {
        foreach (var key in new[] { "id", "eventId", "requestId" })
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [key] = "stable",
                ["timestamp"] = "1788084000000",
                ["tokenUsage"] = new { inputTokens = 1 },
            }));
            var entry = CursorDashboardClient.ParseEvent(document.RootElement, 99, Now.AddDays(-1), Utc)!;
            Assert.Equal("cursor|api|stable", entry.Id);
        }
    }

    [Fact]
    public void DashboardFallbackIdentityUsesGlobalRowIndex()
    {
        using var document = JsonDocument.Parse(
            """{"timestamp":"1788084000000","model":"gpt","tokenUsage":{"inputTokens":1}}""");
        var first = CursorDashboardClient.ParseEvent(document.RootElement, 0, Now.AddDays(-1), Utc)!;
        var second = CursorDashboardClient.ParseEvent(document.RootElement, 100, Now.AddDays(-1), Utc)!;
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DashboardCountersClampNegativeAndAcceptCommaStrings()
    {
        using var document = JsonDocument.Parse(
            """{"timestamp":"1788084000000","tokenUsage":{"inputTokens":-5,"outputTokens":"1,234"}}""");
        var entry = CursorDashboardClient.ParseEvent(document.RootElement, 0, Now.AddDays(-1), Utc)!;
        Assert.Equal(0, entry.Input);
        Assert.Equal(1234, entry.Output);
    }

    [Fact]
    public void PaginationMatchesDashboardVariants()
    {
        (string Json, int Page, int Count, bool Expected)[] cases =
        [
            ("{\"pagination\":{\"hasNextPage\":true}}", 1, 1, true),
            ("{\"pagination\":{\"numPages\":2}}", 1, 1, true),
            ("{\"totalUsageEventsCount\":239}", 3, 39, false),
            ("{}", 1, 100, true),
            ("{}", 1, 42, false),
        ];
        foreach (var item in cases)
        {
            using var document = JsonDocument.Parse(item.Json);
            Assert.Equal(
                item.Expected,
                CursorDashboardClient.HasNextPage(document.RootElement, item.Page, item.Count));
        }
    }

    [Theory]
    [InlineData("usageEventsDisplay")]
    [InlineData("usageEvents")]
    [InlineData("events")]
    public async Task DashboardAcceptsAllUpstreamEventEnvelopeKeys(string key)
    {
        var handler = new QueueHandler(_ => JsonResponse(
            $$"""{"{{key}}":[{{Event("evt", Now, 7)}}],"totalUsageEventsCount":1}"""));
        Assert.Equal(7, (await Daily(Provider(handler)))!.TotalTokens);
    }

    [Fact]
    public async Task DashboardPaginatesAndDeduplicatesStableEventsByMaximumUsage()
    {
        var handler = new QueueHandler(call => call == 1
            ? JsonResponse("{\"events\":[" + Event("same", Now, 10) +
                           "],\"pagination\":{\"hasNextPage\":true}}")
            : JsonResponse("{\"events\":[" + Event("same", Now, 25) +
                           "],\"pagination\":{\"hasNextPage\":false}}"));
        Assert.Equal(25, (await Daily(Provider(handler)))!.TotalTokens);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DashboardRetriesBearerAfterCookieAuthRejection()
    {
        string? authorization = null;
        var handler = new QueueHandler((call, request) =>
        {
            if (call == 1)
            {
                Assert.True(request.Headers.Contains("Cookie"));
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse($$"""{"events":[{{Event("bearer", Now, 8)}}]}""");
        });
        Assert.Equal(8, (await Daily(Provider(handler)))!.TotalTokens);
        Assert.Equal("Bearer token", authorization);
    }

    [Fact]
    public async Task ApiFailureFallsBackToSQLite()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 22, 0)));
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.Equal(22, (await Daily(Provider(handler)))!.TotalTokens);
    }

    [Fact]
    public async Task MalformedApiResponseFallsBackToSQLite()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 23, 0)));
        var handler = new QueueHandler(_ => JsonResponse("{\"unexpected\":[]}"));
        Assert.Equal(23, (await Daily(Provider(handler)))!.TotalTokens);
    }

    [Fact]
    public async Task MissingCredentialFallsBackWithoutNetworkRequest()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 24, 0)));
        var handler = new QueueHandler(_ => throw new InvalidOperationException());
        Assert.Equal(24, (await Daily(Provider(handler, credential: null)))!.TotalTokens);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AuthoritativeApiUsageSuppressesSQLiteInsteadOfSumming()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 999, 0)));
        var handler = new QueueHandler(_ => JsonResponse(
            $$"""{"usageEventsDisplay":[{{Event("api", Now, 31)}}],"totalUsageEventsCount":1}"""));
        Assert.Equal(31, (await Daily(Provider(handler)))!.TotalTokens);
    }

    [Fact]
    public async Task AuthoritativeEmptyApiSuppressesSQLiteFallback()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 999, 0)));
        var handler = new QueueHandler(_ => JsonResponse(
            "{\"usageEventsDisplay\":[],\"totalUsageEventsCount\":0}"));
        Assert.Null(await Daily(Provider(handler)));
    }

    [Fact]
    public async Task ZeroLocalTokenKnownCaseUsesDashboardTokens()
    {
        CreateDatabase(("bubbleId:zero", Bubble(Now, 0, 0)));
        var handler = new QueueHandler(_ => JsonResponse(
            $$"""{"events":[{{Event("api", Now, 77)}}]}"""));
        Assert.Equal(77, (await Daily(Provider(handler)))!.TotalTokens);
    }

    [Fact]
    public async Task StateDatabaseCredentialEnablesDashboardWithoutExposingToken()
    {
        CreateDatabase(("bubbleId:zero", Bubble(Now, 0, 0)), credential: "secret-token");
        var handler = new QueueHandler(_ => JsonResponse(
            $$"""{"events":[{{Event("api", Now, 18)}}]}"""));
        var provider = new LocalCursorUsageProvider(
            new HttpClient(handler), [_directory], credential: null);
        Assert.Equal(18, (await Daily(provider))!.TotalTokens);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SQLiteReadsMultipleRowsSkipsMalformedAndDeduplicatesAcrossRoots()
    {
        var second = Path.Combine(_directory, "other");
        Directory.CreateDirectory(second);
        CreateDatabase(
            ("bubbleId:same", Bubble(Now, 10, 0)),
            ("bubbleId:bad", "not-json"));
        CreateDatabaseAt(second, ("bubbleId:same", Bubble(Now, 40, 0)));
        var provider = Provider(new QueueHandler(), credential: null, roots: [_directory, second]);
        Assert.Equal(40, (await Daily(provider))!.TotalTokens);
    }

    [Fact]
    public async Task SQLiteWalTailIsReadFromTempCopyWithoutChangingSource()
    {
        var path = Path.Combine(_directory, "state.vscdb");
        using var database = WritableSqlite.Open(path);
        database.Execute("PRAGMA journal_mode=WAL");
        database.Execute("CREATE TABLE cursorDiskKV (key TEXT UNIQUE, value BLOB)");
        database.Execute("CREATE TABLE ItemTable (key TEXT UNIQUE, value BLOB)");
        database.Execute($"INSERT INTO cursorDiskKV VALUES ('bubbleId:wal', '{Sql(Bubble(Now, 44, 0))}')");
        var before = SourceSignature(path);
        Assert.Equal(44, (await Daily(Provider(new QueueHandler(), credential: null)))!.TotalTokens);
        Assert.Equal(before, SourceSignature(path));
    }

    [Fact]
    public async Task MissingCursorTableIsGracefulUnavailable()
    {
        using (var database = WritableSqlite.Open(Path.Combine(_directory, "state.vscdb")))
        {
            database.Execute("CREATE TABLE ItemTable (key TEXT UNIQUE, value BLOB)");
        }
        Assert.Null(await Daily(Provider(new QueueHandler(), credential: null)));
    }

    [Fact]
    public async Task TodayFiveHourWeekAndMonthUseSharedCalendarSemantics()
    {
        CreateDatabase(
            ("bubbleId:month", Bubble(new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero), 10, 0)),
            ("bubbleId:week", Bubble(new(2026, 8, 24, 1, 0, 0, TimeSpan.Zero), 20, 0)),
            ("bubbleId:today", Bubble(new(2026, 8, 30, 1, 0, 0, TimeSpan.Zero), 30, 0)),
            ("bubbleId:recent", Bubble(new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero), 40, 0)));
        var provider = Provider(new QueueHandler(), credential: null);
        var daily = await Daily(provider);
        var enrichment = await Enrichment(provider);
        Assert.Equal(70, daily!.TotalTokens);
        Assert.Equal(40, enrichment.ActiveBlock!.TotalTokens);
        Assert.Equal(90, enrichment.WeekTotal!.TotalTokens);
        Assert.Equal(100, enrichment.MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task ZeroTodayMonthOnlyRemainsVisibleThroughEnrichment()
    {
        CreateDatabase(("bubbleId:month", Bubble(Now.AddDays(-20), 1000, 0)));
        var provider = Provider(new QueueHandler(), credential: null);
        Assert.Null(await Daily(provider));
        Assert.Equal(1000, (await Enrichment(provider)).MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task BothSourcesFailAfterSuccessPreservesCachedCursorUsage()
    {
        var handler = new MutableHandler(JsonResponse(
            $$"""{"events":[{{Event("api", Now, 55)}}]}"""));
        var provider = Provider(handler);
        Assert.Equal(55, (await Daily(provider))!.TotalTokens);
        handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(55, (await provider.FetchDailyAsync(
            Now.AddSeconds(31), Utc, DayOfWeek.Monday))!.TotalTokens);
    }

    [Fact]
    public async Task TimeoutFallsBackWithoutBlockingRefresh()
    {
        CreateDatabase(("bubbleId:local", Bubble(Now, 9, 0)));
        var handler = new DelayingHandler();
        var provider = new LocalCursorUsageProvider(
            new HttpClient(handler), [_directory], () => "token", TimeSpan.FromMilliseconds(20));
        Assert.Equal(9, (await Daily(provider))!.TotalTokens);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(new DelayingHandler()).FetchDailyAsync(
                Now, Utc, DayOfWeek.Monday, cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private LocalCursorUsageProvider Provider(
        HttpMessageHandler handler,
        string? credential = "token",
        IEnumerable<string>? roots = null) =>
        new(new HttpClient(handler), roots ?? [_directory], () => credential);

    private Task<PokeTokenBar.Windows.Core.DailyUsage?> Daily(LocalCursorUsageProvider provider) =>
        provider.FetchDailyAsync(Now, Utc, DayOfWeek.Monday);

    private Task<PokeTokenBar.Windows.Core.ProviderEnrichment> Enrichment(LocalCursorUsageProvider provider) =>
        provider.FetchEnrichmentAsync(Now, Utc, DayOfWeek.Monday);

    private LocalUsageEntry? ParseBubble(string key, string json) =>
        LocalCursorUsageProvider.ParseBubble(key, json, Now.AddMonths(-1), Utc);

    private void CreateDatabase(params (string Key, string Json)[] rows) =>
        CreateDatabaseAt(_directory, rows);

    private void CreateDatabase((string Key, string Json) row, string credential) =>
        CreateDatabaseAt(_directory, [row], credential);

    private static void CreateDatabaseAt(
        string root,
        (string Key, string Json) row) =>
        CreateDatabaseAt(root, [row]);

    private static void CreateDatabaseAt(
        string root,
        IReadOnlyList<(string Key, string Json)> rows,
        string? credential = null)
    {
        var path = Path.Combine(root, "state.vscdb");
        using var database = WritableSqlite.Open(path);
        database.Execute("CREATE TABLE cursorDiskKV (key TEXT UNIQUE, value BLOB)");
        database.Execute("CREATE TABLE ItemTable (key TEXT UNIQUE, value BLOB)");
        foreach (var row in rows)
        {
            database.Execute($"INSERT INTO cursorDiskKV VALUES ('{Sql(row.Key)}', '{Sql(row.Json)}')");
        }
        if (credential is not null)
        {
            database.Execute(
                $"INSERT INTO ItemTable VALUES ('cursorAuth/accessToken', '{Sql(credential)}')");
        }
    }

    private static string Bubble(
        DateTimeOffset timestamp,
        long input,
        long output,
        string? model = "model") =>
        JsonSerializer.Serialize(new
        {
            tokenCount = new { inputTokens = input, outputTokens = output },
            createdAt = timestamp.ToUniversalTime().ToString("O"),
            modelType = model,
        });

    private static string Event(string id, DateTimeOffset timestamp, long input) =>
        JsonSerializer.Serialize(new
        {
            id,
            timestamp = timestamp.ToUnixTimeMilliseconds().ToString(),
            model = "gpt",
            tokenUsage = new { inputTokens = input },
        });

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static (long Length, DateTime LastWrite, long WalLength, DateTime? WalWrite) SourceSignature(string path)
    {
        var info = new FileInfo(path);
        var wal = new FileInfo(path + "-wal");
        return (info.Length, info.LastWriteTimeUtc, wal.Exists ? wal.Length : 0, wal.Exists ? wal.LastWriteTimeUtc : null);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpRequestMessage, HttpResponseMessage>? _response;
        public QueueHandler() { }
        public QueueHandler(Func<int, HttpResponseMessage> response) => _response = (call, _) => response(call);
        public QueueHandler(Func<int, HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_response?.Invoke(Calls, request) ??
                                   new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class MutableHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Response);
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay should only complete through cancellation.");
        }
    }

    private sealed class WritableSqlite : IDisposable
    {
        private IntPtr _handle;
        private WritableSqlite(IntPtr handle) => _handle = handle;
        public static WritableSqlite Open(string path)
        {
            var status = Native.sqlite3_open_v2(path, out var handle, 0x00000002 | 0x00000004, null);
            Assert.Equal(0, status);
            return new WritableSqlite(handle);
        }
        public void Execute(string sql)
        {
            var status = Native.sqlite3_exec(_handle, sql, IntPtr.Zero, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) Native.sqlite3_free(error);
            Assert.Equal(0, status);
        }
        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero) Native.sqlite3_close_v2(handle);
        }
        private static class Native
        {
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_open_v2(
                [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
                out IntPtr database,
                int flags,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string? vfs);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_exec(
                IntPtr database,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
                IntPtr callback,
                IntPtr argument,
                out IntPtr error);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void sqlite3_free(IntPtr value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close_v2(IntPtr database);
        }
    }
}
