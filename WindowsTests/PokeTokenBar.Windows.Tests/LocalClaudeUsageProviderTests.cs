using PokeTokenBar.Windows.Infrastructure;
using System.Text.Json;

namespace PokeTokenBar.Windows.Tests;

public sealed class LocalClaudeUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Claude-{Guid.NewGuid():N}");

    public LocalClaudeUsageProviderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MetadataMatchesMacOSProvider()
    {
        var provider = new LocalClaudeUsageProvider([]);

        Assert.Equal("claude_code", provider.Id);
        Assert.Equal("Claude Code", provider.DisplayName);
        Assert.True(provider.ReportsCost);
    }

    [Fact]
    public async Task NormalAssistantTurnsAggregateAllTokenBuckets()
    {
        Write(
            Line("m1", "r1", "claude-opus-4-8", "2026-08-30T10:00:00Z", 100, 20, 30, 40),
            Line("m2", "r2", "claude-sonnet-4-6", "2026-08-30T11:00:00Z", 10, 5, 3, 2));

        var daily = await Daily();

        Assert.NotNull(daily);
        Assert.Equal(110, daily.InputTokens);
        Assert.Equal(25, daily.OutputTokens);
        Assert.Equal(33, daily.CacheCreationTokens);
        Assert.Equal(42, daily.CacheReadTokens);
        Assert.Equal(210, daily.TotalTokens);
    }

    [Fact]
    public async Task MultipleFilesAndNestedSessionsAreCombined()
    {
        Write(Line("m1", "r1", "claude-opus-4-8", "2026-08-30T10:00:00Z", 10, 0, 0, 0));
        WriteTo("project/nested/b.jsonl", Line("m2", "r2", "claude-opus-4-8", "2026-08-30T11:00:00Z", 20, 0, 0, 0));

        Assert.Equal(30, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task DuplicateMessageAndRequestKeepLargestStreamingRecordAcrossFiles()
    {
        Write(Line("m1", "r1", "claude-opus-4-8", "2026-08-30T10:00:00Z", 100, 5, 0, 50));
        WriteTo("other/b.jsonl", Line("m1", "r1", "claude-opus-4-8", "2026-08-30T10:00:01Z", 100, 25, 0, 50));

        var daily = await Daily();

        Assert.Equal(175, daily!.TotalTokens);
        Assert.Equal(25, daily.OutputTokens);
    }

    [Fact]
    public async Task MalformedMissingAndNonAssistantRowsAreIgnored()
    {
        Write(
            "not-json",
            "{\"type\":\"user\",\"usage\":{}}",
            "{\"type\":\"assistant\",\"message\":{\"usage\":{}}}",
            Line("m1", "r1", "claude-opus-4-8", "2026-08-30T10:00:00Z", 7, 3, 0, 0));

        Assert.Equal(10, (await Daily())!.TotalTokens);
    }

    [Fact]
    public async Task MissingAndNegativeTokenFieldsFoldToZero()
    {
        Write("""
            {"type":"assistant","requestId":"r","timestamp":"2026-08-30T10:00:00Z","message":{"id":"m","model":"unknown","usage":{"input_tokens":-5,"output_tokens":null,"cache_read_input_tokens":"bad"}}}
            """);

        Assert.Null(await Daily());
    }

    [Fact]
    public async Task AbsurdTokenValuesAreClampedInsteadOfCrashing()
    {
        Write("""
            {"type":"assistant","requestId":"r","timestamp":"2026-08-30T10:00:00Z","message":{"id":"m","model":"unknown","usage":{"input_tokens":1e30,"output_tokens":0}}}
            """);

        Assert.Equal(1_000_000_000_000_000, (await Daily())!.InputTokens);
    }

    [Theory]
    [InlineData("claude-opus-4-8", 5)]
    [InlineData("claude-opus-future", 5)]
    [InlineData("claude-sonnet-4-6", 3)]
    [InlineData("claude-haiku-4-5-20251001", 1)]
    [InlineData("claude-fable-5", 10)]
    [InlineData("unknown", 0)]
    public void ModelPricingMatchesMacOSTableAndFamilyFallback(string model, double expected) =>
        Assert.Equal(
            expected,
            LocalClaudeUsageProvider.CalculateCost(model, 1_000_000, 0, 0, 0),
            precision: 6);

    [Fact]
    public void CachePricingMatchesMacOSTable()
    {
        var cost = LocalClaudeUsageProvider.CalculateCost(
            "claude-opus-4-8",
            0,
            0,
            1_000_000,
            1_000_000);

        Assert.Equal(6.75, cost, precision: 6);
    }

    [Fact]
    public async Task TodayFiveHourWeekAndMonthUseLocalCalendarWindows()
    {
        Write(
            Line("old", "r0", "claude-opus-4-8", "2026-08-01T01:00:00Z", 10, 0, 0, 0),
            Line("week", "r1", "claude-opus-4-8", "2026-08-24T01:00:00Z", 20, 0, 0, 0),
            Line("today-old", "r2", "claude-opus-4-8", "2026-08-30T01:00:00Z", 30, 0, 0, 0),
            Line("recent", "r3", "claude-opus-4-8", "2026-08-30T10:00:00Z", 40, 0, 0, 0));

        var daily = await Daily();
        var enrichment = await Enrichment();

        Assert.Equal(70, daily!.TotalTokens);
        Assert.Equal(40, enrichment.ActiveBlock!.TotalTokens);
        Assert.Equal(90, enrichment.WeekTotal!.TotalTokens);
        Assert.Equal(100, enrichment.MonthTotal!.TotalTokens);
    }

    [Fact]
    public async Task ZeroTodayWithMonthUsageStillReturnsPeriodEnrichment()
    {
        Write(Line("month", "r1", "claude-opus-4-8", "2026-08-10T10:00:00Z", 1_000_000, 0, 0, 0));

        Assert.Null(await Daily());
        var enrichment = await Enrichment();
        Assert.Equal(1_000_000, enrichment.MonthTotal!.TotalTokens);
        Assert.Equal(5, enrichment.MonthTotal.TotalCost, precision: 6);
    }

    [Fact]
    public async Task MissingAndInvalidRootsAreUnavailableWithoutFailure()
    {
        var provider = new LocalClaudeUsageProvider(
            [Path.Combine(_directory, "missing"), "\0"]);

        Assert.Null(await provider.FetchDailyAsync(Now, Utc, DayOfWeek.Monday));
        Assert.True((await provider.FetchEnrichmentAsync(Now, Utc, DayOfWeek.Monday)).PeriodsOK);
    }

    [Fact]
    public void DefaultWindowsRootsIncludeProfileAndClaudeConfigDirectories()
    {
        var profile = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester");
        var roots = LocalClaudeUsageProvider.GetDefaultRoots(
            profile,
            $"{Path.Combine(profile, "custom")}, ~/second");

        Assert.Contains(Path.Combine(profile, "custom", "projects"), roots);
        Assert.Contains(Path.Combine(profile, "second", "projects"), roots);
        Assert.Contains(Path.Combine(profile, ".config", "claude", "projects"), roots);
        Assert.Contains(Path.Combine(profile, ".claude", "projects"), roots);
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task CancellationStopsFilesystemScan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalClaudeUsageProvider([_directory]).FetchDailyAsync(
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

    private Task<PokeTokenBar.Windows.Core.DailyUsage?> Daily() =>
        new LocalClaudeUsageProvider([_directory]).FetchDailyAsync(Now, Utc, DayOfWeek.Monday);

    private Task<PokeTokenBar.Windows.Core.ProviderEnrichment> Enrichment() =>
        new LocalClaudeUsageProvider([_directory]).FetchEnrichmentAsync(Now, Utc, DayOfWeek.Monday);

    private void Write(params string[] lines) => WriteTo("project/session.jsonl", lines);

    private void WriteTo(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    private static string Line(
        string id,
        string request,
        string model,
        string timestamp,
        long input,
        long output,
        long cacheWrite,
        long cacheRead) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            requestId = request,
            timestamp,
            message = new
            {
                id,
                model,
                usage = new
                {
                    input_tokens = input,
                    output_tokens = output,
                    cache_creation_input_tokens = cacheWrite,
                    cache_read_input_tokens = cacheRead,
                },
            },
        });
}
