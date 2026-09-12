using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalAsideUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Korea = TimeZoneInfo.CreateCustomTimeZone(
        "Aside/Test/Korea",
        TimeSpan.FromHours(9),
        "Korea",
        "Korea");
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Aside-{Guid.NewGuid():N}");

    public LocalAsideUsageProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void MetadataAndDefaultRootMatchWindowsContract()
    {
        var provider = new LocalAsideUsageProvider([]);
        var profile = Path.Combine("C:\\Users", "fixture");

        Assert.Equal("aside", provider.Id);
        Assert.Equal("Aside", provider.DisplayName);
        Assert.True(provider.ReportsCost);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(profile, ".aside", "u")),
            Assert.Single(LocalAsideUsageProvider.GetDefaultRoots(profile)));
    }

    [Fact]
    public void DiscoverySupportsProfileRootAndImmediateChildrenWithoutDuplicatesOrDeepCrawl()
    {
        var usersRoot = Path.Combine(_directory, ".aside", "u");
        var first = EmptyDatabase(Path.Combine(usersRoot, "0"));
        var second = EmptyDatabase(Path.Combine(usersRoot, "1"));
        var direct = EmptyDatabase(Path.Combine(_directory, "direct-profile"));
        _ = EmptyDatabase(Path.Combine(usersRoot, "0", "nested"));

        var databases = LocalAsideUsageProvider.DiscoverDatabases(
            [usersRoot, usersRoot, Path.GetDirectoryName(direct)!]);

        Assert.Equal(
            new[] { first, second, direct }.Order(StringComparer.OrdinalIgnoreCase),
            databases);
    }

    [Fact]
    public void SQLiteRowsMapBucketsExplicitCostAndIgnoreReportedTotal()
    {
        var database = CreateDatabase();
        InsertTurn(
            database,
            1,
            "session",
            """{"input":22744,"output":5515,"cacheRead":758400,"cacheWrite":9,"totalTokens":999999,"cost":{"total":0.65837}}""",
            Now.AddHours(-1));

        var entry = Assert.Single(Load(database));

        Assert.Equal(22_744, entry.Input);
        Assert.Equal(5_515, entry.Output);
        Assert.Equal(9, entry.CacheWrite);
        Assert.Equal(758_400, entry.CacheRead);
        Assert.Equal(786_668, entry.TotalTokens);
        Assert.Equal(0.65837, entry.Cost, precision: 6);
    }

    [Fact]
    public void TotalOnlyUsageFallsBackToInputWithoutInventingCache()
    {
        var database = CreateDatabase();
        InsertTurn(
            database,
            1,
            "session",
            """{"input":null,"totalTokens":123}""",
            Now.AddHours(-1));

        var entry = Assert.Single(Load(database));

        Assert.Equal(123, entry.Input);
        Assert.Equal(0, entry.Output);
        Assert.Equal(0, entry.CacheWrite);
        Assert.Equal(0, entry.CacheRead);
    }

    [Fact]
    public void ZeroTokenTurnIsIgnoredButAbortedTurnWithUsageCounts()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":0,"totalTokens":0}""",
            Now.AddHours(-2), abortedAt: Now.AddHours(-2));
        InsertTurn(database, 2, "session", """{"input":7}""",
            Now.AddHours(-1), abortedAt: Now.AddHours(-1));

        var entry = Assert.Single(Load(database));

        Assert.Equal(7, entry.TotalTokens);
        Assert.EndsWith(":2", entry.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void FinishedTimestampOutranksLastMessageAndUsesLocalCalendarDay()
    {
        var database = CreateDatabase();
        var cutoff = new DateTimeOffset(2026, 9, 11, 15, 0, 0, TimeSpan.Zero);
        var finishedToday = cutoff.AddMinutes(30);
        InsertTurn(database, 1, "session", """{"input":10}""",
            lastMessage: cutoff.AddMinutes(-30), finishedAt: finishedToday);
        InsertTurn(database, 2, "session", """{"input":20}""",
            lastMessage: cutoff.AddMinutes(30), finishedAt: cutoff.AddMinutes(-30));

        var entry = Assert.Single(new LocalAsideUsageProvider([Path.GetDirectoryName(database)!])
            .Load(cutoff, Korea, CancellationToken.None));

        Assert.Equal(finishedToday, entry.Timestamp);
        Assert.Equal(new DateOnly(2026, 9, 12), entry.LocalDay);
    }

    [Fact]
    public async Task MutableTurnUsesThirtySecondCacheThenKeepsLargerValue()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":100}""", Now.AddHours(-1));
        var provider = Provider(database);

        Assert.Equal(100, (await Daily(provider, Now))?.TotalTokens);
        Execute(database, "UPDATE session_turns SET token_usage = '{\"input\":150}' WHERE id = 1");
        Assert.Equal(100, (await Daily(provider, Now.AddSeconds(20)))?.TotalTokens);
        Assert.Equal(
            100,
            (await provider.FetchEnrichmentAsync(
                Now.AddSeconds(20),
                TimeZoneInfo.Utc,
                DayOfWeek.Monday)).MonthTotal?.TotalTokens);
        Assert.Equal(150, (await Daily(provider, Now.AddSeconds(31)))?.TotalTokens);
    }

    [Fact]
    public async Task DeletedSessionDoesNotErasePreviouslyObservedUsage()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":100}""", Now.AddHours(-1));
        var provider = Provider(database);

        Assert.Equal(100, (await Daily(provider, Now))?.TotalTokens);
        Execute(database, "PRAGMA foreign_keys=ON; DELETE FROM sessions WHERE id='session'");
        Assert.Null(await Daily(Provider(database), Now.AddSeconds(31)));
        Assert.Equal(100, (await Daily(provider, Now.AddSeconds(31)))?.TotalTokens);
    }

    [Fact]
    public async Task StableFileIdentitySurvivesMutationAndSeparatesRecreatedDatabaseRows()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":100}""", Now.AddHours(-1));
        var provider = Provider(database);
        var originalIdentity = LocalAsideUsageProvider.DatabaseIdentity(database);

        Assert.Equal(100, (await Daily(provider, Now))?.TotalTokens);
        Execute(database, "UPDATE session_turns SET token_usage = '{\"input\":101}' WHERE id = 1");
        Assert.Equal(originalIdentity, LocalAsideUsageProvider.DatabaseIdentity(database));

        File.Delete(database);
        CreateSchema(database);
        InsertTurn(database, 1, "session", """{"input":10}""", Now.AddHours(-1));
        var recreatedIdentity = LocalAsideUsageProvider.DatabaseIdentity(database);

        Assert.NotEqual(originalIdentity, recreatedIdentity);
        Assert.Equal(110, (await Daily(provider, Now.AddSeconds(31)))?.TotalTokens);
    }

    [Fact]
    public void CorruptForeignAndMalformedSourcesDoNotHideHealthyRows()
    {
        var healthy = CreateDatabase("healthy");
        InsertTurn(healthy, 1, "session", """{"input":42}""", Now.AddHours(-1));
        InsertTurn(healthy, 2, "session", "not-json", Now.AddHours(-1));
        Execute(healthy, "UPDATE sessions SET model='not-json'");
        File.WriteAllText(EmptyDatabase(Path.Combine(_directory, "corrupt")), "not sqlite");
        var foreign = EmptyDatabase(Path.Combine(_directory, "foreign"));
        using (var connection = WritableSqlite.Open(foreign))
            connection.Execute("CREATE TABLE unrelated (id INTEGER)");

        var entries = new LocalAsideUsageProvider([_directory])
            .Load(Now.AddDays(-1), TimeZoneInfo.Utc, CancellationToken.None);

        Assert.Equal(42, Assert.Single(entries).TotalTokens);
    }

    [Fact]
    public async Task CachedSuccessSurvivesLaterAllFailedScan()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":42}""", Now.AddHours(-1));
        var provider = Provider(database);

        Assert.Equal(42, (await Daily(provider, Now))?.TotalTokens);
        File.WriteAllText(database, "not sqlite");

        Assert.Equal(42, (await Daily(provider, Now.AddSeconds(31)))?.TotalTokens);
        Assert.Null(await Daily(Provider(database), Now.AddSeconds(31)));
    }

    [Fact]
    public async Task ConfigurableProviderUsesCustomRootAndCompositionRegistersAsideOnce()
    {
        var database = CreateDatabase();
        InsertTurn(database, 1, "session", """{"input":12}""", DateTimeOffset.UtcNow);
        var profile = Path.GetDirectoryName(database)!;
        var configured = new ConfigurableUsageProvider(
            "aside",
            "Aside",
            reportsCost: true,
            defaultRoots: () => [],
            customRoots: () => [profile],
            roots => new LocalAsideUsageProvider(roots));

        Assert.Equal(12, (await configured.FetchDailyAsync())?.TotalTokens);
        using var httpClient = new HttpClient();
        var usage = AppComposition.CreateUsageViewModel(httpClient, _ => []);
        Assert.Equal(13, usage.RegisteredProviderIds.Count);
        Assert.Equal(1, usage.RegisteredProviderIds.Count(id => id == "aside"));
    }

    [Fact]
    public async Task SessionCurrentModelNeverCreatesPerModelUsageRowsAndCancellationPropagates()
    {
        var database = CreateDatabase(modelJson: """{"modelId":"claude-fable-5-1"}""");
        InsertTurn(database, 1, "session", """{"input":1000000}""", Now.AddHours(-1));
        var provider = Provider(database);

        var daily = Assert.IsType<DailyUsage>(await Daily(provider, Now));
        Assert.Equal(10, daily.TotalCost, precision: 6);
        Assert.DoesNotContain(
            typeof(DailyUsage).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name.Contains("Model", StringComparison.OrdinalIgnoreCase));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.FetchDailyAsync(
                Now.AddMinutes(1),
                TimeZoneInfo.Utc,
                DayOfWeek.Monday,
                cancellation.Token));
    }

    private IReadOnlyList<LocalUsageEntry> Load(string database) =>
        Provider(database).Load(Now.AddDays(-1), TimeZoneInfo.Utc, CancellationToken.None);

    private static LocalAsideUsageProvider Provider(string database) =>
        new([Path.GetDirectoryName(database)!]);

    private static Task<DailyUsage?> Daily(
        LocalAdditionalUsageProvider provider,
        DateTimeOffset now) =>
        provider.FetchDailyAsync(now, TimeZoneInfo.Utc, DayOfWeek.Monday);

    private string CreateDatabase(
        string profile = "0",
        string modelJson = """{"modelId":"synthetic-model"}""")
    {
        var directory = Path.Combine(_directory, profile);
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "state.db");
        CreateSchema(database, modelJson);
        return database;
    }

    private static void CreateSchema(
        string database,
        string modelJson = """{"modelId":"synthetic-model"}""")
    {
        using var connection = WritableSqlite.Open(database);
        connection.Execute("PRAGMA foreign_keys=ON");
        connection.Execute("CREATE TABLE sessions (id TEXT PRIMARY KEY, model TEXT)");
        connection.Execute("CREATE TABLE session_turns (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT REFERENCES sessions(id) ON DELETE CASCADE, token_usage TEXT, started_at INTEGER, last_message_timestamp INTEGER NOT NULL, finished_at INTEGER, aborted_at INTEGER)");
        connection.Execute($"INSERT INTO sessions VALUES ('session','{Sql(modelJson)}')");
    }

    private static void InsertTurn(
        string database,
        long id,
        string session,
        string usage,
        DateTimeOffset lastMessage,
        DateTimeOffset? finishedAt = null,
        DateTimeOffset? abortedAt = null)
    {
        var finished = finishedAt is null ? "NULL" : finishedAt.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var aborted = abortedAt is null ? "NULL" : abortedAt.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Execute(database,
            $"INSERT INTO session_turns VALUES ({id},'{Sql(session)}','{Sql(usage)}',{lastMessage.AddHours(-1).ToUnixTimeSeconds()},{lastMessage.ToUnixTimeSeconds()},{finished},{aborted})");
    }

    private static void Execute(string database, string sql)
    {
        using var connection = WritableSqlite.Open(database);
        connection.Execute(sql);
    }

    private static string EmptyDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "state.db");
        File.WriteAllBytes(database, []);
        return Path.GetFullPath(database);
    }

    private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class WritableSqlite : IDisposable
    {
        private IntPtr _handle;
        private WritableSqlite(IntPtr handle) => _handle = handle;

        public static WritableSqlite Open(string path)
        {
            var status = Native.sqlite3_open_v2(
                path,
                out var handle,
                0x00000002 | 0x00000004,
                null);
            Assert.Equal(0, status);
            return new WritableSqlite(handle);
        }

        public void Execute(string sql)
        {
            var status = Native.sqlite3_exec(
                _handle,
                sql,
                IntPtr.Zero,
                IntPtr.Zero,
                out var error);
            var message = error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(error);
            if (error != IntPtr.Zero) Native.sqlite3_free(error);
            Assert.True(status == 0, message);
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
