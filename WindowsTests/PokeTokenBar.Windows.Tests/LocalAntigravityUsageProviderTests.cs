using System.Runtime.InteropServices;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalAntigravityUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Antigravity-{Guid.NewGuid():N}");

    public LocalAntigravityUsageProviderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MetadataMatchesMacOSProvider()
    {
        var provider = new LocalAntigravityUsageProvider([]);

        Assert.Equal("antigravity", provider.Id);
        Assert.Equal("Antigravity", provider.DisplayName);
        Assert.False(provider.ReportsCost);
    }

    [Fact]
    public void DefaultRootsCoverCoreCliAndIdeUnderWindowsProfile()
    {
        var profile = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester");

        Assert.Equal(
            [
                Path.Combine(profile, ".gemini", "antigravity", "conversations"),
                Path.Combine(profile, ".gemini", "antigravity-cli", "conversations"),
                Path.Combine(profile, ".gemini", "antigravity-ide", "conversations"),
            ],
            LocalAntigravityUsageProvider.GetDefaultRoots(profile));
    }

    [Fact]
    public async Task MissingDirectoryIsUnavailableWithoutFailure()
    {
        var provider = new LocalAntigravityUsageProvider([Path.Combine(_directory, "missing")]);

        Assert.Null(await Daily(provider));
        Assert.True((await Enrichment(provider)).PeriodsOK);
    }

    [Fact]
    public void ProtobufMapsIndependentTokenBucketsAndIdentity()
    {
        var entry = Assert.Single(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("response-1", Now, 100, 50, 7, 25, "gemini-3.6-flash"),
            "conversation",
            1,
            Now.AddDays(-1),
            Utc));

        Assert.Equal("antigravity|response-1", entry.Id);
        Assert.Equal(100, entry.Input);
        Assert.Equal(50, entry.Output);
        Assert.Equal(7, entry.CacheWrite);
        Assert.Equal(25, entry.CacheRead);
        Assert.Equal(182, entry.TotalTokens);
    }

    [Fact]
    public void AntigravityUsageNeverReportsPerTokenCost()
    {
        var entry = Assert.Single(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("response", Now, 1_000_000, 1_000_000, 0, 0, "claude-sonnet-4-6"),
            "conversation",
            1,
            Now,
            Utc));

        Assert.Equal(0, entry.Cost);
    }

    [Fact]
    public void OversizedCounterIsDiscardedWhileOtherCountersSurvive()
    {
        var entry = Assert.Single(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("response", Now, 1_000_000_001, 9, 0, 0, "model"),
            "conversation",
            1,
            Now,
            Utc));

        Assert.Equal(0, entry.Input);
        Assert.Equal(9, entry.Output);
    }

    [Fact]
    public void MalformedOrZeroUsageProtobufIsIgnored()
    {
        Assert.Empty(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            [0xff, 0xff], "conversation", 1, Now, Utc));
        Assert.Empty(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("zero", Now, 0, 0, 0, 0, "model"),
            "conversation",
            2,
            Now,
            Utc));
        Assert.Empty(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("bad-date", DateTimeOffset.FromUnixTimeSeconds(1), 1, 0, 0, 0, "model"),
            "conversation",
            3,
            Now,
            Utc));
    }

    [Fact]
    public void EmbeddedCreatedAtWinsOverFallbackDate()
    {
        var created = Now.AddHours(-2);
        var entry = Assert.Single(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("response", created, 1, 0, 0, 0, "model"),
            "conversation",
            1,
            Now.AddDays(-2),
            Utc));

        Assert.Equal(created, entry.Timestamp);
    }

    [Fact]
    public void MissingCreatedAtUsesSafeFallbackDate()
    {
        var entry = Assert.Single(LocalAntigravityUsageProvider.ParseGenerationMetadata(
            Generation("response", null, 1, 0, 0, 0, "model"),
            "conversation",
            1,
            Now,
            Utc));

        Assert.Equal(Now, entry.Timestamp);
    }

    [Fact]
    public async Task ConversationDatabaseFlowsThroughDailyProvider()
    {
        CreateDatabase(
            "conversation.db",
            [(1, Generation("response", Now.AddHours(-1), 100, 50, 0, 25, "model"))]);

        var daily = await Daily();

        Assert.Equal(175, daily!.TotalTokens);
        Assert.Equal(0, daily.TotalCost);
    }

    [Fact]
    public async Task StepMetadataDatesUndatedGenerationByResponseId()
    {
        var stepDate = Now.AddHours(-3);
        CreateDatabase(
            "steps.db",
            [(1, Generation("response", null, 20, 0, 0, 0, "model", "execution"))],
            [Step("response", "execution", stepDate)]);

        var enrichment = await Enrichment();

        Assert.Equal(20, enrichment.ActiveBlock!.TotalTokens);
        Assert.StartsWith("2026-08-30T09:00:00", enrichment.ActiveBlock.StartTime);
    }

    [Fact]
    public async Task MultipleDatabasesAndRootsDeduplicateCopiedResponse()
    {
        var other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(other);
        CreateDatabase(
            "first.db",
            [(1, Generation("same", Now, 10, 0, 0, 0, "model"))]);
        CreateDatabase(
            Path.Combine(other, "copy.db"),
            [(1, Generation("same", Now, 25, 0, 0, 0, "model"))]);
        var provider = new LocalAntigravityUsageProvider([_directory, other]);

        Assert.Equal(25, (await Daily(provider))!.TotalTokens);
    }

    [Fact]
    public async Task MalformedDatabaseDoesNotHideOtherConversation()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.db"), "not sqlite");
        CreateDatabase(
            "valid.db",
            [(1, Generation("valid", Now, 12, 0, 0, 0, "model"))]);

        Assert.Equal(12, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task LiveWalTailIsReadFromTempCopyWithoutTouchingUserDatabase()
    {
        var path = Path.Combine(_directory, "live.db");
        using var database = WritableSqlite.Open(path);
        database.Execute("PRAGMA journal_mode=WAL");
        database.Execute("CREATE TABLE gen_metadata (idx INTEGER, data BLOB)");
        database.Execute("CREATE TABLE steps (idx INTEGER, metadata BLOB)");
        var blob = Generation("live", Now, 33, 0, 0, 0, "model");
        database.Execute($"INSERT INTO gen_metadata VALUES (1, x'{Convert.ToHexString(blob)}')");
        var sharedMemory = path + "-shm";
        var before = File.Exists(sharedMemory)
            ? (File.GetLastWriteTimeUtc(sharedMemory), new FileInfo(sharedMemory).Length)
            : ((DateTime?)null, 0L);

        Assert.Equal(33, (await Daily())!.TotalTokens);

        var after = File.Exists(sharedMemory)
            ? (File.GetLastWriteTimeUtc(sharedMemory), new FileInfo(sharedMemory).Length)
            : ((DateTime?)null, 0L);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task TodayFiveHourWeekAndMonthUseLocalCalendarWindows()
    {
        CreateDatabase(
            "periods.db",
            [
                (1, Generation("month", new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero), 10, 0, 0, 0, "model")),
                (2, Generation("week", new DateTimeOffset(2026, 8, 24, 1, 0, 0, TimeSpan.Zero), 20, 0, 0, 0, "model")),
                (3, Generation("today", new DateTimeOffset(2026, 8, 30, 1, 0, 0, TimeSpan.Zero), 30, 0, 0, 0, "model")),
                (4, Generation("recent", new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero), 40, 0, 0, 0, "model")),
            ]);

        var daily = await Daily();
        var enrichment = await Enrichment();

        Assert.Equal(70, daily!.TotalTokens);
        Assert.Equal(40, enrichment.ActiveBlock!.TotalTokens);
        Assert.Equal(90, enrichment.WeekTotal!.TotalTokens);
        Assert.Equal(100, enrichment.MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task ZeroTodayWithMonthUsageReturnsCarrierPeriods()
    {
        CreateDatabase(
            "month.db",
            [(1, Generation("month", Now.AddDays(-20), 1_000, 0, 0, 0, "model"))]);

        Assert.Null(await Daily());
        Assert.Equal(1_000, (await Enrichment()).MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task CancellationStopsDatabaseScan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalAntigravityUsageProvider([_directory]).FetchDailyAsync(
                Now,
                Utc,
                DayOfWeek.Monday,
                cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Task<PokeTokenBar.Windows.Core.DailyUsage?> Daily(
        LocalAntigravityUsageProvider? provider = null) =>
        (provider ?? new LocalAntigravityUsageProvider([_directory])).FetchDailyAsync(
            Now,
            Utc,
            DayOfWeek.Monday);

    private Task<PokeTokenBar.Windows.Core.ProviderEnrichment> Enrichment(
        LocalAntigravityUsageProvider? provider = null) =>
        (provider ?? new LocalAntigravityUsageProvider([_directory])).FetchEnrichmentAsync(
            Now,
            Utc,
            DayOfWeek.Monday);

    private void CreateDatabase(
        string relativePath,
        IReadOnlyList<(long Index, byte[] Data)> generations,
        IReadOnlyList<byte[]>? steps = null)
    {
        var path = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = WritableSqlite.Open(path);
        database.Execute("CREATE TABLE gen_metadata (idx INTEGER, data BLOB)");
        database.Execute("CREATE TABLE steps (idx INTEGER, metadata BLOB)");
        foreach (var generation in generations)
        {
            database.Execute(
                $"INSERT INTO gen_metadata VALUES ({generation.Index}, x'{Convert.ToHexString(generation.Data)}')");
        }

        var ordinal = 0;
        foreach (var step in steps ?? [])
        {
            database.Execute(
                $"INSERT INTO steps VALUES ({++ordinal}, x'{Convert.ToHexString(step)}')");
        }

        database.Dispose();
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
    }

    private static byte[] Generation(
        string responseId,
        DateTimeOffset? createdAt,
        ulong input,
        ulong output,
        ulong cacheWrite,
        ulong cacheRead,
        string model,
        string? execution = null)
    {
        var usage = Varint(2, input)
            .Concat(Varint(3, output))
            .Concat(Varint(4, cacheWrite))
            .Concat(Varint(5, cacheRead))
            .Concat(Text(11, responseId))
            .ToArray();
        var chat = Message(4, usage).Concat(Text(19, model));
        if (createdAt is not null)
        {
            chat = chat.Concat(Message(9, Message(4, Timestamp(createdAt.Value))));
        }

        var record = Message(1, chat.ToArray());
        return execution is null ? record : record.Concat(Text(4, execution)).ToArray();
    }

    private static byte[] Step(string responseId, string execution, DateTimeOffset timestamp) =>
        Message(8, Timestamp(timestamp))
            .Concat(Message(9, Text(11, responseId)))
            .Concat(Text(12, execution))
            .ToArray();

    private static byte[] Timestamp(DateTimeOffset timestamp) =>
        Varint(1, (ulong)timestamp.ToUnixTimeSeconds());

    private static byte[] Message(int field, IEnumerable<byte> payload)
    {
        var bytes = payload.ToArray();
        return RawVarint((ulong)((field << 3) | 2))
            .Concat(RawVarint((ulong)bytes.Length))
            .Concat(bytes)
            .ToArray();
    }

    private static byte[] Text(int field, string value) => Message(field, System.Text.Encoding.UTF8.GetBytes(value));

    private static byte[] Varint(int field, ulong value) =>
        RawVarint((ulong)(field << 3)).Concat(RawVarint(value)).ToArray();

    private static byte[] RawVarint(ulong value)
    {
        var result = new List<byte>();
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            result.Add(value == 0 ? current : (byte)(current | 0x80));
        }
        while (value != 0);
        return result.ToArray();
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
            if (error != IntPtr.Zero)
            {
                Native.sqlite3_free(error);
            }
            Assert.Equal(0, status);
        }

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                Native.sqlite3_close_v2(handle);
            }
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
