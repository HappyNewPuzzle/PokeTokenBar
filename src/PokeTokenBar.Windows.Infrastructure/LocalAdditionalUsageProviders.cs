using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public abstract class LocalAdditionalUsageProvider : IUsageProvider
{
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private Cached? _cache;

    protected LocalAdditionalUsageProvider(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = LocalUsageSupport.NormalizeRoots(roots);
    }

    protected IReadOnlyList<string> Roots { get; }
    protected virtual bool PreserveMissingEntries => false;
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual bool ReportsCost => true;

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
        FetchDailyAsync(DateTimeOffset.Now, TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, cancellationToken);

    internal async Task<DailyUsage?> FetchDailyAsync(
        DateTimeOffset now, TimeZoneInfo timeZone, DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var entries = await Entries(now, timeZone, firstDayOfWeek, cancellationToken).ConfigureAwait(false);
        return LocalUsageSupport.Daily(entries, LocalUsageSupport.LocalDate(now, timeZone));
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
        FetchEnrichmentAsync(DateTimeOffset.Now, TimeZoneInfo.Local,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, cancellationToken);

    internal async Task<ProviderEnrichment> FetchEnrichmentAsync(
        DateTimeOffset now, TimeZoneInfo timeZone, DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken = default)
    {
        var entries = await Entries(now, timeZone, firstDayOfWeek, cancellationToken).ConfigureAwait(false);
        return LocalUsageSupport.Enrichment(entries, now, timeZone, firstDayOfWeek);
    }

    internal abstract IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken);

    private async Task<IReadOnlyList<LocalUsageEntry>> Entries(
        DateTimeOffset now, TimeZoneInfo timeZone, DayOfWeek firstDayOfWeek,
        CancellationToken cancellationToken)
    {
        var since = LocalUsageSupport.EnrichmentScanStart(now, timeZone, firstDayOfWeek);
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null && now - _cache.LoadedAt < TimeSpan.FromSeconds(30) &&
                _cache.CoveredSince <= since)
            {
                return _cache.Entries.Where(entry => entry.Timestamp >= since).ToArray();
            }

            try
            {
                var loaded = await Task.Run(
                    () => Load(since, timeZone, cancellationToken), cancellationToken).ConfigureAwait(false);
                var entries = PreserveMissingEntries && _cache is not null
                    ? LocalUsageSupport.Deduplicate(_cache.Entries.Concat(loaded))
                    : LocalUsageSupport.Deduplicate(loaded);
                entries = entries.Where(entry => entry.Timestamp >= since).ToArray();
                _cache = new Cached(now, since, entries);
                return entries;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (_cache is not null)
            {
                return _cache.Entries.Where(entry => entry.Timestamp >= since).ToArray();
            }
        }
        finally
        {
            _scanLock.Release();
        }
    }

    protected static IEnumerable<string> Files(
        string root, string pattern, DateTimeOffset modifiedSince)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(path => File.GetLastWriteTimeUtc(path) >= modifiedSince.UtcDateTime)
                    .ToArray()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    protected static IReadOnlyList<string> RootsFromEnvironment(
        string key, IEnumerable<string> defaults, string? value = null)
    {
        value ??= Environment.GetEnvironmentVariable(key);
        return LocalUsageSupport.NormalizeRoots(string.IsNullOrWhiteSpace(value)
            ? defaults
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record Cached(
        DateTimeOffset LoadedAt, DateTimeOffset CoveredSince, IReadOnlyList<LocalUsageEntry> Entries);
}

public sealed class LocalOpenCodeUsageProvider : LocalAdditionalUsageProvider
{
    public LocalOpenCodeUsageProvider() : this(GetDefaultRoots()) { }
    public LocalOpenCodeUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "opencode";
    public override string DisplayName => "OpenCode";

    public static IReadOnlyList<string> GetDefaultRoots(
        string? userProfile = null, string? localAppData = null, string? environmentValue = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return RootsFromEnvironment("OPENCODE_DATA_DIR",
            [Path.Combine(localAppData, "opencode"), Path.Combine(userProfile, ".local", "share", "opencode")],
            environmentValue);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = OpenCodeDatabase(root);
            if (database is not null)
            {
                try
                {
                    var rows = LocalCursorUsageProvider.WithDatabaseCopy(database,
                        connection => connection.ReadTextRows("SELECT id, session_id, data FROM message", 3, true));
                    foreach (var row in rows)
                    {
                        if (row[0] is { } id && row[2] is { } json &&
                            ParseMessage(json, id, modifiedSince, timeZone) is { } entry)
                        {
                            entries.Add(entry);
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }

            var legacy = Path.Combine(root, "storage", "message");
            foreach (var file in Files(legacy, "*.json", modifiedSince))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (ParseMessage(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file),
                            modifiedSince, timeZone) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseMessage(
        string json, string fallbackId, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!AdditionalJson.Object(root, "tokens", out var tokens) ||
                !AdditionalJson.Object(root, "time", out var time) ||
                !AdditionalJson.Timestamp(time, "created", out var timestamp) || timestamp < modifiedSince ||
                AdditionalJson.String(root, "modelID") is not { } model ||
                AdditionalJson.String(root, "providerID") is null)
            {
                return null;
            }
            AdditionalJson.Object(tokens, "cache", out var cache);
            var input = AdditionalJson.Token(tokens, "input");
            var output = AdditionalJson.Token(tokens, "output");
            var write = AdditionalJson.Token(cache, "write");
            var read = AdditionalJson.Token(cache, "read");
            var total = AdditionalJson.Token(tokens, "total");
            var parts = input + output + write + read;
            if (total > parts) output += total - parts;
            if (input + output + write + read == 0) return null;
            var cost = AdditionalJson.Double(root, "cost") ??
                LocalUsageSupport.CalculateCost(model, input, output, write, read);
            return AdditionalJson.Entry("opencode|" + (AdditionalJson.String(root, "id") ?? fallbackId),
                timestamp, timeZone, input, output, write, read, cost);
        }
        catch (JsonException) { return null; }
    }

    private static string? OpenCodeDatabase(string root)
    {
        if (File.Exists(root) && Path.GetExtension(root).Equals(".db", StringComparison.OrdinalIgnoreCase)) return root;
        var standard = Path.Combine(root, "opencode.db");
        if (File.Exists(standard)) return standard;
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "opencode-*.db", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }
}

public sealed class LocalHermesUsageProvider : LocalAdditionalUsageProvider
{
    public LocalHermesUsageProvider() : this(GetDefaultRoots()) { }
    public LocalHermesUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "hermes";
    public override string DisplayName => "Hermes Agent";

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null, string? environmentValue = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return RootsFromEnvironment("HERMES_HOME", [Path.Combine(userProfile, ".hermes")], environmentValue);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = Path.GetExtension(root).Equals(".db", StringComparison.OrdinalIgnoreCase)
                ? root : Path.Combine(root, "state.db");
            if (!File.Exists(database)) continue;
            try
            {
                var rows = LocalCursorUsageProvider.WithDatabaseCopy(database, connection => connection.ReadTextRows("""
                    SELECT id, model, billing_provider, started_at, message_count,
                           input_tokens, output_tokens, cache_read_tokens, cache_write_tokens,
                           reasoning_tokens, estimated_cost_usd, actual_cost_usd FROM sessions
                    """, 12, true));
                foreach (var row in rows)
                {
                    if (ParseRow(row, modifiedSince, timeZone) is { } entry) entries.Add(entry);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseRow(
        string?[] row, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        if (row.Length < 12 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]) ||
            !AdditionalJson.Timestamp(row[3], out var timestamp) || timestamp < modifiedSince) return null;
        var input = AdditionalJson.Long(row[5]);
        var output = AdditionalJson.Long(row[6]) + AdditionalJson.Long(row[9]);
        var read = AdditionalJson.Long(row[7]);
        var write = AdditionalJson.Long(row[8]);
        if (input + output + read + write == 0) return null;
        var estimated = AdditionalJson.Number(row[10]);
        var actual = AdditionalJson.Number(row[11]);
        return AdditionalJson.Entry("hermes|" + row[0]!.Trim(), timestamp, timeZone,
            input, output, write, read, actual > 0 ? actual : estimated);
    }
}

public sealed class LocalGrokUsageProvider : LocalAdditionalUsageProvider
{
    public LocalGrokUsageProvider() : this(GetDefaultRoots()) { }
    public LocalGrokUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "grok";
    public override string DisplayName => "Grok";

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null, string? environmentValue = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        environmentValue ??= Environment.GetEnvironmentVariable("GROK_HOME");
        string[] defaults = string.IsNullOrWhiteSpace(environmentValue)
            ? [Path.Combine(userProfile, ".grok", "sessions")]
            : [Path.Combine(environmentValue.Trim(), "sessions")];
        return LocalUsageSupport.NormalizeRoots(defaults);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        foreach (var file in Files(root, "updates.jsonl", modifiedSince))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsSubagent(Path.GetDirectoryName(file)!)) continue;
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Contains("turn_completed", StringComparison.Ordinal) &&
                        ParseLine(line, modifiedSince, timeZone) is { } entry) entries.Add(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseLine(
        string line, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var envelope = document.RootElement;
            var notification = AdditionalJson.Object(envelope, "params", out var parameters) ? parameters : envelope;
            if (!AdditionalJson.Object(notification, "update", out var update) ||
                AdditionalJson.String(update, "sessionUpdate") != "turn_completed" ||
                !AdditionalJson.Object(update, "usage", out var usage) ||
                AdditionalJson.String(update, "prompt_id") is not { } turnId) return null;
            AdditionalJson.Object(notification, "_meta", out var meta);
            if (AdditionalJson.Bool(meta, "isReplay")) return null;
            if (!AdditionalJson.Timestamp(meta, "agentTimestampMs", out var timestamp) &&
                !AdditionalJson.Timestamp(envelope, "timestamp", out timestamp)) return null;
            if (timestamp < modifiedSince) return null;
            var output = AdditionalJson.TokenAny(usage, "outputTokens", "output_tokens");
            var reportedRead = AdditionalJson.TokenAny(usage, "cachedReadTokens", "cached_read_tokens");
            long input;
            long read;
            if (AdditionalJson.TryToken(usage, "inputTokens", out var full))
            {
                read = Math.Min(reportedRead, full);
                input = full - read;
            }
            else
            {
                input = AdditionalJson.Token(usage, "input_tokens");
                read = reportedRead;
            }
            var reportedTotal = AdditionalJson.TokenAny(usage, "totalTokens", "total_tokens");
            var parts = input + output + read;
            if (reportedTotal > parts) output += reportedTotal - parts;
            if (input + output + read == 0) return null;
            var cost = 0d;
            if (!AdditionalJson.Bool(usage, "usageIsIncomplete") &&
                !AdditionalJson.Bool(usage, "usage_is_incomplete") &&
                !AdditionalJson.Bool(usage, "costIsPartial") &&
                !AdditionalJson.Bool(usage, "cost_is_partial"))
            {
                var ticks = AdditionalJson.DoubleAny(usage, "costUsdTicks", "cost_usd_ticks") ?? 0;
                if (ticks > 0) cost = ticks / 1e10;
            }
            return AdditionalJson.Entry("grok|" + turnId, timestamp, timeZone,
                input, output, 0, read, cost, GrokModel(usage) ?? "grok");
        }
        catch (JsonException) { return null; }
    }

    private static bool IsSubagent(string directory)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")));
            return AdditionalJson.String(document.RootElement, "session_kind")?.StartsWith(
                "subagent", StringComparison.Ordinal) == true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string? GrokModel(JsonElement usage)
    {
        if (!AdditionalJson.Object(usage, "modelUsage", out var models) &&
            !AdditionalJson.Object(usage, "model_usage", out models)) return null;
        return models.EnumerateObject().Select(property =>
            (property.Name, Total: AdditionalJson.TokenAny(property.Value, "totalTokens", "total_tokens")))
            .OrderByDescending(item => item.Total).ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name).FirstOrDefault();
    }
}

