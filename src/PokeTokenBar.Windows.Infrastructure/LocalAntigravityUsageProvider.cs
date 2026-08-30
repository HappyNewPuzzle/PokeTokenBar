using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalAntigravityUsageProvider : IUsageProvider
{
    private readonly IReadOnlyList<string> _roots;
    private readonly Dictionary<string, CachedDatabase> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public LocalAntigravityUsageProvider()
        : this(GetDefaultRoots())
    {
    }

    public LocalAntigravityUsageProvider(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = LocalUsageSupport.NormalizeRoots(roots);
    }

    public string Id => "antigravity";
    public string DisplayName => "Antigravity";
    public bool ReportsCost => false;

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
        FetchDailyAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    public async Task<DailyUsage?> FetchDailyAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var today = LocalUsageSupport.LocalDate(now, timeZone);
        var entries = await LoadEntriesAsync(
            LocalUsageSupport.StartOfLocalDay(today, timeZone),
            timeZone,
            cancellationToken).ConfigureAwait(false);
        return LocalUsageSupport.Daily(entries, today);
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken = default) =>
        FetchEnrichmentAsync(
            DateTimeOffset.Now,
            TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            cancellationToken);

    public async Task<ProviderEnrichment> FetchEnrichmentAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await LoadEntriesAsync(
                LocalUsageSupport.EnrichmentScanStart(now, timeZone, firstDayOfWeek),
                timeZone,
                cancellationToken).ConfigureAwait(false);
            return LocalUsageSupport.Enrichment(entries, now, timeZone, firstDayOfWeek);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ProviderEnrichment();
        }
    }

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return LocalUsageSupport.NormalizeRoots(
        [
            Path.Combine(userProfile, ".gemini", "antigravity", "conversations"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "conversations"),
            Path.Combine(userProfile, ".gemini", "antigravity-ide", "conversations"),
        ]);
    }

    internal static IReadOnlyList<LocalUsageEntry> ParseGenerationMetadata(
        byte[] blob,
        string conversation,
        long index,
        DateTimeOffset fallbackDate,
        TimeZoneInfo timeZone)
    {
        var entry = AntigravityProto.ParseGeneration(
            blob,
            conversation,
            index,
            fallbackDate,
            timeZone);
        return entry is null ? [] : [entry];
    }

    private async Task<IReadOnlyList<LocalUsageEntry>> LoadEntriesAsync(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => LoadEntries(modifiedSince, timeZone, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private IReadOnlyList<LocalUsageEntry> LoadEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var all = new List<LocalUsageEntry>();
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> databases;
            try
            {
                databases = Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*.db", SearchOption.TopDirectoryOnly)
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            try
            {
                foreach (var database in databases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var signature = Signature(database);
                    if (signature is null || signature.Value.ModifiedAt < modifiedSince)
                    {
                        continue;
                    }

                    if (_cache.TryGetValue(database, out var cached) && cached.Signature == signature.Value)
                    {
                        all.AddRange(cached.Entries);
                        continue;
                    }

                    try
                    {
                        var entries = ReadDatabase(database, signature.Value.ModifiedAt, timeZone);
                        _cache[database] = new CachedDatabase(signature.Value, entries);
                        all.AddRange(entries);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        if (cached is not null)
                        {
                            all.AddRange(cached.Entries);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A failed directory enumeration does not affect other roots/providers.
            }
        }

        return LocalUsageSupport.Deduplicate(
            all.Where(entry => entry.Timestamp >= modifiedSince));
    }

    private static DatabaseSignature? Signature(string database)
    {
        DateTimeOffset? newest = null;
        long size = 0;
        foreach (var path in new[] { database, database + "-wal" })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var info = new FileInfo(path);
                var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                newest = newest is null || modified > newest ? modified : newest;
                size += info.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return newest is null ? null : new DatabaseSignature(newest.Value, size);
    }

    private static IReadOnlyList<LocalUsageEntry> ReadDatabase(
        string database,
        DateTimeOffset fallbackDate,
        TimeZoneInfo timeZone)
    {
        var temporary = Directory.CreateTempSubdirectory("PokeTokenBar-Antigravity-");
        try
        {
            var copy = Path.Combine(temporary.FullName, Path.GetFileName(database));
            File.Copy(database, copy);
            foreach (var suffix in new[] { "-wal" })
            {
                if (File.Exists(database + suffix))
                {
                    File.Copy(database + suffix, copy + suffix);
                }
            }

            using var connection = SqliteConnection.OpenReadOnly(copy);
            var stepDates = ReadStepDates(connection);
            var entries = new List<LocalUsageEntry>();
            foreach (var row in connection.ReadRows(
                         "SELECT idx, data FROM gen_metadata WHERE data IS NOT NULL ORDER BY idx"))
            {
                var response = AntigravityProto.ResponseId(row.Data);
                var execution = AntigravityProto.ExecutionId(row.Data);
                var date = response is not null && stepDates.ByResponse.TryGetValue(response, out var responseDate)
                    ? responseDate
                    : execution is not null && stepDates.ByExecution.TryGetValue(execution, out var dates) && dates.Count > 0
                        ? dates.Dequeue()
                        : fallbackDate;
                var entry = AntigravityProto.ParseGeneration(
                    row.Data,
                    Path.GetFileNameWithoutExtension(database),
                    row.Index,
                    date,
                    timeZone);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
        finally
        {
            try
            {
                temporary.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked temp copy is harmless and never touches the user's database.
            }
        }
    }

    private static StepDates ReadStepDates(SqliteConnection connection)
    {
        var result = new StepDates();
        foreach (var row in connection.ReadRows(
                     "SELECT 0, metadata FROM steps WHERE metadata IS NOT NULL ORDER BY idx"))
        {
            var date = AntigravityProto.Timestamp(row.Data, 8) ??
                       AntigravityProto.Timestamp(row.Data, 1);
            if (date is null)
            {
                continue;
            }

            var response = AntigravityProto.StepResponseId(row.Data);
            if (response is not null)
            {
                result.ByResponse[response] = date.Value;
            }

            var execution = AntigravityProto.String(row.Data, 12);
            if (execution is not null)
            {
                if (!result.ByExecution.TryGetValue(execution, out var dates))
                {
                    dates = new Queue<DateTimeOffset>();
                    result.ByExecution[execution] = dates;
                }

                dates.Enqueue(date.Value);
            }
        }

        return result;
    }

    private sealed record CachedDatabase(
        DatabaseSignature Signature,
        IReadOnlyList<LocalUsageEntry> Entries);

    private readonly record struct DatabaseSignature(DateTimeOffset ModifiedAt, long Size);

    private sealed class StepDates
    {
        public Dictionary<string, DateTimeOffset> ByResponse { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Queue<DateTimeOffset>> ByExecution { get; } = new(StringComparer.Ordinal);
    }
}

internal static class AntigravityProto
{
    private const ulong TokenCeiling = 1_000_000_000;

    public static LocalUsageEntry? ParseGeneration(
        byte[] blob,
        string conversation,
        long index,
        DateTimeOffset fallbackDate,
        TimeZoneInfo timeZone)
    {
        if (!Payload(blob, 1, out var chatModel) || !Payload(chatModel, 4, out var usage))
        {
            return null;
        }

        var embeddedDate = CreatedAt(chatModel);
        if (embeddedDate.Present && embeddedDate.Value is null)
        {
            return null;
        }

        var date = embeddedDate.Value ?? fallbackDate;
        var responseId = String(usage, 11);
        var identity = responseId is null
            ? $"antigravity|{conversation}|{index}"
            : $"antigravity|{responseId}";
        var model = String(chatModel, 19) ?? "unknown";
        var input = Token(usage, 2);
        var output = Token(usage, 3);
        var cacheWrite = Token(usage, 4);
        var cacheRead = Token(usage, 5);
        if (input + output + cacheWrite + cacheRead == 0)
        {
            return null;
        }

        return new LocalUsageEntry(
            identity,
            date,
            LocalUsageSupport.LocalDate(date, timeZone),
            input,
            output,
            cacheWrite,
            cacheRead,
            Cost: 0);
    }

    public static string? ResponseId(byte[] blob) =>
        Payload(blob, 1, out var chatModel) && Payload(chatModel, 4, out var usage)
            ? String(usage, 11)
            : null;

    public static string? ExecutionId(byte[] blob) => String(blob, 4);

    public static string? StepResponseId(byte[] blob) =>
        Payload(blob, 9, out var model) ? String(model, 11) : null;

    public static DateTimeOffset? Timestamp(byte[] data, int field) =>
        Payload(data, field, out var stamp) ? TimestampValue(stamp) : null;

    public static string? String(byte[] data, int field) =>
        Payload(data, field, out var payload) && payload.Length > 0
            ? Encoding.UTF8.GetString(payload) is { Length: > 0 } text ? text : null
            : null;

    private static (bool Present, DateTimeOffset? Value) CreatedAt(byte[] chatModel)
    {
        if (!Payload(chatModel, 9, out var start) || !Payload(start, 4, out var stamp))
        {
            return (false, null);
        }

        return (true, TimestampValue(stamp));
    }

    private static DateTimeOffset? TimestampValue(byte[] stamp)
    {
        if (!Varint(stamp, 1, out var seconds) ||
            seconds < 1_000_000_000 ||
            seconds > 4_102_444_800)
        {
            return null;
        }

        var nanos = Varint(stamp, 2, out var rawNanos) && rawNanos < 1_000_000_000
            ? rawNanos
            : 0;
        return DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            .AddTicks((long)(nanos / 100));
    }

    private static long Token(byte[] data, int field) =>
        !Varint(data, field, out var value) || value > TokenCeiling ? 0 : (long)value;

    private static bool Varint(byte[] data, int targetField, out ulong result)
    {
        result = 0;
        var index = 0;
        while (ReadField(data, ref index, out var field, out var value, out _))
        {
            if (field == targetField)
            {
                result = value;
                return true;
            }
        }

        return false;
    }

    private static bool Payload(byte[] data, int targetField, out byte[] result)
    {
        result = [];
        var index = 0;
        while (ReadField(data, ref index, out var field, out _, out var payload))
        {
            if (field == targetField && payload is not null)
            {
                result = payload;
                return true;
            }
        }

        return false;
    }

    private static bool ReadField(
        byte[] data,
        ref int index,
        out int field,
        out ulong value,
        out byte[]? payload)
    {
        field = 0;
        value = 0;
        payload = null;
        if (!ReadRawVarint(data, ref index, out var key))
        {
            return false;
        }

        field = (int)(key >> 3);
        if (field <= 0)
        {
            return false;
        }

        switch (key & 7)
        {
            case 0:
                return ReadRawVarint(data, ref index, out value);
            case 1 when data.Length - index >= 8:
                index += 8;
                return true;
            case 2:
                if (!ReadRawVarint(data, ref index, out var length) || length > (ulong)(data.Length - index))
                {
                    return false;
                }

                payload = data.AsSpan(index, (int)length).ToArray();
                index += (int)length;
                return true;
            case 5 when data.Length - index >= 4:
                index += 4;
                return true;
            default:
                return false;
        }
    }

    private static bool ReadRawVarint(byte[] data, ref int index, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (index < data.Length && shift <= 63)
        {
            var current = data[index++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }
}

internal sealed class SqliteConnection : IDisposable
{
    private const int Ok = 0;
    private const int Error = 1;
    private const int Row = 100;
    private const int Done = 101;
    private const int ReadOnlyFlag = 0x00000001;
    private const int NoMutexFlag = 0x00008000;
    private IntPtr _handle;

    private SqliteConnection(IntPtr handle) => _handle = handle;

    public static SqliteConnection OpenReadOnly(string path)
    {
        var status = Native.sqlite3_open_v2(path, out var handle, ReadOnlyFlag | NoMutexFlag, null);
        if (status != Ok || handle == IntPtr.Zero)
        {
            if (handle != IntPtr.Zero)
            {
                Native.sqlite3_close_v2(handle);
            }

            throw new IOException($"Unable to open Antigravity conversation database ({status}).");
        }

        return new SqliteConnection(handle);
    }

    public IReadOnlyList<(long Index, byte[] Data)> ReadRows(string sql)
    {
        var prepared = Native.sqlite3_prepare_v2(_handle, sql, -1, out var statement, IntPtr.Zero);
        if (prepared == Error)
        {
            return [];
        }

        if (prepared != Ok || statement == IntPtr.Zero)
        {
            throw new InvalidDataException($"Unable to query Antigravity conversation database ({prepared}).");
        }

        try
        {
            var rows = new List<(long, byte[])>();
            while (true)
            {
                var step = Native.sqlite3_step(statement);
                if (step == Done)
                {
                    return rows;
                }

                if (step != Row)
                {
                    throw new InvalidDataException($"Incomplete Antigravity conversation scan ({step}).");
                }

                var pointer = Native.sqlite3_column_blob(statement, 1);
                var length = Native.sqlite3_column_bytes(statement, 1);
                if (pointer == IntPtr.Zero || length <= 0)
                {
                    continue;
                }

                var data = new byte[length];
                Marshal.Copy(pointer, data, 0, length);
                rows.Add((Native.sqlite3_column_int64(statement, 0), data));
            }
        }
        finally
        {
            Native.sqlite3_finalize(statement);
        }
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
        internal static extern int sqlite3_close_v2(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            int bytes,
            out IntPtr statement,
            IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
    }
}
