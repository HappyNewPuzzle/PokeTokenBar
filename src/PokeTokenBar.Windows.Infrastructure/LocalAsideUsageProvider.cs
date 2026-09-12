using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class LocalAsideUsageProvider : LocalAdditionalUsageProvider
{
    public LocalAsideUsageProvider() : this(GetDefaultRoots()) { }
    public LocalAsideUsageProvider(IEnumerable<string> roots) : base(roots) { }

    protected override bool PreserveMissingEntries => true;
    public override string Id => "aside";
    public override string DisplayName => "Aside";

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return LocalUsageSupport.NormalizeRoots([Path.Combine(userProfile, ".aside", "u")]);
    }

    internal static IReadOnlyList<string> DiscoverDatabases(IEnumerable<string> roots)
    {
        var databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            AddIfDatabase(root);
            AddIfDatabase(Path.Combine(root, "state.db"));
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var child in Directory.EnumerateDirectories(
                             root, "*", SearchOption.TopDirectoryOnly))
                {
                    AddIfDatabase(Path.Combine(child, "state.db"));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException) { }
        }

        return databases.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddIfDatabase(string path)
        {
            if (File.Exists(path)
                && Path.GetFileName(path).Equals("state.db", StringComparison.OrdinalIgnoreCase))
            {
                databases.Add(Path.GetFullPath(path));
            }
        }
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        var cutoff = modifiedSince.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var query = $"""
            SELECT t.id, t.token_usage,
                   COALESCE(t.finished_at, t.last_message_timestamp), s.model
            FROM session_turns t
            LEFT JOIN sessions s ON s.id = t.session_id
            WHERE COALESCE(t.finished_at, t.last_message_timestamp) >= {cutoff}
            """;

        foreach (var database in DiscoverDatabases(Roots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var identity = DatabaseIdentity(database);
                var rows = LocalCursorUsageProvider.WithDatabaseCopy(
                    database,
                    connection => connection.ReadTextRows(
                        query,
                        4,
                        prepareErrorIsFailure: true));
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ParseRow(
                            row,
                            database,
                            identity,
                            modifiedSince,
                            timeZone) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // One unavailable or incompatible profile must not hide healthy profiles.
            }
        }

        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseRow(
        string?[] row,
        string database,
        string databaseIdentity,
        DateTimeOffset modifiedSince,
        TimeZoneInfo timeZone)
    {
        var usageJson = row.Length < 2 ? null : row[1];
        if (row.Length < 4
            || string.IsNullOrWhiteSpace(row[0])
            || string.IsNullOrWhiteSpace(usageJson)
            || !AdditionalJson.Timestamp(row[2], out var timestamp)
            || timestamp < modifiedSince)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(usageJson);
            var usage = document.RootElement;
            if (usage.ValueKind != JsonValueKind.Object) return null;

            var hasBuckets = AdditionalJson.TryToken(usage, "input", out var input);
            hasBuckets |= AdditionalJson.TryToken(usage, "output", out var output);
            hasBuckets |= AdditionalJson.TryToken(usage, "cacheWrite", out var cacheWrite);
            hasBuckets |= AdditionalJson.TryToken(usage, "cacheRead", out var cacheRead);
            if (!hasBuckets)
            {
                input = AdditionalJson.Token(usage, "totalTokens");
                output = cacheWrite = cacheRead = 0;
            }

            if (input + output + cacheWrite + cacheRead == 0) return null;

            // sessions.model is current session metadata, not historical per-turn truth.
            // It is used only for cost fallback; Aside usage stays aggregate-only.
            var model = ModelId(row[3]);
            var explicitCost = AdditionalJson.Object(usage, "cost", out var cost)
                ? AdditionalJson.Double(cost, "total")
                : null;
            var totalCost = explicitCost is >= 0
                ? explicitCost.Value
                : LocalUsageSupport.CalculateCost(
                    model, input, output, cacheWrite, cacheRead);
            var canonicalDatabase = Path.GetFullPath(database);
            return AdditionalJson.Entry(
                $"aside|{canonicalDatabase}#{databaseIdentity}:{row[0]!.Trim()}",
                timestamp,
                timeZone,
                input,
                output,
                cacheWrite,
                cacheRead,
                totalCost);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string DatabaseIdentity(string database)
    {
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                database,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (Native.GetFileInformationByHandle(handle, out var info))
            {
                return $"{info.VolumeSerialNumber:X8}-{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DllNotFoundException
                or EntryPointNotFoundException) { }

        try
        {
            return $"fallback-{File.GetCreationTimeUtc(database).Ticks:X16}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return "fallback-unavailable";
        }
    }

    private static string ModelId(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            return AdditionalJson.String(document.RootElement, "modelId") ?? "aside";
        }
        catch (JsonException)
        {
            return "aside";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);
    }
}