public sealed class LocalCopilotUsageProvider : LocalAdditionalUsageProvider
{
    public LocalCopilotUsageProvider() : this(GetDefaultRoots()) { }
    public LocalCopilotUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "copilot";
    public override string DisplayName => "Copilot";
    public override bool ReportsCost => false;

    public static IReadOnlyList<string> GetDefaultRoots(string? userProfile = null, string? environmentValue = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return RootsFromEnvironment("COPILOT_HOME", [Path.Combine(userProfile, ".copilot")], environmentValue);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = Path.GetExtension(root).Equals(".db", StringComparison.OrdinalIgnoreCase)
                ? root : Path.Combine(root, "session-store.db");
            if (!File.Exists(database)) continue;
            try
            {
                var rows = LocalCursorUsageProvider.WithDatabaseCopy(database, connection => connection.ReadTextRows("""
                    SELECT id, model, input_tokens, output_tokens, cache_read_tokens,
                           cache_write_tokens, created_at FROM assistant_usage_events
                    """, 7, true));
                foreach (var row in rows)
                {
                    if (ParseRow(row, database, modifiedSince, timeZone) is { } entry) entries.Add(entry);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseRow(
        string?[] row, string database, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        if (row.Length < 7 || !long.TryParse(row[0], out var id) ||
            !AdditionalJson.Timestamp(row[6], out var timestamp) || timestamp < modifiedSince) return null;
        var model = string.IsNullOrWhiteSpace(row[1]) ? "unknown" : row[1]!.Trim();
        var fullInput = AdditionalJson.Long(row[2]);
        var output = AdditionalJson.Long(row[3]);
        var read = AdditionalJson.Long(row[4]);
        var write = AdditionalJson.Long(row[5]);
        var input = Math.Max(0, fullInput - read - write);
        if (input + output + read + write == 0) return null;
        return AdditionalJson.Entry($"copilot|{Path.GetFullPath(database)}|{id}", timestamp, timeZone,
            input, output, write, read, 0, model);
    }
}

public sealed class LocalKiroUsageProvider : LocalAdditionalUsageProvider
{
    public LocalKiroUsageProvider() : this(GetDefaultRoots()) { }
    public LocalKiroUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "kiro";
    public override string DisplayName => "Kiro";
    public override bool ReportsCost => false;
    protected override bool PreserveMissingEntries => true;

    public static IReadOnlyList<string> GetDefaultRoots(
        string? userProfile = null, string? appData = null,
        string? cliHome = null, string? kiroHome = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        cliHome ??= Environment.GetEnvironmentVariable("KIRO_CLI_HOME");
        kiroHome ??= Environment.GetEnvironmentVariable("KIRO_HOME");
        var legacy = string.IsNullOrWhiteSpace(cliHome)
            ? [Path.Combine(appData, "kiro-cli")]
            : cliHome.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var current = string.IsNullOrWhiteSpace(kiroHome)
            ? [Path.Combine(userProfile, ".kiro")]
            : kiroHome.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return LocalUsageSupport.NormalizeRoots(legacy.Concat(current.Select(path => Path.Combine(path, "sessions"))));
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = Path.GetExtension(root).Equals(".sqlite3", StringComparison.OrdinalIgnoreCase)
                ? root : Path.Combine(root, "data.sqlite3");
            if (seen.Add(database) && File.Exists(database))
            {
                try { entries.AddRange(ReadDatabase(database, modifiedSince, timeZone)); }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            foreach (var file in KiroFiles(root, modifiedSince))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                try
                {
                    entries.AddRange(Path.GetFileName(file).Equals("messages.jsonl", StringComparison.OrdinalIgnoreCase)
                        ? ParseV3(file, modifiedSince, timeZone)
                        : ParseCli(file, modifiedSince, timeZone));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    private static IReadOnlyList<LocalUsageEntry> ReadDatabase(
        string database, DateTimeOffset modifiedSince, TimeZoneInfo timeZone) =>
        LocalCursorUsageProvider.WithDatabaseCopy(database, connection =>
        {
            var entries = new List<LocalUsageEntry>();
            foreach (var row in connection.ReadTextRows("SELECT conversation_id, value FROM conversations_v2", 2))
            {
                if (row[1] is { } json) entries.AddRange(ParseConversation(json, row[0], modifiedSince, timeZone));
            }
            foreach (var row in connection.ReadTextRows("SELECT value FROM conversations", 1))
            {
                if (row[0] is { } json) entries.AddRange(ParseConversation(json, null, modifiedSince, timeZone));
            }
            return LocalUsageSupport.Deduplicate(entries);
        });

    internal static IReadOnlyList<LocalUsageEntry> ParseConversation(
        string json, string? fallbackId, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var conversation = fallbackId ?? AdditionalJson.String(root, "conversation_id");
            if (string.IsNullOrWhiteSpace(conversation) ||
                !root.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array) return [];
            long accumulated = root.TryGetProperty("latest_summary", out var summary)
                ? JsonBytes(summary) : 0;
            var entries = new List<LocalUsageEntry>();
            foreach (var turn in history.EnumerateArray())
            {
                if (turn.ValueKind != JsonValueKind.Object) continue;
                var user = turn.TryGetProperty("user", out var userValue) ? FieldBytes(userValue) : 0;
                var assistant = turn.TryGetProperty("assistant", out var assistantValue) ? FieldBytes(assistantValue) : 0;
                if (AdditionalJson.Object(turn, "request_metadata", out var metadata) &&
                    AdditionalJson.Timestamp(metadata, "request_start_timestamp_ms", out var timestamp) &&
                    timestamp >= modifiedSince)
                {
                    var input = (accumulated + user) / 4;
                    var output = AdditionalJson.Token(metadata, "response_size") / 4;
                    if (input + output > 0)
                    {
                        entries.Add(AdditionalJson.Entry(
                            $"kiro|{conversation}|{timestamp.ToUnixTimeMilliseconds()}", timestamp, timeZone,
                            input, output, 0, 0, 0,
                            AdditionalJson.String(metadata, "model_id") ?? "unknown"));
                    }
                }
                accumulated += user + assistant;
            }
            return entries;
        }
        catch (JsonException) { return []; }
    }

    private static IEnumerable<string> KiroFiles(string root, DateTimeOffset modifiedSince) =>
        Files(root, "*.jsonl", modifiedSince).Where(file =>
            Path.GetFileName(file).Equals("messages.jsonl", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("cli", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<LocalUsageEntry> ParseCli(
        string file, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        string session = Path.GetFileNameWithoutExtension(file);
        var model = "unknown";
        var companion = Path.ChangeExtension(file, ".json");
        try
        {
            using var info = JsonDocument.Parse(File.ReadAllText(companion));
            session = AdditionalJson.String(info.RootElement, "session_id") ?? session;
            if (AdditionalJson.Object(info.RootElement, "session_state", out var state) &&
                AdditionalJson.Object(state, "rts_model_state", out var modelState) &&
                AdditionalJson.Object(modelState, "model_info", out var modelInfo))
                model = AdditionalJson.String(modelInfo, "model_id") ?? model;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }

        var entries = new List<LocalUsageEntry>();
        long history = 0, prompt = 0, assistant = 0, tools = 0;
        DateTimeOffset? date = null;
        var started = false;
        void Flush()
        {
            if (started && date is { } timestamp && timestamp >= modifiedSince)
            {
                var input = (history + prompt + tools) / 4;
                var output = assistant / 4;
                if (input + output > 0) entries.Add(AdditionalJson.Entry(
                    $"kiro|cli|{session}|{timestamp.ToUnixTimeMilliseconds()}", timestamp, timeZone,
                    input, output, 0, 0, 0, model));
            }
            history += prompt + assistant + tools;
            prompt = assistant = tools = 0;
            date = null;
        }
        foreach (var line in File.ReadLines(file))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var kind = AdditionalJson.String(root, "kind");
                AdditionalJson.Object(root, "data", out var data);
                if (kind == "Prompt")
                {
                    if (started) Flush();
                    started = true;
                    prompt = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("content", out var content)
                        ? TextBytes(content) : 0;
                    if (AdditionalJson.Object(data, "meta", out var meta) &&
                        AdditionalJson.Timestamp(meta, "timestamp", out var timestamp)) date = timestamp;
                }
                else if (kind == "AssistantMessage" && data.ValueKind == JsonValueKind.Object &&
                         data.TryGetProperty("content", out var content))
                    assistant += TextBytes(content);
                else if (kind == "ToolResults" && data.ValueKind == JsonValueKind.Object &&
                         data.TryGetProperty("content", out content))
                    tools += TextBytes(content);
                else if (kind == "Clear") { Flush(); started = false; history = 0; }
            }
            catch (JsonException) { }
        }
        Flush();
        return entries;
    }

    private static IReadOnlyList<LocalUsageEntry> ParseV3(
        string file, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        var session = Path.GetFileName(Path.GetDirectoryName(file));
        var model = "unknown";
        DateTimeOffset? fallback = null;
        try
        {
            using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "session.json")));
            session = AdditionalJson.String(info.RootElement, "id") ?? session;
            model = AdditionalJson.String(info.RootElement, "modelId") ?? model;
            if (AdditionalJson.Timestamp(info.RootElement, "createdAt", out var created)) fallback = created;
            else if (AdditionalJson.Timestamp(info.RootElement, "lastModifiedAt", out var modified)) fallback = modified;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }

        var entries = new List<LocalUsageEntry>();
        long history = 0, prompt = 0, assistant = 0;
        DateTimeOffset? date = null;
        var started = false;
        var index = 0;
        void Flush()
        {
            var had = started && prompt + assistant > 0;
            var timestamp = date ?? fallback;
            if (had && timestamp is { } stamp && stamp >= modifiedSince)
            {
                var input = (history + prompt) / 4;
                var output = assistant / 4;
                if (input + output > 0) entries.Add(AdditionalJson.Entry(
                    $"kiro|v3|{session}|{index}", stamp, timeZone, input, output, 0, 0, 0, model));
            }
            if (had) index++;
            history += prompt + assistant;
            prompt = assistant = 0;
            date = null;
        }
        foreach (var line in File.ReadLines(file))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                AdditionalJson.Timestamp(root, "timestamp", out var eventDate);
                if (AdditionalJson.Object(root, "payload", out var payload) &&
                    AdditionalJson.String(payload, "type") is { } type)
                {
                    if (type == "user")
                    {
                        if (started) Flush(); started = true;
                        prompt = payload.TryGetProperty("content", out var content) ? TextBytes(content) : 0;
                        date = eventDate == default ? null : eventDate;
                    }
                    else if (type == "assistant")
                    {
                        if (payload.TryGetProperty("content", out var content)) assistant += TextBytes(content);
                        started |= assistant > 0; if (date is null && eventDate != default) date = eventDate;
                    }
                    else if (type == "tool_call" && payload.TryGetProperty("args", out var args))
                    {
                        assistant += args.ValueKind == JsonValueKind.String
                            ? System.Text.Encoding.UTF8.GetByteCount(args.GetString() ?? "") : JsonBytes(args);
                        started |= assistant > 0;
                    }
                    else if (type == "tool_result" && payload.TryGetProperty("content", out var content))
                        prompt += TextBytes(content);
                    else if (type == "turn_end") { if (date is null && eventDate != default) date = eventDate; Flush(); started = false; }
                }
                else if (AdditionalJson.String(root, "role") is { } role)
                {
                    if (role is "user" or "human" or "prompt")
                    {
                        if (started) Flush(); started = true;
                        prompt = root.TryGetProperty("content", out var content) ? TextBytes(content) : 0;
                        date = eventDate == default ? null : eventDate;
                    }
                    else if (role is "assistant" or "bot")
                    {
                        if (root.TryGetProperty("content", out var content)) assistant += TextBytes(content);
                        started |= assistant > 0; if (date is null && eventDate != default) date = eventDate;
                    }
                }
            }
            catch (JsonException) { }
        }
        Flush();
        return entries;
    }

    private static long FieldBytes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return System.Text.Encoding.UTF8.GetByteCount(value.GetString() ?? "");
        if (value.ValueKind != JsonValueKind.Object) return 0;
        return value.EnumerateObject().Where(property => property.Name != "images")
            .Sum(property => JsonBytes(property.Value));
    }

