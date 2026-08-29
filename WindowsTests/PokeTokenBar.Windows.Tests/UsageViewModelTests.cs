using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.App.Formatting;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class UsageViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Now);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-UsageViewModelTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void InitialState_BeforeRefreshMatchesEmptySwiftUiState()
    {
        var viewModel = ViewModel();

        Assert.Empty(viewModel.Providers);
        Assert.Null(viewModel.ProviderName);
        Assert.Null(viewModel.SelectedProviderId);
        Assert.Null(viewModel.TodayTokens);
        Assert.Null(viewModel.TodayTokensText);
        Assert.Null(viewModel.RecentFiveHourTokens);
        Assert.Null(viewModel.WeekTokens);
        Assert.Null(viewModel.MonthTokens);
        Assert.Equal(0, viewModel.TotalTodayTokens);
        Assert.Equal("0", viewModel.TotalTodayTokensText);
        Assert.False(viewModel.IsRefreshing);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.LastUpdated);
        Assert.Null(viewModel.LastUpdatedText);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task SingleProviderDaily_MapsSelectedAndAggregateUsage()
    {
        var daily = new DailyUsage(
            "2026-08-29",
            InputTokens: 10_000,
            OutputTokens: 1_000,
            CacheCreationTokens: 345,
            CacheReadTokens: 1_000,
            TotalTokens: 12_345,
            TotalCost: 0);
        var viewModel = ViewModel(Provider("one", daily: daily));

        await viewModel.RefreshAsync();

        Assert.Equal("one", viewModel.SelectedProviderId);
        Assert.Equal(12_345, viewModel.TodayTokens);
        Assert.Equal("12.3K", viewModel.TodayTokensText);
        Assert.Equal("12,345", viewModel.TodayTokensGroupedText);
        Assert.Equal(10_000, viewModel.InputTokens);
        Assert.Equal("10K", viewModel.InputTokensText);
        Assert.Equal(1_000, viewModel.OutputTokens);
        Assert.Equal("1K", viewModel.OutputTokensText);
        Assert.Equal(345, viewModel.CacheWriteTokens);
        Assert.Equal(1_000, viewModel.CacheReadTokens);
        Assert.Equal(12_345, viewModel.TotalTodayTokens);
    }

    [Fact]
    public async Task ActiveFiveHourBlock_MapsTokenValue()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(40, 50, 60));
        var viewModel = ViewModel(provider);

        await viewModel.RefreshAsync();

        Assert.Equal(40, viewModel.RecentFiveHourTokens);
        Assert.Equal("40", viewModel.RecentFiveHourTokensText);
    }

    [Fact]
    public async Task WeekUsage_MapsSelectedAndAggregateValues()
    {
        var viewModel = ViewModel(
            Provider("one", daily: Daily(10), enrichment: Enrichment(40, 50, 60)));

        await viewModel.RefreshAsync();

        Assert.Equal(50, viewModel.WeekTokens);
        Assert.Equal("50", viewModel.WeekTokensText);
        Assert.Equal(50, viewModel.TotalWeekTokens);
    }

    [Fact]
    public async Task MonthUsage_MapsSelectedAndAggregateValues()
    {
        var viewModel = ViewModel(
            Provider("one", daily: Daily(10), enrichment: Enrichment(40, 50, 60)));

        await viewModel.RefreshAsync();

        Assert.Equal(60, viewModel.MonthTokens);
        Assert.Equal("60", viewModel.MonthTokensText);
        Assert.Equal(60, viewModel.TotalMonthTokens);
    }

    [Fact]
    public async Task ProviderDisplayName_IsTakenFromSelectedSnapshot()
    {
        var viewModel = ViewModel(Provider("one", "Provider One", Daily(10)));

        await viewModel.RefreshAsync();

        Assert.Equal("Provider One", viewModel.ProviderName);
    }

    [Fact]
    public async Task NullToday_RemainsNullInsteadOfBeingPresentedAsZero()
    {
        var provider = Provider("one", daily: null, enrichment: Enrichment(40, 50, 60));
        var viewModel = ViewModel(provider);

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.TodayTokens);
        Assert.Null(viewModel.TodayTokensText);
        Assert.Null(viewModel.InputTokens);
        Assert.Null(viewModel.OutputTokens);
        Assert.Null(viewModel.CacheWriteTokens);
        Assert.Null(viewModel.CacheReadTokens);
        Assert.Equal(0, viewModel.TotalTodayTokens);
        Assert.Equal(40, viewModel.RecentFiveHourTokens);
    }

    [Fact]
    public async Task NullBlockWeekAndMonth_RemainNull()
    {
        var viewModel = ViewModel(Provider("one", daily: Daily(10)));

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.RecentFiveHourTokens);
        Assert.Null(viewModel.RecentFiveHourTokensText);
        Assert.Null(viewModel.WeekTokens);
        Assert.Null(viewModel.MonthTokens);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(987, "987")]
    [InlineData(12_345, "12.3K")]
    [InlineData(190_612_940, "190.6M")]
    [InlineData(1_240_000_000, "1.24B")]
    [InlineData(1_000_000, "1M")]
    [InlineData(-12_345, "-12.3K")]
    public void CompactTokenFormatting_MatchesSwift(long value, string expected)
    {
        Assert.Equal(expected, UsageValueFormatter.Compact(value));
    }

    [Fact]
    public void GroupedAndCostFormatting_MatchSwift()
    {
        Assert.Equal(
            "253,412,890",
            UsageValueFormatter.Grouped(253_412_890, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("$48.10", UsageValueFormatter.Cost(48.104));
        Assert.Equal("$9.5", UsageValueFormatter.CompactCost(9.54));
        Assert.Equal("$311", UsageValueFormatter.CompactCost(311.4));
        Assert.Equal("$12.3K", UsageValueFormatter.CompactCost(12_340));
    }

    [Fact]
    public async Task CostMapping_RespectsProviderReportsCost()
    {
        var paid = Provider(
            "paid",
            daily: Daily(10, cost: 1.23),
            enrichment: Enrichment(1, 2, 3, 4.56, 7.89));
        var viewModel = ViewModel(paid);

        await viewModel.RefreshAsync();

        Assert.Equal(1.23, viewModel.TodayCost);
        Assert.Equal("$1.23", viewModel.TodayCostText);
        Assert.Equal(4.56, viewModel.WeekCost);
        Assert.Equal(7.89, viewModel.MonthCost);
        Assert.True(viewModel.ShowsCost);
        Assert.Equal("$1.23", viewModel.TotalTodayCostText);
        Assert.Equal("2", viewModel.TotalWeekTokensText);
        Assert.Equal("3", viewModel.TotalMonthTokensText);
        Assert.Equal("$4.56", viewModel.TotalWeekCostText);
        Assert.Equal("$7.89", viewModel.TotalMonthCostText);

        var flat = ViewModel(Provider("flat", daily: Daily(10, cost: 99), reportsCost: false));
        await flat.RefreshAsync();
        Assert.Null(flat.TodayCost);
        Assert.Null(flat.TodayCostText);
        Assert.False(flat.ShowsCost);
    }

    [Fact]
    public async Task RefreshAsync_InvokesStoreAndAppliesFinalState()
    {
        var provider = Provider("one", daily: Daily(10));
        var viewModel = ViewModel(provider);

        await viewModel.RefreshAsync();

        Assert.Equal(1, provider.DailyCalls);
        Assert.Equal(1, provider.EnrichmentCalls);
        Assert.Equal(10, viewModel.TodayTokens);
        Assert.Equal(Now, viewModel.LastUpdated);
        Assert.Equal("just now", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task RefreshCommand_RaisesPropertyNotificationsForMappedValues()
    {
        var viewModel = ViewModel(Provider("one", "One", Daily(12_345)));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.Contains(nameof(UsageViewModel.ProviderName), changed);
        Assert.Contains(nameof(UsageViewModel.TodayTokens), changed);
        Assert.Contains(nameof(UsageViewModel.TodayTokensText), changed);
        Assert.Contains(nameof(UsageViewModel.TotalTodayTokens), changed);
        Assert.Contains(nameof(UsageViewModel.LastUpdated), changed);
        Assert.Contains(nameof(UsageViewModel.LastUpdatedText), changed);
    }

    [Fact]
    public async Task PropertyChanged_UsesExactPropertyNames()
    {
        var viewModel = ViewModel(Provider("one", daily: Daily(10)));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.RefreshAsync();

        Assert.DoesNotContain(changed, string.IsNullOrWhiteSpace);
        Assert.All(changed, name => Assert.NotNull(typeof(UsageViewModel).GetProperty(name!)));
    }

    [Fact]
    public async Task IsRefreshingAndCanExecute_TrackRefreshLifetime()
    {
        var started = NewSignal();
        var release = NewSignal();
        var provider = Provider(
            "one",
            dailyHandler: async token =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return Daily(10);
            });
        var viewModel = ViewModel(provider);

        var refresh = viewModel.RefreshCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(viewModel.IsRefreshing);
        Assert.True(viewModel.RefreshCommand.IsExecuting);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));

        release.SetResult();
        await refresh;
        Assert.False(viewModel.IsRefreshing);
        Assert.False(viewModel.RefreshCommand.IsExecuting);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshCommand_PreventsDuplicateExecutionWhileRunning()
    {
        var started = NewSignal();
        var release = NewSignal();
        var provider = Provider(
            "one",
            dailyHandler: async token =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return Daily(10);
            });
        var viewModel = ViewModel(provider);

        var first = viewModel.RefreshCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await viewModel.RefreshCommand.ExecuteAsync();
        release.SetResult();
        await first;

        Assert.Equal(1, provider.DailyCalls);
    }

    [Fact]
    public async Task DailyFailure_PreservesPreviousVisibleSnapshotAndShowsError()
    {
        var provider = Provider("one", daily: Daily(10));
        var viewModel = ViewModel(provider);
        await viewModel.RefreshAsync();
        provider.DailyError = new TestException("boom");

        await viewModel.RefreshAsync();

        Assert.Equal(10, viewModel.TodayTokens);
        Assert.Contains("one: boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SuccessfulNullDaily_RemovesPreviousTodayFromUi()
    {
        var provider = Provider("one", daily: Daily(10));
        var viewModel = ViewModel(provider);
        await viewModel.RefreshAsync();
        provider.Daily = null;

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.TodayTokens);
        Assert.Empty(viewModel.Providers);
    }

    [Fact]
    public async Task EnrichmentFailure_PreservesPreviouslyDisplayedDetails()
    {
        var provider = Provider("one", daily: Daily(10), enrichment: Enrichment(40, 50, 60));
        var viewModel = ViewModel(provider);
        await viewModel.RefreshAsync();
        provider.EnrichmentError = new TestException("ignored");

        await viewModel.RefreshAsync();

        Assert.Equal(40, viewModel.RecentFiveHourTokens);
        Assert.Equal(50, viewModel.WeekTokens);
        Assert.Equal(60, viewModel.MonthTokens);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LaterSuccessfulRefresh_UpdatesValuesAndClearsError()
    {
        var provider = Provider("one", dailyError: new TestException("boom"));
        var viewModel = ViewModel(provider);
        await viewModel.RefreshAsync();
        provider.DailyError = null;
        provider.Daily = Daily(20);

        await viewModel.RefreshAsync();

        Assert.Equal(20, viewModel.TodayTokens);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PreferredProviderSelectionAndFallback_MatchSwift()
    {
        var one = Provider("one", "One", Daily(10));
        var two = Provider("two", "Two", Daily(20));
        var viewModel = ViewModel("two", one, two);
        await viewModel.RefreshAsync();

        Assert.Equal("two", viewModel.SelectedProviderId);
        Assert.Equal("Two", viewModel.ProviderName);
        Assert.Equal(20, viewModel.TodayTokens);

        viewModel.PreferredProviderId = "missing";
        Assert.Equal("one", viewModel.SelectedProviderId);
        Assert.Equal(10, viewModel.TodayTokens);
    }

    [Fact]
    public async Task ProviderOrdering_ComesDirectlyFromStoreSnapshots()
    {
        var viewModel = ViewModel(
            Provider("third", daily: Daily(3)),
            Provider("first", daily: Daily(1)),
            Provider("second", daily: Daily(2)));

        await viewModel.RefreshAsync();

        Assert.Equal(
            ["third", "first", "second"],
            viewModel.Providers.Select(provider => provider.ProviderId));
    }

    [Fact]
    public async Task NoProvider_RemainsSafeAfterRefresh()
    {
        var viewModel = ViewModel();

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Providers);
        Assert.Null(viewModel.ProviderName);
        Assert.Equal(0, viewModel.TotalTodayTokens);
        Assert.Equal(Now, viewModel.LastUpdated);
    }

    [Fact]
    public async Task Cancellation_RestoresUiRefreshStateWithoutCreatingError()
    {
        var started = NewSignal();
        var provider = Provider(
            "one",
            dailyHandler: async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Daily(10);
            });
        var viewModel = ViewModel(provider);
        using var cancellation = new CancellationTokenSource();

        var refresh = viewModel.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(viewModel.IsRefreshing);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AsyncCommand_CapturesUnhandledExceptionAtUiBoundary()
    {
        Exception? captured = null;
        var command = new AsyncCommand(
            _ => Task.FromException(new TestException("command failed")),
            onException: exception => captured = exception);

        await command.ExecuteAsync();

        Assert.Equal("command failed", captured?.Message);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void ViewModelPublicContract_UsesCoreModelsAndNoInfrastructureOrCodexTypes()
    {
        var members = typeof(UsageViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var exposedTypes = members.SelectMany(MemberTypes).Where(type => type is not null).Cast<Type>();
        var fields = typeof(UsageViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(exposedTypes, type =>
            type.Namespace?.Contains("Infrastructure", StringComparison.Ordinal) == true ||
            type.Name.Contains("Codex", StringComparison.Ordinal));
        Assert.DoesNotContain(fields, field =>
            field.FieldType.Namespace?.Contains("Infrastructure", StringComparison.Ordinal) == true ||
            field.FieldType.Name.Contains("Codex", StringComparison.Ordinal));
        Assert.Contains(typeof(INotifyPropertyChanged), typeof(UsageViewModel).GetInterfaces());
    }

    [Fact]
    public async Task LocalCodexStoreAndViewModel_UseOnlyTemporaryFilesystem()
    {
        var root = Path.Combine(_temporaryDirectory, "codex");
        var actualNow = DateTimeOffset.Now;
        WriteRollout(root, "one.jsonl", actualNow, "local-codex", 80, 10, 40);
        var clock = new FixedTimeProvider(actualNow);
        var store = new UsageStore([new LocalCodexUsageProvider([root])], clock);
        var viewModel = new UsageViewModel(store, timeProvider: clock);

        await viewModel.RefreshAsync();

        Assert.Equal("codex", viewModel.SelectedProviderId);
        Assert.Equal("Codex", viewModel.ProviderName);
        Assert.Equal(130, viewModel.TodayTokens);
        Assert.Equal("130", viewModel.TodayTokensText);
    }

    private static IEnumerable<Type?> MemberTypes(MemberInfo member) => member switch
    {
        PropertyInfo property => [property.PropertyType],
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(x => x.ParameterType)],
        ConstructorInfo constructor => constructor.GetParameters().Select(x => x.ParameterType),
        EventInfo eventInfo => [eventInfo.EventHandlerType!],
        _ => [],
    };

    private static UsageViewModel ViewModel(params TestUsageProvider[] providers)
    {
        var store = new UsageStore(providers, Clock);
        return new UsageViewModel(store, timeProvider: Clock);
    }

    private static UsageViewModel ViewModel(
        string preferredProviderId,
        params TestUsageProvider[] providers)
    {
        var store = new UsageStore(providers, Clock);
        return new UsageViewModel(store, preferredProviderId, Clock);
    }

    private static TestUsageProvider Provider(
        string id,
        string? displayName = null,
        DailyUsage? daily = null,
        bool reportsCost = true,
        ProviderEnrichment? enrichment = null,
        Exception? dailyError = null,
        Exception? enrichmentError = null,
        Func<CancellationToken, Task<DailyUsage?>>? dailyHandler = null) =>
        new(id, displayName ?? id, reportsCost)
        {
            Daily = daily,
            Enrichment = enrichment ?? new ProviderEnrichment(),
            DailyError = dailyError,
            EnrichmentError = enrichmentError,
            DailyHandler = dailyHandler,
        };

    private static DailyUsage Daily(long total, double cost = 0) =>
        new("2026-08-29", total, 0, 0, 0, total, cost);

    private static ProviderEnrichment Enrichment(
        long block,
        long week,
        long month,
        double weekCost = 0,
        double monthCost = 0) =>
        new(
            new BlockUsage("block", "start", "end", true, block, 0, 1),
            BlocksOK: true,
            new PeriodUsage("week", week, weekCost),
            new PeriodUsage("month", month, monthCost),
            PeriodsOK: true);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void WriteRollout(
        string root,
        string relativePath,
        DateTimeOffset now,
        string sessionId,
        long input,
        long output,
        long cacheRead)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var timestamp = now.AddMinutes(-5).ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
        var meta = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + sessionId
            + "\",\"thread_source\":\"user\"}}";
        var lastInput = input + cacheRead;
        var total = input + output + cacheRead;
        var state = "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
            + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
            + "\"total_token_usage\":{"
            + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{cacheRead},"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{output},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{total}}},"
            + "\"last_token_usage\":{"
            + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":{cacheRead},"
            + $"\"cache_write_input_tokens\":0,\"output_tokens\":{output},"
            + $"\"reasoning_output_tokens\":0,\"total_tokens\":{total}}}}}}}}}";
        File.WriteAllText(path, string.Join(Environment.NewLine, meta, state));
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
    }

    private sealed class TestUsageProvider(
        string id,
        string displayName,
        bool reportsCost) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public bool ReportsCost { get; } = reportsCost;
        public DailyUsage? Daily { get; set; }
        public ProviderEnrichment Enrichment { get; set; } = new();
        public Exception? DailyError { get; set; }
        public Exception? EnrichmentError { get; set; }
        public Func<CancellationToken, Task<DailyUsage?>>? DailyHandler { get; set; }
        public int DailyCalls { get; private set; }
        public int EnrichmentCalls { get; private set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default)
        {
            DailyCalls++;
            if (DailyHandler is not null)
            {
                return DailyHandler(cancellationToken);
            }

            return DailyError is null
                ? Task.FromResult(Daily)
                : Task.FromException<DailyUsage?>(DailyError);
        }

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default)
        {
            EnrichmentCalls++;
            return EnrichmentError is null
                ? Task.FromResult(Enrichment)
                : Task.FromException<ProviderEnrichment>(EnrichmentError);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone(
                $"UsageViewModelTests/{Guid.NewGuid():N}",
                now.Offset,
                "Test",
                "Test");
    }

    private sealed class TestException(string message) : Exception(message);
}
