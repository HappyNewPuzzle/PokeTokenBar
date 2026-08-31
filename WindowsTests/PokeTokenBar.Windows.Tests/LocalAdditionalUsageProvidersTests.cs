using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalAdditionalUsageProvidersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    public static TheoryData<string> Providers => new()
    {
        "opencode", "hermes", "grok", "copilot", "kiro", "pi", "omp",
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void Metadata_MatchesUpstreamContract(string id)
    {
        var provider = CreateProvider(id, [Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]);

        Assert.Equal(id, provider.Id);
        Assert.Equal(id switch
        {
            "opencode" => "OpenCode",
            "hermes" => "Hermes Agent",
            "grok" => "Grok",
            "copilot" => "Copilot",
            "kiro" => "Kiro",
            "pi" => "Pi",
            _ => "omp",
        }, provider.DisplayName);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void DefaultRoot_UsesWindowsOrHomeConvention(string id)
    {
        var profile = Path.Combine("C:\\Users", "fixture");
        var local = Path.Combine(profile, "AppData", "Local");
        var roaming = Path.Combine(profile, "AppData", "Roaming");
        var roots = id switch
        {
            "opencode" => LocalOpenCodeUsageProvider.GetDefaultRoots(profile, local, ""),
            "hermes" => LocalHermesUsageProvider.GetDefaultRoots(profile, ""),
            "grok" => LocalGrokUsageProvider.GetDefaultRoots(profile, ""),
            "copilot" => LocalCopilotUsageProvider.GetDefaultRoots(profile, ""),
            "kiro" => LocalKiroUsageProvider.GetDefaultRoots(profile, roaming, "", ""),
            "pi" => LocalPiUsageProvider.GetDefaultRoots(profile, "", ""),
            _ => LocalOmpUsageProvider.GetDefaultRoots(profile, ""),
        };

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.StartsWith(Path.GetFullPath(profile), root, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MissingRoot_ReturnsEmptyUsage(string id)
    {
        var provider = CreateProvider(id, [Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]);

        Assert.Null(await Daily(provider));
        var periods = await Enrichment(provider);
        Assert.Null(periods.ActiveBlock);
        Assert.Equal(0, periods.WeekTotal?.TotalTokens);
        Assert.Equal(0, periods.MonthTotal?.TotalTokens);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task NormalFixture_MapsTokenBuckets(string id)
    {
        using var fixture = new ProviderFixture(id, [Now.AddHours(-1)]);

        var usage = Assert.IsType<DailyUsage>(await Daily(fixture.Provider));
        Assert.Equal(ExpectedBuckets(id),
            (usage.InputTokens, usage.OutputTokens, usage.CacheCreationTokens, usage.CacheReadTokens));
        Assert.Equal(usage.InputTokens + usage.OutputTokens + usage.CacheCreationTokens + usage.CacheReadTokens,
            usage.TotalTokens);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void MalformedRecord_IsIgnored(string id)
    {
        var since = Now.AddMonths(-1);
        var result = id switch
        {
            "opencode" => LocalOpenCodeUsageProvider.ParseMessage("{", "bad", since, Utc) is null,
            "hermes" => LocalHermesUsageProvider.ParseRow(["bad"], since, Utc) is null,
            "grok" => LocalGrokUsageProvider.ParseLine("{", since, Utc) is null,
            "copilot" => LocalCopilotUsageProvider.ParseRow(["bad"], "x.db", since, Utc) is null,
            "kiro" => LocalKiroUsageProvider.ParseConversation("{", null, since, Utc).Count == 0,
            "pi" => LocalPiUsageProvider.ParseLine("{", "x", 0, since, Utc) is null,
            _ => LocalOmpUsageProvider.ParseLine("{", "x", 0, since, Utc) is null,
        };

        Assert.True(result);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DuplicateRecord_IsCountedOnce(string id)
    {
        using var fixture = new ProviderFixture(id, [Now.AddHours(-1), Now.AddHours(-1)], duplicateIds: true);

        var usage = Assert.IsType<DailyUsage>(await Daily(fixture.Provider));
        Assert.Equal(ExpectedTotal(id), usage.TotalTokens);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Periods_IncludeTodayAndOlderMonthUsage(string id)
    {
        using var fixture = new ProviderFixture(id, [Now.AddHours(-1), Now.AddDays(-21)]);

        var usage = Assert.IsType<DailyUsage>(await Daily(fixture.Provider));
        var periods = await Enrichment(fixture.Provider);
        Assert.Equal(ExpectedTotal(id), usage.TotalTokens);
        Assert.Equal(ExpectedTotal(id), periods.ActiveBlock?.TotalTokens);
        Assert.Equal(ExpectedTotal(id), periods.WeekTotal?.TotalTokens);
        Assert.Equal(ExpectedTotal(id) * 2, periods.MonthTotal?.TotalTokens);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MultipleSessions_AreAggregated(string id)
    {
        using var fixture = new ProviderFixture(id, [Now.AddHours(-1), Now.AddHours(-2)]);

        var usage = Assert.IsType<DailyUsage>(await Daily(fixture.Provider));
        Assert.Equal(ExpectedTotal(id) * 2, usage.TotalTokens);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task CostReporting_MatchesProviderSemantics(string id)
    {
        using var fixture = new ProviderFixture(id, [Now.AddHours(-1)]);

        var usage = Assert.IsType<DailyUsage>(await Daily(fixture.Provider));
        var reportsCost = id is not ("copilot" or "kiro" or "pi");
        Assert.Equal(reportsCost, fixture.Provider.ReportsCost);
        Assert.Equal(reportsCost ? 1.25 : 0, usage.TotalCost, 6);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ProviderSpecificEdge_MatchesUpstreamSemantics(string id)
    {
        var since = Now.AddDays(-1);
        switch (id)
        {
            case "opencode":
            {
                var entry = Assert.IsType<LocalUsageEntry>(LocalOpenCodeUsageProvider.ParseMessage(
                    JsonSerializer.Serialize(new
                    {
                        id = "edge", modelID = "model", providerID = "provider",
                        time = new { created = Now.ToUnixTimeMilliseconds() },
                        tokens = new { input = 1, output = 1, total = 10 },
                    }), "fallback", since, Utc));
                Assert.Equal(10, entry.TotalTokens);
                Assert.Equal(9, entry.Output);
                break;
            }
            case "hermes":
            {
                var row = new string?[] { "id", "model", "provider", Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), "1", "1", "1", "0", "0", "1", "2.5", "0" };
                Assert.Equal(2.5, Assert.IsType<LocalUsageEntry>(
                    LocalHermesUsageProvider.ParseRow(row, since, Utc)).Cost);
                break;
            }
            case "grok":
            {
                var replay = JsonSerializer.Serialize(new
                {
                    @params = new
                    {
                        update = new { sessionUpdate = "turn_completed", prompt_id = "id", usage = new { inputTokens = 1 } },
                        _meta = new { isReplay = true, agentTimestampMs = Now.ToUnixTimeMilliseconds() },
                    },
                });
                Assert.Null(LocalGrokUsageProvider.ParseLine(replay, since, Utc));
                break;
            }
            case "copilot":
            {
                var row = new string?[] { "1", "model", "2", "3", "4", "5", Now.ToString("O") };
                var entry = Assert.IsType<LocalUsageEntry>(
                    LocalCopilotUsageProvider.ParseRow(row, "fixture.db", since, Utc));
                Assert.Equal(0, entry.Input);
                Assert.Equal(12, entry.TotalTokens);
                break;
            }
            case "kiro":
            {
                var entries = LocalKiroUsageProvider.ParseConversation(JsonSerializer.Serialize(new
                {
                    conversation_id = "id",
                    history = new object[]
                    {
                        "malformed",
                        new
                        {
                            user = new { content = new string('u', 8), images = new[] { new string('x', 100) } },
                            assistant = new { content = new string('a', 4) },
                            request_metadata = new { request_start_timestamp_ms = Now.ToUnixTimeMilliseconds(), response_size = 4 },
                        },
                    },
                }), null, since, Utc);
                Assert.Equal(3, Assert.Single(entries).TotalTokens);
                break;
            }
            case "pi":
            {
                var line = JsonSerializer.Serialize(new
                {
                    id = "compact", type = "compaction", timestamp = Now.ToString("O"), usage = new { totalTokens = 25 },
                });
                var entry = Assert.IsType<LocalUsageEntry>(LocalPiUsageProvider.ParseLine(line, "x", 0, since, Utc));
                Assert.Equal(25, entry.Input);
                break;
            }
            default:
            {
                var line = JsonSerializer.Serialize(new
                {
                    type = "message",
                    message = new
                    {
                        role = "assistant", model = "claude-3-5-sonnet", timestamp = Now.ToUnixTimeMilliseconds(),
                        usage = new { input = 1000, output = 10 },
                    },
                });
                var entry = Assert.IsType<LocalUsageEntry>(LocalOmpUsageProvider.ParseLine(line, "x", 7, since, Utc));
                Assert.StartsWith("omp|x|missing-7", entry.Id, StringComparison.Ordinal);
                Assert.True(entry.Cost > 0);
                break;
            }
        }
    }

    [Fact]
    public async Task OpenCode_ReadsPreferredSqliteFormat()
    {
        using var directory = new TempDirectory();
        var json = JsonSerializer.Serialize(new
        {
            id = "db", modelID = "model", providerID = "provider",
            time = new { created = Now.ToUnixTimeMilliseconds() },
            tokens = new { input = 10, output = 5 }, cost = 1.25,
        });
        using (var database = WritableSqlite.Open(Path.Combine(directory.Path, "opencode.db")))
        {
            database.Execute("CREATE TABLE message (id TEXT, session_id TEXT, data TEXT)");
            database.Execute($"INSERT INTO message VALUES ('db', 'session', '{Sql(json)}')");
        }

        var usage = Assert.IsType<DailyUsage>(await Daily(new LocalOpenCodeUsageProvider([directory.Path])));
        Assert.Equal(15, usage.TotalTokens);
    }

    [Fact]
    public async Task Kiro_ReadsCli220JsonlFormat()
    {
        using var directory = new TempDirectory();
        var cli = Path.Combine(directory.Path, "cli");
        Directory.CreateDirectory(cli);
        File.WriteAllLines(Path.Combine(cli, "session.jsonl"),
        [
            JsonSerializer.Serialize(new { kind = "Prompt", data = new { content = new string('u', 40), meta = new { timestamp = Now.ToString("O") } } }),
            JsonSerializer.Serialize(new { kind = "AssistantMessage", data = new { content = new string('a', 20) } }),
        ]);

        var usage = Assert.IsType<DailyUsage>(await Daily(new LocalKiroUsageProvider([directory.Path])));
        Assert.Equal(15, usage.TotalTokens);
    }

    [Fact]
    public async Task Kiro_ReadsCurrentMessagesJsonlFormat()
    {
        using var directory = new TempDirectory();
        var session = Path.Combine(directory.Path, "session");
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "session.json"),
            JsonSerializer.Serialize(new { id = "session", modelId = "model", createdAt = Now.ToString("O") }));
        File.WriteAllLines(Path.Combine(session, "messages.jsonl"),
        [
            JsonSerializer.Serialize(new { timestamp = Now.ToString("O"), payload = new { type = "user", content = new string('u', 40) } }),
            JsonSerializer.Serialize(new { timestamp = Now.ToString("O"), payload = new { type = "assistant", content = new string('a', 20) } }),
            JsonSerializer.Serialize(new { timestamp = Now.ToString("O"), payload = new { type = "turn_end" } }),
        ]);

        var usage = Assert.IsType<DailyUsage>(await Daily(new LocalKiroUsageProvider([directory.Path])));
        Assert.Equal(15, usage.TotalTokens);
    }

    [Fact]
    public async Task Grok_ExcludesSubagentSession()
    {
        using var directory = new TempDirectory();
        var session = Path.Combine(directory.Path, "session");
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "summary.json"), "{\"session_kind\":\"subagent_task\"}");
        File.WriteAllText(Path.Combine(session, "updates.jsonl"), JsonSerializer.Serialize(new
        {
            @params = new
            {
                update = new { sessionUpdate = "turn_completed", prompt_id = "id", usage = new { inputTokens = 10 } },
                _meta = new { agentTimestampMs = Now.ToUnixTimeMilliseconds() },
            },
        }));

        Assert.Null(await Daily(new LocalGrokUsageProvider([directory.Path])));
    }

    [Fact]
    public async Task Omp_ExcludesBridgeDirectory()
    {
        using var directory = new TempDirectory();
        var bridge = Path.Combine(directory.Path, "bridge");
        Directory.CreateDirectory(bridge);
        File.WriteAllText(Path.Combine(bridge, "session.jsonl"), JsonSerializer.Serialize(new
        {
            id = "id", type = "message",
            message = new { role = "assistant", timestamp = Now.ToUnixTimeMilliseconds(), usage = new { input = 10 } },
        }));

        Assert.Null(await Daily(new LocalOmpUsageProvider([directory.Path])));
    }

    private static Task<DailyUsage?> Daily(IUsageProvider provider) => provider switch
    {
        LocalAdditionalUsageProvider local => local.FetchDailyAsync(Now, Utc, DayOfWeek.Monday),
        _ => throw new InvalidOperationException(),
    };

    private static Task<ProviderEnrichment> Enrichment(IUsageProvider provider) => provider switch
    {
        LocalAdditionalUsageProvider local => local.FetchEnrichmentAsync(Now, Utc, DayOfWeek.Monday),
        _ => throw new InvalidOperationException(),
    };

    private static IUsageProvider CreateProvider(string id, IEnumerable<string> roots) => id switch
    {
        "opencode" => new LocalOpenCodeUsageProvider(roots),
        "hermes" => new LocalHermesUsageProvider(roots),
        "grok" => new LocalGrokUsageProvider(roots),
        "copilot" => new LocalCopilotUsageProvider(roots),
        "kiro" => new LocalKiroUsageProvider(roots),
        "pi" => new LocalPiUsageProvider(roots),
        _ => new LocalOmpUsageProvider(roots),
    };

    private static (long Input, long Output, long Write, long Read) ExpectedBuckets(string id) => id switch
    {
        "grok" => (10, 5, 0, 2),
        "kiro" => (10, 5, 0, 0),
        _ => (10, 5, 1, 2),
    };

    private static long ExpectedTotal(string id)
    {
        var value = ExpectedBuckets(id);
        return value.Input + value.Output + value.Write + value.Read;
    }

    private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "poketokenbar-phase3d-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class ProviderFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "poketokenbar-phase3d-" + Guid.NewGuid().ToString("N"));

        public ProviderFixture(string id, IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds = false)
        {
            Directory.CreateDirectory(_root);
            Write(id, timestamps, duplicateIds);
            Provider = CreateProvider(id, [_root]);
        }

        public IUsageProvider Provider { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private void Write(string id, IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            switch (id)
            {
                case "opencode": WriteOpenCode(timestamps, duplicateIds); break;
                case "hermes": WriteHermes(timestamps, duplicateIds); break;
                case "grok": WriteGrok(timestamps, duplicateIds); break;
                case "copilot": WriteCopilot(timestamps, duplicateIds); break;
                case "kiro": WriteKiro(timestamps, duplicateIds); break;
                case "pi": WritePi(timestamps, duplicateIds); break;
                default: WriteOmp(timestamps, duplicateIds); break;
            }
        }

        private void WriteOpenCode(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            var directory = Path.Combine(_root, "storage", "message", "session");
            Directory.CreateDirectory(directory);
            for (var index = 0; index < timestamps.Count; index++)
            {
                var rowId = duplicateIds ? "same" : $"row-{index}";
                File.WriteAllText(Path.Combine(directory, $"{index}.json"), JsonSerializer.Serialize(new
                {
                    id = rowId,
                    modelID = "fixture-model",
                    providerID = "fixture",
                    time = new { created = timestamps[index].ToUnixTimeMilliseconds() },
                    tokens = new { input = 10, output = 5, cache = new { write = 1, read = 2 }, total = 18 },
                    cost = 1.25,
                }));
            }
        }

        private void WriteHermes(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            using var database = WritableSqlite.Open(Path.Combine(_root, "state.db"));
            database.Execute("CREATE TABLE sessions (id TEXT, model TEXT, billing_provider TEXT, started_at TEXT, message_count TEXT, input_tokens TEXT, output_tokens TEXT, cache_read_tokens TEXT, cache_write_tokens TEXT, reasoning_tokens TEXT, estimated_cost_usd TEXT, actual_cost_usd TEXT)");
            for (var index = 0; index < timestamps.Count; index++)
                database.Execute($"INSERT INTO sessions VALUES ('{(duplicateIds ? "same" : $"row-{index}")}', 'fixture-model', 'fixture', '{timestamps[index].ToUnixTimeMilliseconds()}', '1', '10', '4', '2', '1', '1', '0.5', '1.25')");
        }

        private void WriteGrok(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            var directory = Path.Combine(_root, "session");
            Directory.CreateDirectory(directory);
            var lines = timestamps.Select((timestamp, index) => JsonSerializer.Serialize(new
            {
                @params = new
                {
                    update = new
                    {
                        sessionUpdate = "turn_completed",
                        prompt_id = duplicateIds ? "same" : $"row-{index}",
                        usage = new { inputTokens = 12, outputTokens = 5, cachedReadTokens = 2, totalTokens = 17, costUsdTicks = 12_500_000_000d },
                    },
                    _meta = new { agentTimestampMs = timestamp.ToUnixTimeMilliseconds() },
                },
            }));
            File.WriteAllLines(Path.Combine(directory, "updates.jsonl"), lines);
        }

        private void WriteCopilot(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            using var database = WritableSqlite.Open(Path.Combine(_root, "session-store.db"));
            database.Execute("CREATE TABLE assistant_usage_events (id INTEGER, model TEXT, input_tokens TEXT, output_tokens TEXT, cache_read_tokens TEXT, cache_write_tokens TEXT, created_at TEXT)");
            for (var index = 0; index < timestamps.Count; index++)
                database.Execute($"INSERT INTO assistant_usage_events VALUES ({(duplicateIds ? 1 : index + 1)}, 'fixture-model', '13', '5', '2', '1', '{timestamps[index]:O}')");
        }

        private void WriteKiro(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            using var database = WritableSqlite.Open(Path.Combine(_root, "data.sqlite3"));
            database.Execute("CREATE TABLE conversations_v2 (conversation_id TEXT, value TEXT)");
            for (var index = 0; index < timestamps.Count; index++)
            {
                var conversation = duplicateIds ? "same" : $"row-{index}";
                var json = JsonSerializer.Serialize(new
                {
                    conversation_id = conversation,
                    history = new[]
                    {
                        new
                        {
                            user = new { content = new string('u', 40) },
                            assistant = new { content = new string('a', 20) },
                            request_metadata = new
                            {
                                request_start_timestamp_ms = timestamps[index].ToUnixTimeMilliseconds(),
                                response_size = 20,
                            },
                        },
                    },
                });
                database.Execute($"INSERT INTO conversations_v2 VALUES ('{conversation}', '{Sql(json)}')");
            }
        }

        private void WritePi(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            var lines = timestamps.Select((timestamp, index) => JsonSerializer.Serialize(new
            {
                id = duplicateIds ? "same" : $"row-{index}",
                type = "message",
                message = new
                {
                    timestamp = timestamp.ToUnixTimeMilliseconds(),
                    usage = new { input = 10, output = 5, cacheWrite = 1, cacheRead = 2 },
                },
            }));
            File.WriteAllLines(Path.Combine(_root, "session.jsonl"), lines);
        }

        private void WriteOmp(IReadOnlyList<DateTimeOffset> timestamps, bool duplicateIds)
        {
            var lines = timestamps.Select((timestamp, index) => JsonSerializer.Serialize(new
            {
                id = duplicateIds ? "same" : $"row-{index}",
                type = "message",
                message = new
                {
                    role = "assistant",
                    model = "fixture-model",
                    timestamp = timestamp.ToUnixTimeMilliseconds(),
                    usage = new
                    {
                        input = 10,
                        output = 5,
                        cacheWrite = 1,
                        cacheRead = 2,
                        cost = new { total = 1.25 },
                    },
                },
            }));
            File.WriteAllLines(Path.Combine(_root, "session.jsonl"), lines);
        }

        private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
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
                [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string? vfs);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_exec(
                IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
                IntPtr callback, IntPtr argument, out IntPtr error);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void sqlite3_free(IntPtr value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close_v2(IntPtr database);
        }
    }
}