    private static long TextBytes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return System.Text.Encoding.UTF8.GetByteCount(value.GetString() ?? "");
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Sum(TextBytes);
        if (value.ValueKind != JsonValueKind.Object) return 0;
        if (AdditionalJson.String(value, "kind") is { } kind)
            return kind == "text" && value.TryGetProperty("data", out var data) ? TextBytes(data) : 0;
        foreach (var key in new[] { "content", "text", "data" })
            if (value.TryGetProperty(key, out var child)) return TextBytes(child);
        return 0;
    }

    private static long JsonBytes(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => System.Text.Encoding.UTF8.GetByteCount(value.GetString() ?? ""),
        JsonValueKind.Number => System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()),
        JsonValueKind.Array => value.EnumerateArray().Sum(JsonBytes),
        JsonValueKind.Object => value.EnumerateObject().Sum(property => JsonBytes(property.Value)),
        _ => 0,
    };
}

public sealed class LocalPiUsageProvider : LocalAdditionalUsageProvider
{
    public LocalPiUsageProvider() : this(GetDefaultRoots()) { }
    public LocalPiUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "pi";
    public override string DisplayName => "Pi";
    public override bool ReportsCost => false;

    public static IReadOnlyList<string> GetDefaultRoots(
        string? userProfile = null, string? agentDirectory = null, string? sessionDirectory = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        agentDirectory ??= Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR");
        sessionDirectory ??= Environment.GetEnvironmentVariable("PI_CODING_AGENT_SESSION_DIR");
        var roots = new List<string> { Path.Combine(userProfile, ".pi", "agent", "sessions") };
        if (!string.IsNullOrWhiteSpace(agentDirectory)) roots.Add(Path.Combine(agentDirectory.Trim(), "sessions"));
        if (!string.IsNullOrWhiteSpace(sessionDirectory)) roots.Add(sessionDirectory.Trim());
        return LocalUsageSupport.NormalizeRoots(roots);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        foreach (var file in Files(root, "*.jsonl", modifiedSince))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var index = 0;
                foreach (var line in File.ReadLines(file))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Contains("\"usage\"", StringComparison.Ordinal) &&
                        ParseLine(line, Path.GetFileName(file), index++, modifiedSince, timeZone) is { } entry)
                        entries.Add(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseLine(
        string line, string file, int lineIndex, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var envelope = document.RootElement;
            var id = AdditionalJson.String(envelope, "id");
            var type = AdditionalJson.String(envelope, "type");
            if (string.IsNullOrWhiteSpace(id) || type is null) return null;
            JsonElement usage;
            DateTimeOffset timestamp;
            if (type == "message")
            {
                if (!AdditionalJson.Object(envelope, "message", out var message) ||
                    AdditionalJson.String(message, "stopReason") is "aborted" or "error" ||
                    !AdditionalJson.Object(message, "usage", out usage)) return null;
                if (!AdditionalJson.Timestamp(message, "timestamp", out timestamp) &&
                    !AdditionalJson.Timestamp(envelope, "timestamp", out timestamp)) return null;
            }
            else if (type is "compaction" or "branch_summary")
            {
                if (!AdditionalJson.Object(envelope, "usage", out usage) ||
                    !AdditionalJson.Timestamp(envelope, "timestamp", out timestamp)) return null;
            }
            else return null;
            if (timestamp < modifiedSince) return null;
            var buckets = UsageBuckets(usage);
            if (buckets is null) return null;
            return AdditionalJson.Entry(id, timestamp, timeZone,
                buckets.Value.Input, buckets.Value.Output, buckets.Value.Write, buckets.Value.Read, 0, "pi");
        }
        catch (JsonException) { return null; }
    }

