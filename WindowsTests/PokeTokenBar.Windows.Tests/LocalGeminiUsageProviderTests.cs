using System.Text.Json;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalGeminiUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Gemini-{Guid.NewGuid():N}");

    public LocalGeminiUsageProviderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MetadataMatchesMacOSProvider()
    {
        var provider = new LocalGeminiUsageProvider([]);

        Assert.Equal("gemini", provider.Id);
        Assert.Equal("Gemini", provider.DisplayName);
        Assert.True(provider.ReportsCost);
    }

    [Fact]
    public void DefaultRootUsesWindowsUserProfile()
    {
        var profile = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester");

        Assert.Equal(
            [Path.Combine(profile, ".gemini", "tmp")],
            LocalGeminiUsageProvider.GetDefaultRoots(profile));
    }

    [Fact]
    public async Task MissingDirectoryIsUnavailableWithoutFailure()
    {
        var provider = new LocalGeminiUsageProvider([Path.Combine(_directory, "missing")]);

        Assert.Null(await Daily(provider));
        Assert.True((await Enrichment(provider)).PeriodsOK);
    }

    [Fact]
    public async Task JsonLinesMapsTokenFieldsAndLastUpdateWins()
    {
        WriteJsonLines(
            Metadata("2026-08-30T10:00:00Z"),
            Record("m1", "2026-08-30T10:01:00Z", "gemini-2.5-pro", 1000, 50, 600, 30, 20),
            Record("m2", "2026-08-30T10:02:00Z", "gemini-2.5-flash", 10, 5, 0, 0, 0),
            Update("m2", 10, 8, 0, 2, 0));

        var daily = await Daily();

        Assert.NotNull(daily);
        Assert.Equal(430, daily.InputTokens);
        Assert.Equal(90, daily.OutputTokens);
        Assert.Equal(600, daily.CacheReadTokens);
        Assert.Equal(0, daily.CacheCreationTokens);
        Assert.Equal(1120, daily.TotalTokens);
    }

    [Fact]
    public async Task LegacyJsonMessagesUseSessionTimestampFallback()
    {
        WriteLegacy(
            "2026-08-30T09:00:00Z",
            RecordElement("a1", null, "gemini-2.5-pro", 100, 10, 0, 0, 0));

        Assert.Equal(110, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task MultipleNestedSessionsAndExtensionsAreCombined()
    {
        WriteJsonLines(Record("a", "2026-08-30T09:00:00Z", "gemini-2.5-pro", 10, 0, 0, 0, 0));
        WriteLegacy(
            "2026-08-30T10:00:00Z",
            [RecordElement("b", null, "gemini-2.5-pro", 20, 0, 0, 0, 0)],
            "other/checkpoint.json");

        Assert.Equal(30, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task MalformedAndUnsupportedRowsDoNotHideValidUsage()
    {
        WriteJsonLines(
            "not-json",
            "{\"type\":\"user\",\"content\":[]}",
            "{\"type\":\"gemini\",\"tokens\":{}}",
            Record("ok", "2026-08-30T11:00:00Z", "gemini-2.5-pro", 7, 3, 0, 0, 0));

        Assert.Equal(10, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task MissingNegativeAndAbsurdValuesFollowMacOSSafetySemantics()
    {
        WriteJsonLines("""
            {"id":"safe","timestamp":"2026-08-30T11:00:00Z","model":"gemini","tokens":{"input":1e30,"output":-2,"cached":null,"thoughts":"bad","tool":1e30}}
            """);

        var daily = await Daily();

        Assert.Equal(2_000_000_000_000_000, daily!.InputTokens);
        Assert.Equal(0, daily.OutputTokens);
    }

    [Fact]
    public async Task SameTurnCopiedAcrossRootsKeepsLargestRecord()
    {
        var other = Path.Combine(_directory, "other");
        WriteJsonLines(Record("same", "2026-08-30T10:00:00Z", "gemini-2.5-pro", 10, 0, 0, 0, 0));
        WriteJsonLines(
            [Record("same", "2026-08-30T10:00:01Z", "gemini-2.5-pro", 25, 0, 0, 0, 0)],
            other);
        var provider = new LocalGeminiUsageProvider([_directory, other]);

        Assert.Equal(25, (await Daily(provider))!.TotalTokens);
    }

    [Theory]
    [InlineData("gemini-2.5-pro", 1.25)]
    [InlineData("gemini-2.5-flash", 0.30)]
    [InlineData("gemini-2.0-flash", 0.10)]
    [InlineData("gemini-3.1-pro-preview", 1.25)]
    [InlineData("gemini-3-flash-lite", 0.30)]
    [InlineData("gemini-nano-banana", 0)]
    public void PricingMatchesMacOSTableAndFamilyFallback(string model, double expected) =>
        Assert.Equal(
            expected,
            LocalGeminiUsageProvider.CalculateCost(model, 1_000_000, 0, 0, 0),
            precision: 6);

    [Fact]
    public void CacheReadAndOutputPricingMatchMacOSTable()
    {
        var cost = LocalGeminiUsageProvider.CalculateCost(
            "gemini-2.5-pro",
            420,
            80,
            0,
            600);

        Assert.Equal(
            420 * 1.25e-6 + 80 * 10e-6 + 600 * 0.3125e-6,
            cost,
            precision: 12);
    }

    [Fact]
    public async Task TodayFiveHourWeekAndMonthUseLocalCalendarWindows()
    {
        WriteJsonLines(
            Record("month", "2026-08-01T01:00:00Z", "gemini-2.5-pro", 10, 0, 0, 0, 0),
            Record("week", "2026-08-24T01:00:00Z", "gemini-2.5-pro", 20, 0, 0, 0, 0),
            Record("today", "2026-08-30T01:00:00Z", "gemini-2.5-pro", 30, 0, 0, 0, 0),
            Record("recent", "2026-08-30T10:00:00Z", "gemini-2.5-pro", 40, 0, 0, 0, 0));

        var daily = await Daily();
        var enrichment = await Enrichment();

        Assert.Equal(70, daily!.TotalTokens);
        Assert.Equal(40, enrichment.ActiveBlock!.TotalTokens);
        Assert.Equal(90, enrichment.WeekTotal!.TotalTokens);
        Assert.Equal(100, enrichment.MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task ZeroTodayWithMonthUsageReturnsCarrierPeriodsAndCost()
    {
        WriteJsonLines(Record(
            "month",
            "2026-08-10T10:00:00Z",
            "gemini-2.5-pro",
            1_000_000,
            0,
            0,
            0,
            0));

        Assert.Null(await Daily());
        var enrichment = await Enrichment();
        Assert.Equal(1_000_000, enrichment.MonthTotal!.TotalTokens);
        Assert.Equal(1.25, enrichment.MonthTotal.TotalCost, precision: 6);
    }

    [Fact]
    public async Task CancellationStopsFilesystemScan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalGeminiUsageProvider([_directory]).FetchDailyAsync(
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
        LocalGeminiUsageProvider? provider = null) =>
        (provider ?? new LocalGeminiUsageProvider([_directory])).FetchDailyAsync(
            Now,
            Utc,
            DayOfWeek.Monday);

    private Task<PokeTokenBar.Windows.Core.ProviderEnrichment> Enrichment(
        LocalGeminiUsageProvider? provider = null) =>
        (provider ?? new LocalGeminiUsageProvider([_directory])).FetchEnrichmentAsync(
            Now,
            Utc,
            DayOfWeek.Monday);

    private void WriteJsonLines(params string[] lines) => WriteJsonLines(lines, _directory);

    private static void WriteJsonLines(string[] lines, string root)
    {
        var path = Path.Combine(root, "hash", "chats", "session.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    private void WriteLegacy(string startTime, JsonElement message, string relative = "hash/chats/checkpoint.json") =>
        WriteLegacy(startTime, [message], relative);

    private void WriteLegacy(string startTime, JsonElement[] messages, string relative)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { startTime, messages }));
    }

    private static string Metadata(string timestamp) =>
        JsonSerializer.Serialize(new { type = "session_metadata", timestamp });

    private static string Record(
        string id,
        string? timestamp,
        string model,
        long input,
        long output,
        long cached,
        long thoughts,
        long tool) =>
        JsonSerializer.Serialize(new
        {
            type = "gemini",
            id,
            timestamp,
            model,
            tokens = new { input, output, cached, thoughts, tool },
        });

    private static JsonElement RecordElement(
        string id,
        string? timestamp,
        string model,
        long input,
        long output,
        long cached,
        long thoughts,
        long tool) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "gemini",
            id,
            timestamp,
            model,
            tokens = new { input, output, cached, thoughts, tool },
        });

    private static string Update(
        string id,
        long input,
        long output,
        long cached,
        long thoughts,
        long tool) =>
        JsonSerializer.Serialize(new
        {
            type = "message_update",
            id,
            tokens = new { input, output, cached, thoughts, tool },
        });
}
