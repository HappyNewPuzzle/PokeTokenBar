using System.Text.Json;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexRateLimitsTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-RateLimits-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("codex.exe", CodexExecutableKind.Direct)]
    [InlineData("codex.cmd", CodexExecutableKind.CommandScript)]
    [InlineData("codex.ps1", CodexExecutableKind.PowerShellScript)]
    public void ExecutableResolver_FindsSupportedWindowsLaunchForms(
        string fileName,
        CodexExecutableKind expectedKind)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        var resolver = IsolatedResolver(_temporaryDirectory);

        var result = resolver.Resolve();

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(path), result.Path);
        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public void RequestLines_MatchSwiftInitializeHandshakeAndReadOrder()
    {
        var lines = CodexRateLimitsProvider.CreateRequestLines("1.2.3");

        Assert.Equal(3, lines.Count);
        using var initialize = JsonDocument.Parse(lines[0]);
        using var initialized = JsonDocument.Parse(lines[1]);
        using var read = JsonDocument.Parse(lines[2]);
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        Assert.Equal(0, initialize.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(
            "token_mac",
            initialize.RootElement.GetProperty("params").GetProperty("clientInfo")
                .GetProperty("name").GetString());
        Assert.True(
            initialize.RootElement.GetProperty("params").GetProperty("capabilities")
                .GetProperty("experimentalApi").GetBoolean());
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
        Assert.Equal("account/rateLimits/read", read.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, read.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void ResponseParser_MapsFiveHourWeeklyResetAndMultipleBuckets()
    {
        var status = ParseStatus("""
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": { "usedPercent": 86, "windowDurationMins": 300, "resetsAt": 1781694161 },
                "secondary": { "usedPercent": 58, "windowDurationMins": 10080, "resetsAt": 1781855658 },
                "credits": { "hasCredits": false, "unlimited": false, "balance": null },
                "planType": "team"
              },
              "rateLimitsByLimitId": {
                "codex": { "limitId": "codex" },
                "codex_other": {
                  "limitId": "codex_other",
                  "primary": { "usedPercent": 41, "windowDurationMins": 300 }
                }
              }
            }
            """);

        Assert.Equal(86, status.RateLimits.Primary?.UsedPercent);
        Assert.Equal(300, status.RateLimits.Primary?.WindowDurationMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1781694161), status.RateLimits.Primary?.ResetsAt);
        Assert.Equal(58, status.RateLimits.Secondary?.UsedPercent);
        Assert.Equal(10_080, status.RateLimits.Secondary?.WindowDurationMinutes);
        Assert.Equal(2, status.Snapshots.Count);
        Assert.Equal("codex_other", status.Snapshots[1].LimitId);
        Assert.Equal(86, status.MaxPrimaryUsedPercent);
    }

    [Fact]
    public void ResponseParser_RejectsMalformedOrIncompleteResult()
    {
        using var malformedWindow = JsonDocument.Parse(
            """{"rateLimits":{"primary":{"usedPercent":"86"}}}""");
        using var missingRoot = JsonDocument.Parse("""{"other":{}}""");

        Assert.Throws<JsonException>(() =>
            CodexRateLimitJsonParser.Parse(malformedWindow.RootElement));
        Assert.Throws<JsonException>(() =>
            CodexRateLimitJsonParser.Parse(missingRoot.RootElement));
    }

    [Fact]
    public void JsonRpcReader_IgnoresLogsMalformedLinesAndOtherResponseIds()
    {
        Assert.False(CodexAppServerProcess.TryReadResponse(
            "server started", 1, out _, out _));
        Assert.False(CodexAppServerProcess.TryReadResponse(
            "{broken", 1, out _, out _));
        Assert.False(CodexAppServerProcess.TryReadResponse(
            "{\"id\":0,\"result\":{}}", 1, out _, out _));

        Assert.True(CodexAppServerProcess.TryReadResponse(
            "{\"id\":1,\"result\":{\"rateLimits\":{}}}",
            1,
            out var result,
            out var error));
        Assert.Null(error);
        Assert.Equal(JsonValueKind.Object, result?.ValueKind);
    }

    [Fact]
    public void JsonRpcReader_ReportsMatchingErrorResponse()
    {
        var matched = CodexAppServerProcess.TryReadResponse(
            "{\"id\":1,\"error\":{\"code\":-32000,\"message\":\"not signed in\"}}",
            1,
            out var result,
            out var error);

        Assert.True(matched);
        Assert.Null(result);
        Assert.Equal("not signed in", error);
    }

    [Fact]
    public async Task Process_PropagatesNonZeroExitAndCancellation()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var failingPath = Path.Combine(_temporaryDirectory, "fail.cmd");
        File.WriteAllText(failingPath, "@echo failure 1>&2\r\n@exit /b 7\r\n");
        var process = new CodexAppServerProcess(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<CodexAppServerException>(() =>
            process.SendAsync(
                new CodexExecutable(failingPath, CodexExecutableKind.CommandScript),
                Array.Empty<string>(),
                1));
        Assert.Contains("code 7", failure.Message, StringComparison.Ordinal);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            process.SendAsync(
                new CodexExecutable(failingPath, CodexExecutableKind.CommandScript),
                Array.Empty<string>(),
                1,
                cancellation.Token));
    }

    [Fact]
    public async Task Provider_ReturnsNullWithoutExecutableAndMapsAppServerResult()
    {
        var missingProcess = new FakeProcess(ParseElement("{}"));
        var missing = new CodexRateLimitsProvider(
            IsolatedResolver(Path.Combine(_temporaryDirectory, "missing")),
            missingProcess);
        Assert.Null(await missing.FetchAsync());
        Assert.Equal(0, missingProcess.Calls);

        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "codex.cmd"), string.Empty);
        var process = new FakeProcess(ParseElement("""
            {"rateLimits":{"primary":{"usedPercent":82,"windowDurationMins":300}}}
            """));
        var provider = new CodexRateLimitsProvider(
            IsolatedResolver(_temporaryDirectory),
            process,
            "9.8.7");

        var status = await provider.FetchAsync();

        Assert.Equal(82, status?.RateLimits.Primary?.UsedPercent);
        Assert.Equal(1, process.Calls);
        Assert.Equal(1, process.ResponseId);
        Assert.Contains("account/rateLimits/read", process.InputLines![2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Store_MergesSuccessfulLimitsWithoutChangingLocalUsage()
    {
        var local = new FakeUsageProvider(Daily(130));
        var official = new FakeRateLimitsProvider(Status(82, 61, Now.AddHours(2)));
        var store = new UsageStore(
            [local],
            new FixedTimeProvider(Now),
            official);

        await store.RefreshAsync();

        Assert.Equal(130, store.TodayTotalTokens);
        Assert.Equal(82, store.CodexRateLimits?.RateLimits.Primary?.UsedPercent);
        Assert.Equal(Now, store.CodexRateLimitsUpdatedAt);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task Store_RateLimitFailurePreservesPreviousLimitsAndLocalRefresh()
    {
        var local = new FakeUsageProvider(Daily(130));
        var official = new FakeRateLimitsProvider(Status(82, 61, Now.AddHours(2)));
        var store = new UsageStore([local], codexRateLimitsProvider: official);
        await store.RefreshAsync();

        local.Daily = Daily(260);
        official.Error = new InvalidOperationException("app-server unavailable");
        await store.RefreshAsync();

        Assert.Equal(260, store.TodayTotalTokens);
        Assert.Equal(82, store.CodexRateLimits?.RateLimits.Primary?.UsedPercent);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task Store_SuccessfulUnavailableResultClearsPreviousLimitsLikeSwift()
    {
        var official = new FakeRateLimitsProvider(Status(82, 61, Now.AddHours(2)));
        var store = new UsageStore(
            [new FakeUsageProvider(Daily(130))],
            codexRateLimitsProvider: official);
        await store.RefreshAsync();

        official.Value = null;
        await store.RefreshAsync();

        Assert.Null(store.CodexRateLimits);
        Assert.Equal(130, store.TodayTotalTokens);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task ViewModel_MapsOfficialLimitsAndHidesUnavailableData()
    {
        var official = new FakeRateLimitsProvider(
            Status(0, 14, Now.AddHours(1).AddMinutes(24)));
        var store = new UsageStore(
            [new FakeUsageProvider(Daily(130))],
            new FixedTimeProvider(Now),
            official);
        var viewModel = new UsageViewModel(store, timeProvider: new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasCodexRateLimits);
        Assert.Equal(100, viewModel.FiveHourRemainingPercent);
        Assert.Equal("100% remaining", viewModel.FiveHourRemainingText);
        Assert.Equal("Resets in 1h 24m", viewModel.FiveHourResetText);
        Assert.Equal(86, viewModel.WeeklyRemainingPercent);
        Assert.Equal("86% remaining", viewModel.WeeklyRemainingText);

        var unavailable = new UsageViewModel(
            new UsageStore([new FakeUsageProvider(Daily(1))]),
            timeProvider: new FixedTimeProvider(Now));
        await unavailable.RefreshAsync();
        Assert.False(unavailable.HasCodexRateLimits);
        Assert.Null(unavailable.FiveHourRemainingText);
        Assert.Null(unavailable.WeeklyRemainingText);
    }

    [Fact]
    public async Task ViewModel_ShowsOfficialLimitsWhenLocalUsageIsEmpty()
    {
        var official = new FakeRateLimitsProvider(
            Status(82, 61, Now.AddHours(1)));
        var store = new UsageStore(
            [new FakeUsageProvider(daily: null)],
            new FixedTimeProvider(Now),
            official);
        var viewModel = new UsageViewModel(store, timeProvider: new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.Equal("codex", viewModel.SelectedProviderId);
        Assert.Null(viewModel.TodayTokens);
        Assert.True(viewModel.HasCodexRateLimits);
        Assert.Equal("18% remaining", viewModel.FiveHourRemainingText);
        Assert.Equal("39% remaining", viewModel.WeeklyRemainingText);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(-10, 100)]
    [InlineData(110, 0)]
    public async Task ViewModel_ClampsRemainingPercentage(int used, int expected)
    {
        var store = new UsageStore(
            [new FakeUsageProvider(Daily(1))],
            new FixedTimeProvider(Now),
            new FakeRateLimitsProvider(Status(used, used, Now.AddHours(1))));
        var viewModel = new UsageViewModel(store, timeProvider: new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.Equal(expected, viewModel.FiveHourRemainingPercent);
        Assert.Equal(expected, viewModel.WeeklyRemainingPercent);
    }

    private CodexExecutableResolver IsolatedResolver(string baseDirectory) =>
        new(
            path: Path.Combine(_temporaryDirectory, "no-path"),
            userProfile: Path.Combine(_temporaryDirectory, "no-profile"),
            appData: Path.Combine(_temporaryDirectory, "no-appdata"),
            baseDirectory: baseDirectory);

    private static CodexRateLimitStatus ParseStatus(string json) =>
        CodexRateLimitJsonParser.Parse(ParseElement(json));

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static CodexRateLimitStatus Status(
        int primary,
        int secondary,
        DateTimeOffset reset) =>
        new(new CodexRateLimitSnapshot(
            "codex",
            null,
            new CodexRateLimitWindow(primary, 300, reset),
            new CodexRateLimitWindow(secondary, 10_080, reset.AddDays(5)),
            null,
            null,
            "plus",
            null));

    private static DailyUsage Daily(long total) =>
        new("2026-08-30", total, 0, 0, 0, total, 0);

    private sealed class FakeProcess(JsonElement result) : ICodexAppServerProcess
    {
        public int Calls { get; private set; }
        public int ResponseId { get; private set; }
        public IReadOnlyList<string>? InputLines { get; private set; }

        public Task<JsonElement> SendAsync(
            CodexExecutable executable,
            IReadOnlyList<string> inputLines,
            int responseId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ResponseId = responseId;
            InputLines = inputLines;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeRateLimitsProvider(CodexRateLimitStatus? value)
        : ICodexRateLimitsProvider
    {
        public CodexRateLimitStatus? Value { get; set; } = value;
        public Exception? Error { get; set; }

        public Task<CodexRateLimitStatus?> FetchAsync(
            CancellationToken cancellationToken = default) =>
            Error is null
                ? Task.FromResult(Value)
                : Task.FromException<CodexRateLimitStatus?>(Error);
    }

    private sealed class FakeUsageProvider(DailyUsage? daily) : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => false;
        public DailyUsage? Daily { get; set; } = daily;

        public Task<DailyUsage?> FetchDailyAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Daily);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