    internal static (long Input, long Output, long Write, long Read)? UsageBuckets(JsonElement usage)
    {
        var has = AdditionalJson.TryToken(usage, "input", out var input);
        has |= AdditionalJson.TryToken(usage, "output", out var output);
        has |= AdditionalJson.TryToken(usage, "cacheWrite", out var write);
        has |= AdditionalJson.TryToken(usage, "cacheRead", out var read);
        if (has) return (input, output, write, read);
        return AdditionalJson.TryToken(usage, "totalTokens", out var total)
            ? (total, 0, 0, 0) : null;
    }
}

public sealed class LocalOmpUsageProvider : LocalAdditionalUsageProvider
{
    public LocalOmpUsageProvider() : this(GetDefaultRoots()) { }
    public LocalOmpUsageProvider(IEnumerable<string> roots) : base(roots) { }
    public override string Id => "omp";
    public override string DisplayName => "omp";

    public static IReadOnlyList<string> GetDefaultRoots(
        string? userProfile = null, string? agentDirectory = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        agentDirectory ??= Environment.GetEnvironmentVariable("OMP_CODING_AGENT_DIR");
        var roots = new List<string> { Path.Combine(userProfile, ".omp", "agent", "sessions") };
        if (!string.IsNullOrWhiteSpace(agentDirectory)) roots.Add(Path.Combine(agentDirectory.Trim(), "sessions"));
        return LocalUsageSupport.NormalizeRoots(roots);
    }

