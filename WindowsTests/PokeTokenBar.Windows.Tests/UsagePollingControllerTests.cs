using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class UsagePollingControllerTests
{
    [Fact]
    public void ManualModeCreatesNoPollingTimer()
    {
        var harness = Create(RefreshIntervalMode.Manual);
        using var controller = harness.Controller;

        controller.Start();

        Assert.Empty(harness.TimeProvider.ActiveTimers);
    }

    [Fact]
    public void DefaultModeSchedulesFirstAndFollowingRefreshAtTwoMinutes()
    {
        var harness = Create(RefreshIntervalMode.TwoMinutes);
        using var controller = harness.Controller;

        controller.Start();

        var timer = Assert.Single(harness.TimeProvider.ActiveTimers);
        Assert.Equal(TimeSpan.FromMinutes(2), timer.DueTime);
        Assert.Equal(TimeSpan.FromMinutes(2), timer.Period);
    }

    [Fact]
    public void IntervalChangePersistsAndReplacesExistingSchedule()
    {
        var harness = Create(RefreshIntervalMode.OneMinute);
        using var controller = harness.Controller;
        controller.Start();
        var original = Assert.Single(harness.TimeProvider.ActiveTimers);

        harness.Settings.SelectedRefreshInterval = RefreshIntervalMode.FifteenMinutes;

        Assert.True(original.IsDisposed);
        var replacement = Assert.Single(harness.TimeProvider.ActiveTimers);
        Assert.Equal(TimeSpan.FromMinutes(15), replacement.DueTime);
        Assert.Equal(
            RefreshIntervalMode.FifteenMinutes,
            harness.Persistence.LastSaved?.RefreshInterval);
    }

    [Fact]
    public async Task PollingTickRefreshesUsage()
    {
        var harness = Create(RefreshIntervalMode.OneMinute);
        using var controller = harness.Controller;
        controller.Start();

        Assert.Single(harness.TimeProvider.ActiveTimers).Fire();

        await WaitUntilAsync(() => harness.Provider.DailyCalls == 1);
    }

    [Fact]
    public async Task OverlappingTicksDoNotCreateRefreshStorm()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = Create(
            RefreshIntervalMode.OneMinute,
            async token =>
            {
                await release.Task.WaitAsync(token);
                return Daily(1);
            });
        using var controller = harness.Controller;
        controller.Start();
        var timer = Assert.Single(harness.TimeProvider.ActiveTimers);

        timer.Fire();
        await WaitUntilAsync(() => harness.Provider.DailyCalls == 1);
        timer.Fire();
        timer.Fire();
        release.SetResult();
        await WaitUntilAsync(() => !harness.ViewModel.IsRefreshing);

        Assert.Equal(1, harness.Provider.DailyCalls);
    }

    [Fact]
    public async Task ProviderFailureDoesNotStopFollowingPollingTicks()
    {
        var harness = Create(
            RefreshIntervalMode.OneMinute,
            _ => Task.FromException<DailyUsage?>(new IOException("scan failed")));
        using var controller = harness.Controller;
        controller.Start();
        var timer = Assert.Single(harness.TimeProvider.ActiveTimers);

        timer.Fire();
        await WaitUntilAsync(() => harness.Provider.DailyCalls == 1 && !harness.ViewModel.IsRefreshing);
        timer.Fire();
        await WaitUntilAsync(() => harness.Provider.DailyCalls == 2);

        Assert.False(timer.IsDisposed);
    }

    [Fact]
    public async Task DisposeStopsTimersAndIsIdempotent()
    {
        var harness = Create(RefreshIntervalMode.OneMinute);
        var controller = harness.Controller;
        controller.Start();
        var timer = Assert.Single(harness.TimeProvider.ActiveTimers);

        controller.Dispose();
        controller.Dispose();
        timer.Fire();
        await Task.Yield();

        Assert.True(timer.IsDisposed);
        Assert.Equal(0, harness.Provider.DailyCalls);
    }

    [Fact]
    public async Task TrulyEmptyRefreshSchedulesOneShortRetryOnly()
    {
        var harness = Create(RefreshIntervalMode.Manual);
        using var controller = harness.Controller;
        controller.Start();

        await harness.ViewModel.RefreshAsync();

        var retry = Assert.Single(harness.TimeProvider.ActiveTimers);
        Assert.Equal(UsagePollingController.EmptyRetryDelay, retry.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, retry.Period);
        retry.Fire();
        await WaitUntilAsync(() => harness.Provider.DailyCalls == 2 && !harness.ViewModel.IsRefreshing);
        Assert.Empty(harness.TimeProvider.ActiveTimers);
    }

    [Fact]
    public async Task MonthOnlyUsageIsNotTreatedAsEmpty()
    {
        var enrichment = new ProviderEnrichment(
            PeriodsOK: true,
            MonthTotal: new PeriodUsage("month", 42, 0));
        var harness = Create(RefreshIntervalMode.Manual, enrichment: enrichment);
        using var controller = harness.Controller;
        controller.Start();

        await harness.ViewModel.RefreshAsync();

        Assert.Equal(42, harness.ViewModel.MonthTokens);
        Assert.Empty(harness.TimeProvider.ActiveTimers);
    }

    [Fact]
    public async Task OfficialOnlyUsageIsNotTreatedAsEmpty()
    {
        var harness = Create(
            RefreshIntervalMode.Manual,
            providerId: "codex",
            limitsProvider: new FakeRateLimitsProvider());
        using var controller = harness.Controller;
        controller.Start();

        await harness.ViewModel.RefreshAsync();

        Assert.True(harness.ViewModel.HasCodexRateLimits);
        Assert.Empty(harness.TimeProvider.ActiveTimers);
    }

    [Fact]
    public async Task PauseCancelsPollingAndRetryAndResumeCreatesOneSchedule()
    {
        var harness = Create(RefreshIntervalMode.OneMinute);
        using var controller = harness.Controller;
        controller.Start();
        await harness.ViewModel.RefreshAsync();
        var oldTimers = harness.TimeProvider.Timers.ToArray();

        controller.Pause();
        foreach (var timer in oldTimers)
        {
            timer.Fire();
        }
        await Task.Yield();

        Assert.All(oldTimers, timer => Assert.True(timer.IsDisposed));
        Assert.Equal(1, harness.Provider.DailyCalls);

        controller.Resume();
        controller.Resume();
        var resumed = Assert.Single(harness.TimeProvider.ActiveTimers);
        Assert.Equal(TimeSpan.FromMinutes(1), resumed.Period);
    }

    [Fact]
    public void PersistedIntervalIsRestoredWhenPollingStarts()
    {
        var harness = Create(RefreshIntervalMode.FiveMinutes);
        using var controller = harness.Controller;

        controller.Start();

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            Assert.Single(harness.TimeProvider.ActiveTimers).Period);
    }

    private static Harness Create(
        RefreshIntervalMode interval,
        Func<CancellationToken, Task<DailyUsage?>>? daily = null,
        ProviderEnrichment? enrichment = null,
        string providerId = "test",
        ICodexRateLimitsProvider? limitsProvider = null)
    {
        var provider = new FakeUsageProvider(providerId, daily, enrichment);
        var viewModel = new UsageViewModel(
            new UsageStore([provider], codexRateLimitsProvider: limitsProvider));
        var persistence = new FakeSettingsPersistence(
            AppSettings.Default with { RefreshInterval = interval });
        var settings = new SettingsViewModel(persistence, new FakeAutoStart());
        var timeProvider = new FakeTimeProvider();
        var controller = new UsagePollingController(viewModel, settings, timeProvider);
        return new Harness(controller, viewModel, settings, provider, persistence, timeProvider);
    }

    private static DailyUsage Daily(long tokens) =>
        new(DateTimeOffset.Now.ToString("yyyy-MM-dd"), tokens, 0, 0, 0, tokens, 0);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Expected polling state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed record Harness(
        UsagePollingController Controller,
        UsageViewModel ViewModel,
        SettingsViewModel Settings,
        FakeUsageProvider Provider,
        FakeSettingsPersistence Persistence,
        FakeTimeProvider TimeProvider);

    private sealed class FakeUsageProvider(
        string id,
        Func<CancellationToken, Task<DailyUsage?>>? daily,
        ProviderEnrichment? enrichment) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public bool ReportsCost => true;
        public int DailyCalls { get; private set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default)
        {
            DailyCalls++;
            return daily?.Invoke(cancellationToken) ?? Task.FromResult<DailyUsage?>(null);
        }

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(enrichment ?? new ProviderEnrichment());
    }

    private sealed class FakeRateLimitsProvider : ICodexRateLimitsProvider
    {
        public Task<CodexRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = new CodexRateLimitSnapshot(
                "codex",
                "Codex",
                new CodexRateLimitWindow(10, 300, null),
                null,
                null,
                null,
                null,
                null);
            return Task.FromResult<CodexRateLimitStatus?>(new CodexRateLimitStatus(snapshot));
        }
    }

    private sealed class FakeSettingsPersistence(AppSettings settings) : IAppSettingsPersistence
    {
        public AppSettings? LastSaved { get; private set; } = settings;
        public AppSettings? Load() => LastSaved;
        public void Save(AppSettings value) => LastSaved = value;
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public List<FakeTimer> Timers { get; } = [];
        public IReadOnlyList<FakeTimer> ActiveTimers =>
            Timers.Where(static timer => !timer.IsDisposed).ToArray();

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new FakeTimer(callback, state, dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class FakeTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;
        public TimeSpan Period { get; private set; } = period;
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            if (IsDisposed)
            {
                return false;
            }

            DueTime = newDueTime;
            Period = newPeriod;
            return true;
        }

        public void Fire()
        {
            if (!IsDisposed)
            {
                callback(state);
            }
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
