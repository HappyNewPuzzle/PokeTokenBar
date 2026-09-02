using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase7AProviderUxTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
    private readonly string _temp = Path.Combine(
        Path.GetTempPath(), $"PokeTokenBar-Phase7A-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(LimitDisplayMode.Used, 14, "14% used")]
    [InlineData(LimitDisplayMode.Remaining, 86, "86% remaining")]
    public async Task MultiBucketRowsUseConfiguredDisplayMode(
        LimitDisplayMode mode, int expectedPercent, string expectedText)
    {
        var status = MultiBucketStatus(14);
        var viewModel = await CodexViewModel(status);

        viewModel.SetLimitDisplayMode(mode);

        Assert.Equal(4, viewModel.OfficialLimitRows.Count);
        Assert.Equal(expectedPercent, viewModel.OfficialLimitRows[0].RemainingPercent);
        Assert.Equal(expectedText, viewModel.OfficialLimitRows[0].RemainingText);
        Assert.Collection(viewModel.OfficialLimitRows,
            row => Assert.Equal("Codex · 5-hour session", row.Label),
            row => Assert.Equal("Codex · Weekly", row.Label),
            row => Assert.Equal("Codex other · 5-hour session", row.Label),
            row => Assert.Equal("Codex other · Weekly", row.Label));
    }

    [Theory]
    [InlineData(300, "5-hour session")]
    [InlineData(10080, "Weekly")]
    [InlineData(120, "2-hour")]
    [InlineData(45, "45-minute")]
    public async Task CodexWindowDurationProducesStableLabel(int minutes, string expected)
    {
        var status = new CodexRateLimitStatus(new CodexRateLimitSnapshot(
            "codex", null, new CodexRateLimitWindow(10, minutes, Now.AddHours(1)),
            null, null, null, null, null));

        var viewModel = await CodexViewModel(status);

        Assert.Equal(expected, Assert.Single(viewModel.OfficialLimitRows).Label);
    }

    [Theory]
    [InlineData("12.50", false, "Credits: 12.50")]
    [InlineData(null, true, "Credits: ∞")]
    [InlineData(null, false, null)]
    public async Task CreditsNeverInventZero(string? balance, bool unlimited, string? expected)
    {
        var status = new CodexRateLimitStatus(new CodexRateLimitSnapshot(
            "codex", null, new CodexRateLimitWindow(10, 300, Now.AddHours(1)), null,
            new CodexCreditsSnapshot(balance, balance is not null || unlimited, unlimited),
            null, null, null));

        var viewModel = await CodexViewModel(status);

        Assert.Equal(expected, viewModel.CreditsText);
        Assert.Equal(expected is not null, viewModel.HasCredits);
    }

    [Fact]
    public async Task PersonalSpendIsMoneyDetailNotTokenWallet()
    {
        var status = new CodexRateLimitStatus(new CodexRateLimitSnapshot(
            "codex", null, null, null, null,
            new CodexSpendControlLimit("$100", 86, Now.AddDays(1), "$14"),
            "plus", null));

        var viewModel = await CodexViewModel(status);

        var row = Assert.Single(viewModel.OfficialLimitRows);
        Assert.Equal("Personal spend limit", row.Label);
        Assert.Equal("$14 / $100", row.DetailText);
        Assert.Equal(86, row.RemainingPercent);
        Assert.Equal("86% remaining", row.RemainingText);
    }

    [Theory]
    [InlineData(0, ProviderRuntimeStatus.NoSessions)]
    [InlineData(1, ProviderRuntimeStatus.Ready)]
    [InlineData(2, ProviderRuntimeStatus.Error)]
    public async Task RuntimeStatusUsesRefreshOutcome(int mode, ProviderRuntimeStatus expected)
    {
        var provider = new FakeUsage("local")
        {
            Daily = mode == 1 ? Daily(1) : null,
            Error = mode == 2 ? new IOException("fixture") : null,
        };
        var store = new UsageStore([provider], new FixedTimeProvider(Now));

        await store.RefreshAsync();

        var status = Assert.Single(store.ProviderStatuses);
        Assert.Equal(expected, status.RuntimeStatus);
        Assert.Equal(ProviderAuthStatus.NotApplicable, status.AuthStatus);
    }

    [Fact]
    public async Task FailedRefreshWithPreviousDataIsStale()
    {
        var provider = new FakeUsage("local") { Daily = Daily(1) };
        var store = new UsageStore([provider], new FixedTimeProvider(Now));
        await store.RefreshAsync();
        provider.Error = new IOException("fixture");

        await store.RefreshAsync();

        Assert.Equal(ProviderRuntimeStatus.Stale, Assert.Single(store.ProviderStatuses).RuntimeStatus);
        Assert.Single(store.Snapshots);
    }

    [Theory]
    [InlineData(true, true, ProviderRuntimeStatus.Ready, ProviderAuthStatus.Authenticated)]
    [InlineData(true, false, ProviderRuntimeStatus.LocalDataOnly, ProviderAuthStatus.QuotaUnavailable)]
    [InlineData(false, false, ProviderRuntimeStatus.NoSessions, ProviderAuthStatus.QuotaUnavailable)]
    [InlineData(false, true, ProviderRuntimeStatus.Ready, ProviderAuthStatus.Authenticated)]
    public async Task CodexLocalAndOfficialAvailabilityStayIndependent(
        bool local, bool official,
        ProviderRuntimeStatus runtime, ProviderAuthStatus auth)
    {
        var store = new UsageStore(
            [new FakeUsage("codex") { Daily = local ? Daily(1) : null }],
            new FixedTimeProvider(Now),
            new FakeCodexLimits { Value = official ? SimpleCodexStatus(10) : null });

        await store.RefreshAsync();

        var status = Assert.Single(store.ProviderStatuses);
        Assert.Equal(runtime, status.RuntimeStatus);
        Assert.Equal(auth, status.AuthStatus);
    }

    [Fact]
    public async Task ProviderFailureDoesNotEraseSiblingStatus()
    {
        var store = new UsageStore(
            [
                new FakeUsage("broken") { Error = new IOException("fixture") },
                new FakeUsage("healthy") { Daily = Daily(2) },
            ],
            new FixedTimeProvider(Now));

        await store.RefreshAsync();

        Assert.Equal(ProviderRuntimeStatus.Error,
            store.ProviderStatuses.Single(status => status.ProviderId == "broken").RuntimeStatus);
        Assert.Equal(ProviderRuntimeStatus.Ready,
            store.ProviderStatuses.Single(status => status.ProviderId == "healthy").RuntimeStatus);
    }

    [Fact]
    public async Task OfficialFailurePreservesLimitsAndMarksProviderStale()
    {
        var limits = new FakeCodexLimits { Value = SimpleCodexStatus(10) };
        var store = new UsageStore(
            [new FakeUsage("codex") { Daily = Daily(1) }],
            new FixedTimeProvider(Now), limits);
        await store.RefreshAsync();
        limits.Error = new IOException("fixture");

        await store.RefreshAsync();

        Assert.NotNull(store.CodexRateLimits);
        Assert.Equal(ProviderRuntimeStatus.Stale, Assert.Single(store.ProviderStatuses).RuntimeStatus);
    }

    [Fact]
    public async Task FirstOfficialFailureKeepsFreshLocalDataAsLocalOnly()
    {
        var store = new UsageStore(
            [new FakeUsage("codex") { Daily = Daily(1) }],
            new FixedTimeProvider(Now),
            new FakeCodexLimits { Error = new IOException("fixture") });

        await store.RefreshAsync();

        var status = Assert.Single(store.ProviderStatuses);
        Assert.Equal(ProviderRuntimeStatus.LocalDataOnly, status.RuntimeStatus);
        Assert.Equal(ProviderAuthStatus.QuotaUnavailable, status.AuthStatus);
    }

    [Fact]
    public void ForecastMatchesUpstreamFormula()
    {
        var result = UsageStore.ForecastDepletion(
            23_000_000, 1_000_000, 23, Now);

        Assert.Equal(Now.AddMinutes(77), result);
    }

    [Theory]
    [InlineData(1_000_000, 1_000_000, 3)]
    [InlineData(1_000_000, 1_000_000, 100)]
    [InlineData(0, 1_000_000, 23)]
    [InlineData(1_000_000, 0, 23)]
    [InlineData(1_000_000, 9_999, 23)]
    [InlineData(100_000_000, 10_000, 10)]
    [InlineData(1_000_000, double.NaN, 23)]
    [InlineData(1_000_000, double.PositiveInfinity, 23)]
    public void ForecastRejectsUnsafeInputs(long tokens, double burn, double used)
    {
        Assert.Null(UsageStore.ForecastDepletion(tokens, burn, used, Now));
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(60, false)]
    public async Task ForecastDistinguishesExhaustionBeforeReset(int resetMinutes, bool projected)
    {
        var viewModel = await ClaudeViewModel(
            23_000_000, 1_000_000, 23, Now.AddMinutes(resetMinutes));

        Assert.Equal(projected, !viewModel.ForecastText!.Contains("No projection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZeroBurnShowsNoProjectionWithoutDivision()
    {
        var viewModel = await ClaudeViewModel(23_000_000, 0, 23, Now.AddHours(2));

        Assert.Null(viewModel.BurnRateText);
        Assert.Equal("Forecast: No projection", viewModel.ForecastText);
    }

    [Fact]
    public void SettingsRowsExposeReadOnlyStatusAndCustomRootFlag()
    {
        Directory.CreateDirectory(_temp);
        var persistence = new MemorySettings(new AppSettings(
            CustomProviderRoots: new Dictionary<string, string> { ["codex"] = _temp }));
        var settings = new SettingsViewModel(persistence, new FakeAutoStart(), AppLanguage.En);

        settings.UpdateProviderStatuses([
            new ProviderStatusSnapshot("codex", "Codex", ProviderRuntimeStatus.LocalDataOnly,
                ProviderAuthStatus.QuotaUnavailable),
            new ProviderStatusSnapshot("gemini", "Gemini", ProviderRuntimeStatus.NoSessions,
                ProviderAuthStatus.NotApplicable),
        ]);

        Assert.Equal("Custom root configured", settings.ProviderStatusRows[0].RootStatusText);
        Assert.Equal("Quota unavailable", settings.ProviderStatusRows[0].AuthStatusText);
        Assert.Equal("Default folders", settings.ProviderStatusRows[1].RootStatusText);
    }

    [Theory]
    [InlineData(AppLanguage.Ko)]
    [InlineData(AppLanguage.En)]
    [InlineData(AppLanguage.Ja)]
    [InlineData(AppLanguage.Es)]
    [InlineData(AppLanguage.Fr)]
    [InlineData(AppLanguage.Pt)]
    [InlineData(AppLanguage.De)]
    public void Phase7StringsExistForEveryRuntimeLanguage(AppLanguage language)
    {
        var localization = new LocalizationService(language);

        Assert.All(new[]
        {
            localization.Ready, localization.NotInstalled, localization.AuthenticationRequired,
            localization.LocalDataOnly, localization.QuotaUnavailable, localization.Credits,
            localization.Spend, localization.BurnRate, localization.Forecast,
            localization.NoProjection,
        }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private static async Task<UsageViewModel> CodexViewModel(CodexRateLimitStatus status)
    {
        var store = new UsageStore(
            [new FakeUsage("codex") { Daily = Daily(1) }],
            new FixedTimeProvider(Now),
            new FakeCodexLimits { Value = status });
        var viewModel = new UsageViewModel(store, timeProvider: new FixedTimeProvider(Now));
        await viewModel.RefreshAsync();
        return viewModel;
    }

    private static async Task<UsageViewModel> ClaudeViewModel(
        long tokens, double burn, double used, DateTimeOffset reset)
    {
        var usage = new FakeUsage("claude_code")
        {
            Enrichment = new ProviderEnrichment(
                new BlockUsage("block", "", "", true, tokens, 0, burn), true),
        };
        var store = new UsageStore(
            [usage], new FixedTimeProvider(Now),
            claudeRateLimitsProvider: new FakeClaudeLimits
            {
                Value = new ClaudeRateLimitStatus(
                    new ClaudeRateLimitWindow(used, reset), null, null, null, null, null),
            });
        var viewModel = new UsageViewModel(store, timeProvider: new FixedTimeProvider(Now));
        await viewModel.RefreshAsync();
        return viewModel;
    }

    private static CodexRateLimitStatus MultiBucketStatus(int used)
    {
        var first = new CodexRateLimitSnapshot(
            "codex", null,
            new CodexRateLimitWindow(used, 300, Now.AddHours(1)),
            new CodexRateLimitWindow(used, 10_080, Now.AddDays(1)),
            null, null, "plus", null);
        var second = new CodexRateLimitSnapshot(
            "codex_other", "codex_other",
            new CodexRateLimitWindow(used, 300, Now.AddHours(1)),
            new CodexRateLimitWindow(used, 10_080, Now.AddDays(1)),
            null, null, "plus", null);
        return new CodexRateLimitStatus(first,
            new Dictionary<string, CodexRateLimitSnapshot>
            {
                ["codex"] = first,
                ["codex_other"] = second,
            });
    }

    private static CodexRateLimitStatus SimpleCodexStatus(int used) =>
        new(new CodexRateLimitSnapshot(
            "codex", null, new CodexRateLimitWindow(used, 300, Now.AddHours(1)),
            null, null, null, null, null));

    private static DailyUsage Daily(long tokens) =>
        new("2026-09-02", tokens, 0, 0, 0, tokens, 0);

    private sealed class FakeUsage(string id) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = id;
        public bool ReportsCost => true;
        public DailyUsage? Daily { get; set; }
        public ProviderEnrichment Enrichment { get; set; } = new();
        public Exception? Error { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Error is null ? Task.FromResult(Daily) : Task.FromException<DailyUsage?>(Error);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Enrichment);
    }

    private sealed class FakeCodexLimits : ICodexRateLimitsProvider
    {
        public CodexRateLimitStatus? Value { get; set; }
        public Exception? Error { get; set; }
        public Task<CodexRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Error is null ? Task.FromResult(Value) : Task.FromException<CodexRateLimitStatus?>(Error);
    }

    private sealed class FakeClaudeLimits : IClaudeRateLimitsProvider
    {
        public ClaudeRateLimitStatus? Value { get; set; }
        public Task<ClaudeRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class MemorySettings(AppSettings settings) : IAppSettingsPersistence
    {
        public AppSettings? Load() => settings;
        public void Save(AppSettings value) => settings = value;
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }
}