    internal override IReadOnlyList<LocalUsageEntry> Load(
        DateTimeOffset modifiedSince, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var entries = new List<LocalUsageEntry>();
        foreach (var root in Roots)
        foreach (var file in Files(root, "*.jsonl", modifiedSince))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("bridge", StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                var index = 0;
                foreach (var line in File.ReadLines(file))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Contains("\"usage\"", StringComparison.Ordinal) &&
                        ParseLine(line, Path.GetFileName(file), index++, modifiedSince, timeZone) is { } entry)
                        entries.Add(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return LocalUsageSupport.Deduplicate(entries);
    }

    internal static LocalUsageEntry? ParseLine(
        string line, string file, int lineIndex, DateTimeOffset modifiedSince, TimeZoneInfo timeZone)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var envelope = document.RootElement;
            var type = AdditionalJson.String(envelope, "type");
            JsonElement usage;
            DateTimeOffset timestamp;
            var model = "omp";
            if (type == "message")
            {
                if (!AdditionalJson.Object(envelope, "message", out var message) ||
                    AdditionalJson.String(message, "role") != "assistant" ||
                    AdditionalJson.String(message, "stopReason") is "aborted" or "error" ||
                    !AdditionalJson.Object(message, "usage", out usage)) return null;
                model = AdditionalJson.String(message, "model") ?? model;
                if (!AdditionalJson.Timestamp(message, "timestamp", out timestamp) &&
                    !AdditionalJson.Timestamp(envelope, "timestamp", out timestamp)) return null;
            }
            else if (type is "compaction" or "branch_summary")
            {
                if (!AdditionalJson.Object(envelope, "usage", out usage) ||
                    !AdditionalJson.Timestamp(envelope, "timestamp", out timestamp)) return null;
            }
            else return null;
            if (timestamp < modifiedSince) return null;
            var buckets = LocalPiUsageProvider.UsageBuckets(usage);
            if (buckets is null) return null;
            var sourceCost = AdditionalJson.Object(usage, "cost", out var costObject)
                ? AdditionalJson.Double(costObject, "total") : null;
            var cost = sourceCost is > 0 ? sourceCost.Value : LocalUsageSupport.CalculateCost(
                model, buckets.Value.Input, buckets.Value.Output, buckets.Value.Write, buckets.Value.Read);
            var id = AdditionalJson.String(envelope, "id") ?? $"missing-{lineIndex}";
            return AdditionalJson.Entry($"omp|{file}|{id}", timestamp, timeZone,
                buckets.Value.Input, buckets.Value.Output, buckets.Value.Write, buckets.Value.Read, cost, model);
        }
        catch (JsonException) { return null; }
    }
}

internal static class AdditionalJson
{
    private const long MaxToken = 1_000_000_000_000_000;

    public static LocalUsageEntry Entry(
        string id, DateTimeOffset timestamp, TimeZoneInfo timeZone,
        long input, long output, long write, long read, double cost, string model = "unknown") =>
        new(id, timestamp, LocalUsageSupport.LocalDate(timestamp, timeZone),
            input, output, write, read, double.IsFinite(cost) && cost > 0 ? cost : 0);

    public static bool Object(JsonElement parent, string property, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    public static string? String(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static bool Bool(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);

    public static long Token(JsonElement parent, string property) =>
        TryToken(parent, property, out var value) ? value : 0;

    public static long TokenAny(JsonElement parent, string first, string second) =>
        TryToken(parent, first, out var value) || TryToken(parent, second, out value) ? value : 0;

    public static bool TryToken(JsonElement parent, string property, out long result)
    {
        result = 0;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
        double number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) ||
            value.ValueKind == JsonValueKind.String && double.TryParse(
                value.GetString()?.Replace(",", "", StringComparison.Ordinal),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            if (!double.IsFinite(number)) return false;
            result = number <= 0 ? 0 : number >= MaxToken ? MaxToken : (long)number;
            return true;
        }
        return false;
    }

    public static double? Double(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ||
         value.ValueKind == JsonValueKind.String && double.TryParse(
             value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) &&
        double.IsFinite(number) ? number : null;

    public static double? DoubleAny(JsonElement parent, string first, string second) =>
        Double(parent, first) ?? Double(parent, second);

    public static long Long(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed) : 0;

    public static double Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) ? parsed : 0;

    public static bool Timestamp(JsonElement parent, string property, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return Epoch(number, out timestamp);
        if (value.ValueKind != JsonValueKind.String) return false;
        var raw = value.GetString();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? Epoch(number, out timestamp)
            : DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    public static bool Timestamp(string? raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return Epoch(number, out timestamp);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var normalized = raw.Contains(' ') && !raw.Contains('T') ? raw.Replace(' ', 'T') : raw;
        if (normalized.Length >= 19 && normalized[10] == 'T' &&
            !normalized[11..].Contains('Z') && !normalized[11..].Contains('+') &&
            !normalized[11..].Contains('-')) normalized += "Z";
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    private static bool Epoch(double number, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (!double.IsFinite(number) || number <= 0) return false;
        try
        {
            timestamp = number >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
                : DateTimeOffset.FromUnixTimeSeconds((long)number);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
