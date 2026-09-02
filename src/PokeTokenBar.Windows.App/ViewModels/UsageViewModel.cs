using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.App.Formatting;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class UsageViewModel : INotifyPropertyChanged
{
    private readonly UsageStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly LocalizationService _localization;

    private IReadOnlyList<ProviderSnapshot> _providers = Array.Empty<ProviderSnapshot>();
    private string? _preferredProviderId;
    private string? _selectedProviderId;
    private string? _providerName;
    private long? _todayTokens;
    private long? _inputTokens;
    private long? _outputTokens;
    private long? _cacheWriteTokens;
    private long? _cacheReadTokens;
    private long? _recentFiveHourTokens;
    private long? _weekTokens;
    private long? _monthTokens;
    private double? _todayCost;
    private double? _weekCost;
    private double? _monthCost;
    private long _totalTodayTokens;
    private long _totalWeekTokens;
    private long _totalMonthTokens;
    private double _totalTodayCost;
    private double _totalWeekCost;
    private double _totalMonthCost;
    private bool _showsCost;
    private bool _isRefreshing;
    private string? _errorMessage;
    private DateTimeOffset? _lastUpdated;
    private string? _lastUpdatedText;
    private bool _hasCodexRateLimits;
    private bool _hasFiveHourLimit;
    private int _fiveHourRemainingPercent;
    private string? _fiveHourRemainingText;
    private string? _fiveHourResetText;
    private bool _hasWeeklyLimit;
    private int _weeklyRemainingPercent;
    private string? _weeklyRemainingText;
    private string? _weeklyResetText;
    private string? _officialLimitsMetadataText;
    private IReadOnlyList<OfficialLimitRow> _antigravityLimitRows = Array.Empty<OfficialLimitRow>();
    private IReadOnlyList<OfficialLimitRow> _officialLimitRows = Array.Empty<OfficialLimitRow>();
    private IReadOnlyList<ProviderStatusSnapshot> _providerStatuses = Array.Empty<ProviderStatusSnapshot>();
    private string? _providerStatusText;
    private string? _providerAuthStatusText;
    private string? _creditsText;
    private string? _burnRateText;
    private string? _forecastText;
    private LimitDisplayMode _limitDisplayMode = LimitDisplayMode.Remaining;

    public UsageViewModel(
        UsageStore store,
        string? preferredProviderId = null,
        TimeProvider? timeProvider = null,
        LocalizationService? localization = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _preferredProviderId = preferredProviderId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _localization = localization ?? new LocalizationService(AppLanguage.En);
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => !IsRefreshing,
            exception => ErrorMessage = exception.Message);
        ApplyStoreState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler<RefreshCompletedEventArgs>? RefreshCompleted;

    internal IReadOnlyDictionary<string, long> TodayTokensByProvider =>
        _store.TodayTokensByProvider;

    internal string TodayDate =>
        _timeProvider.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal bool HasUsageData => _store.HasUsageData;

    internal IReadOnlyList<string> RegisteredProviderIds => _store.RegisteredProviderIds;

    internal bool HasRefreshError => !string.IsNullOrWhiteSpace(_store.LastErrorDescription);

    internal UsageCacheLoadStatus UsageCacheStatus => _store.UsageCacheStatus;

    internal bool LimitsReady =>
        _store.ClaudeRateLimits is not null ||
        _store.CodexRateLimits is not null ||
        _store.AntigravityRateLimits is not null;

    internal IReadOnlyList<CandyWindow> CandyEligibleWindows
    {
        get
        {
            var windows = new List<CandyWindow>();
            if (_store.ClaudeRateLimits?.FiveHour is { } fiveHour)
            {
                windows.Add(new CandyWindow(
                    "claude.fiveHour", _localization.ClaudeFiveHour,
                    LimitWindowClass.Session, fiveHour.UsedPercent));
            }

            if (_store.ClaudeRateLimits?.SevenDay is { } sevenDay)
            {
                windows.Add(new CandyWindow(
                    "claude.sevenDay", _localization.ClaudeWeekly,
                    LimitWindowClass.Weekly, sevenDay.UsedPercent));
            }

            foreach (var snapshot in _store.CodexRateLimits?.VisibleSnapshots ?? [])
            {
                var key = snapshot.LimitId ?? snapshot.LimitName ?? "codex";
                var name = snapshot.LimitName ?? "Codex";
                if (snapshot.Primary is { } primary)
                {
                    windows.Add(new CandyWindow(
                        $"codex.{key}.primary", $"{name} {_localization.Session}",
                        WindowClass(primary.WindowDurationMinutes), primary.UsedPercent));
                }

                if (snapshot.Secondary is { } secondary)
                {
                    windows.Add(new CandyWindow(
                        $"codex.{key}.secondary", $"{name} {_localization.Weekly}",
                        WindowClass(secondary.WindowDurationMinutes), secondary.UsedPercent));
                }
            }

            foreach (var group in _store.AntigravityRateLimits?.Groups ?? [])
            {
                var groupKey = group.DisplayName.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                    ? "gemini"
                    : "3p";
                if (group.FiveHour is { } session)
                {
                    windows.Add(new CandyWindow(
                        $"antigravity.{groupKey}.5h", $"{group.DisplayName} {_localization.FiveHourSession}",
                        LimitWindowClass.Session, session.UsedPercent));
                }

                if (group.Weekly is { } weekly)
                {
                    windows.Add(new CandyWindow(
                        $"antigravity.{groupKey}.weekly", $"{group.DisplayName} {_localization.Weekly}",
                        LimitWindowClass.Weekly, weekly.UsedPercent));
                }
            }

            return windows;
        }
    }

    internal static LimitWindowClass WindowClass(int? minutes) =>
        minutes is > 1440 ? LimitWindowClass.Weekly : LimitWindowClass.Session;

    internal IReadOnlyList<LimitNotificationWindow> NotificationWindows =>
        CandyEligibleWindows.Select(window => new LimitNotificationWindow(
            window.Key, window.Name, window.Utilization)).ToArray();

    internal void SetLimitDisplayMode(LimitDisplayMode mode)
    {
        if (_limitDisplayMode == mode) return;
        _limitDisplayMode = mode;
        ApplyOfficialLimits(SelectedProviderId);
    }

    public static int DisplayPercent(double? usedPercent, LimitDisplayMode mode)
    {
        var used = (int)Math.Round(
            Math.Clamp(usedPercent ?? 100, 0, 100),
            MidpointRounding.AwayFromZero);
        return mode == LimitDisplayMode.Remaining ? 100 - used : used;
    }

    public AsyncCommand RefreshCommand { get; }

    public IReadOnlyList<ProviderSnapshot> Providers
    {
        get => _providers;
        private set => SetField(ref _providers, value);
    }

    public string? PreferredProviderId
    {
        get => _preferredProviderId;
        set
        {
            if (SetField(ref _preferredProviderId, value))
            {
                ApplyStoreState();
            }
        }
    }

    public string? SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            if (value != _selectedProviderId)
            {
                PreferredProviderId = value;
            }
        }
    }

    public string? ProviderName
    {
        get => _providerName;
        private set
        {
            if (SetField(ref _providerName, value)) OnPropertyChanged(nameof(ProviderNameText));
        }
    }

    public string ProviderNameText => ProviderName ?? _localization.NoUsageData;

    public long? TodayTokens
    {
        get => _todayTokens;
        private set
        {
            if (SetField(ref _todayTokens, value))
            {
                OnPropertyChanged(nameof(TodayTokensText));
                OnPropertyChanged(nameof(TodayTokensGroupedText));
            }
        }
    }

    public string? TodayTokensText =>
        TodayTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public string? TodayTokensGroupedText =>
        TodayTokens is long value ? UsageValueFormatter.Grouped(value) : null;

    public long? InputTokens
    {
        get => _inputTokens;
        private set
        {
            if (SetField(ref _inputTokens, value))
            {
                OnPropertyChanged(nameof(InputTokensText));
            }
        }
    }

    public string? InputTokensText =>
        InputTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? OutputTokens
    {
        get => _outputTokens;
        private set
        {
            if (SetField(ref _outputTokens, value))
            {
                OnPropertyChanged(nameof(OutputTokensText));
            }
        }
    }

    public string? OutputTokensText =>
        OutputTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? CacheWriteTokens
    {
        get => _cacheWriteTokens;
        private set
        {
            if (SetField(ref _cacheWriteTokens, value))
            {
                OnPropertyChanged(nameof(CacheWriteTokensText));
            }
        }
    }

    public string? CacheWriteTokensText =>
        CacheWriteTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? CacheReadTokens
    {
        get => _cacheReadTokens;
        private set
        {
            if (SetField(ref _cacheReadTokens, value))
            {
                OnPropertyChanged(nameof(CacheReadTokensText));
            }
        }
    }

    public string? CacheReadTokensText =>
        CacheReadTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? RecentFiveHourTokens
    {
        get => _recentFiveHourTokens;
        private set
        {
            if (SetField(ref _recentFiveHourTokens, value))
            {
                OnPropertyChanged(nameof(RecentFiveHourTokensText));
            }
        }
    }

    public string? RecentFiveHourTokensText =>
        RecentFiveHourTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? WeekTokens
    {
        get => _weekTokens;
        private set
        {
            if (SetField(ref _weekTokens, value))
            {
                OnPropertyChanged(nameof(WeekTokensText));
            }
        }
    }

    public string? WeekTokensText =>
        WeekTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public long? MonthTokens
    {
        get => _monthTokens;
        private set
        {
            if (SetField(ref _monthTokens, value))
            {
                OnPropertyChanged(nameof(MonthTokensText));
            }
        }
    }

    public string? MonthTokensText =>
        MonthTokens is long value ? UsageValueFormatter.Compact(value) : null;

    public double? TodayCost
    {
        get => _todayCost;
        private set
        {
            if (SetField(ref _todayCost, value))
            {
                OnPropertyChanged(nameof(TodayCostText));
            }
        }
    }

    public string? TodayCostText =>
        TodayCost is double value ? UsageValueFormatter.Cost(value) : null;

    public double? WeekCost
    {
        get => _weekCost;
        private set
        {
            if (SetField(ref _weekCost, value))
            {
                OnPropertyChanged(nameof(WeekCostText));
            }
        }
    }

    public string? WeekCostText =>
        WeekCost is double value ? UsageValueFormatter.Cost(value) : null;

    public double? MonthCost
    {
        get => _monthCost;
        private set
        {
            if (SetField(ref _monthCost, value))
            {
                OnPropertyChanged(nameof(MonthCostText));
            }
        }
    }

    public string? MonthCostText =>
        MonthCost is double value ? UsageValueFormatter.Cost(value) : null;

    public long TotalTodayTokens
    {
        get => _totalTodayTokens;
        private set
        {
            if (SetField(ref _totalTodayTokens, value))
            {
                OnPropertyChanged(nameof(TotalTodayTokensText));
                OnPropertyChanged(nameof(TotalTodayTokensGroupedText));
            }
        }
    }

    public string TotalTodayTokensText => UsageValueFormatter.Compact(TotalTodayTokens);

    public string TotalTodayTokensGroupedText => UsageValueFormatter.Grouped(TotalTodayTokens);

    public long TotalWeekTokens
    {
        get => _totalWeekTokens;
        private set
        {
            if (SetField(ref _totalWeekTokens, value))
            {
                OnPropertyChanged(nameof(TotalWeekTokensText));
            }
        }
    }

    public string TotalWeekTokensText => UsageValueFormatter.Compact(TotalWeekTokens);

    public long TotalMonthTokens
    {
        get => _totalMonthTokens;
        private set
        {
            if (SetField(ref _totalMonthTokens, value))
            {
                OnPropertyChanged(nameof(TotalMonthTokensText));
            }
        }
    }

    public string TotalMonthTokensText => UsageValueFormatter.Compact(TotalMonthTokens);

    public double TotalTodayCost
    {
        get => _totalTodayCost;
        private set
        {
            if (SetField(ref _totalTodayCost, value))
            {
                OnPropertyChanged(nameof(TotalTodayCostText));
            }
        }
    }

    public string TotalTodayCostText => UsageValueFormatter.Cost(TotalTodayCost);

    public double TotalWeekCost
    {
        get => _totalWeekCost;
        private set
        {
            if (SetField(ref _totalWeekCost, value))
            {
                OnPropertyChanged(nameof(TotalWeekCostText));
            }
        }
    }

    public string TotalWeekCostText => UsageValueFormatter.Cost(TotalWeekCost);

    public double TotalMonthCost
    {
        get => _totalMonthCost;
        private set
        {
            if (SetField(ref _totalMonthCost, value))
            {
                OnPropertyChanged(nameof(TotalMonthCostText));
            }
        }
    }

    public string TotalMonthCostText => UsageValueFormatter.Cost(TotalMonthCost);

    public bool ShowsCost
    {
        get => _showsCost;
        private set => SetField(ref _showsCost, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetField(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public string? LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetField(ref _lastUpdatedText, value);
    }

    public bool HasCodexRateLimits
    {
        get => _hasCodexRateLimits;
        private set => SetField(ref _hasCodexRateLimits, value);
    }

    public bool HasFiveHourLimit
    {
        get => _hasFiveHourLimit;
        private set => SetField(ref _hasFiveHourLimit, value);
    }

    public int FiveHourRemainingPercent
    {
        get => _fiveHourRemainingPercent;
        private set => SetField(ref _fiveHourRemainingPercent, value);
    }

    public string? FiveHourRemainingText
    {
        get => _fiveHourRemainingText;
        private set => SetField(ref _fiveHourRemainingText, value);
    }

    public string? FiveHourResetText
    {
        get => _fiveHourResetText;
        private set => SetField(ref _fiveHourResetText, value);
    }

    public bool HasWeeklyLimit
    {
        get => _hasWeeklyLimit;
        private set => SetField(ref _hasWeeklyLimit, value);
    }

    public int WeeklyRemainingPercent
    {
        get => _weeklyRemainingPercent;
        private set => SetField(ref _weeklyRemainingPercent, value);
    }

    public string? WeeklyRemainingText
    {
        get => _weeklyRemainingText;
        private set => SetField(ref _weeklyRemainingText, value);
    }

    public string? WeeklyResetText
    {
        get => _weeklyResetText;
        private set => SetField(ref _weeklyResetText, value);
    }

    public string? OfficialLimitsMetadataText
    {
        get => _officialLimitsMetadataText;
        private set => SetField(ref _officialLimitsMetadataText, value);
    }

    public IReadOnlyList<OfficialLimitRow> AntigravityLimitRows
    {
        get => _antigravityLimitRows;
        private set
        {
            if (SetField(ref _antigravityLimitRows, value))
            {
                OnPropertyChanged(nameof(HasAntigravityLimitRows));
            }
        }
    }

    public bool HasAntigravityLimitRows => AntigravityLimitRows.Count > 0;

    public IReadOnlyList<OfficialLimitRow> OfficialLimitRows
    {
        get => _officialLimitRows;
        private set
        {
            if (SetField(ref _officialLimitRows, value))
            {
                OnPropertyChanged(nameof(HasOfficialLimitRows));
            }
        }
    }

    public bool HasOfficialLimitRows => OfficialLimitRows.Count > 0;

    public IReadOnlyList<ProviderStatusSnapshot> ProviderStatuses
    {
        get => _providerStatuses;
        private set => SetField(ref _providerStatuses, value);
    }

    public string? ProviderStatusText
    {
        get => _providerStatusText;
        private set => SetField(ref _providerStatusText, value);
    }

    public string? ProviderAuthStatusText
    {
        get => _providerAuthStatusText;
        private set => SetField(ref _providerAuthStatusText, value);
    }

    public string? CreditsText
    {
        get => _creditsText;
        private set
        {
            if (SetField(ref _creditsText, value)) OnPropertyChanged(nameof(HasCredits));
        }
    }

    public bool HasCredits => CreditsText is not null;

    public string? BurnRateText
    {
        get => _burnRateText;
        private set
        {
            if (SetField(ref _burnRateText, value)) OnPropertyChanged(nameof(HasBurnForecast));
        }
    }

    public string? ForecastText
    {
        get => _forecastText;
        private set
        {
            if (SetField(ref _forecastText, value)) OnPropertyChanged(nameof(HasBurnForecast));
        }
    }

    public bool HasBurnForecast => BurnRateText is not null || ForecastText is not null;

    internal void RefreshPresentation()
    {
        ApplyStoreState();
        OnPropertyChanged(nameof(ProviderNameText));
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(scheduleEmptyRetry: true, cancellationToken);

    internal async Task RefreshAsync(
        bool scheduleEmptyRetry,
        CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        Exception? refreshError = null;
        var cancelled = false;
        try
        {
            await _store.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (Exception exception)
        {
            refreshError = exception;
            throw;
        }
        finally
        {
            ApplyStoreState();
            if (refreshError is not null)
            {
                ErrorMessage = refreshError.Message;
            }

            IsRefreshing = false;
            RefreshCompleted?.Invoke(
                this,
                new RefreshCompletedEventArgs(
                    scheduleEmptyRetry && !cancelled && refreshError is null,
                    !cancelled && refreshError is null));
        }
    }

    private void ApplyStoreState()
    {
        Providers = new ReadOnlyCollection<ProviderSnapshot>(_store.Snapshots.ToArray());
        ProviderStatuses = new ReadOnlyCollection<ProviderStatusSnapshot>(
            _store.ProviderStatuses.ToArray());
        var selected = _store.Snapshot(PreferredProviderId);
        SetField(ref _selectedProviderId, selected?.ProviderId, nameof(SelectedProviderId));
        ProviderName = selected?.DisplayName;
        TodayTokens = selected?.Today?.TotalTokens;
        InputTokens = selected?.Today?.InputTokens;
        OutputTokens = selected?.Today?.OutputTokens;
        CacheWriteTokens = selected?.Today?.CacheCreationTokens;
        CacheReadTokens = selected?.Today?.CacheReadTokens;
        RecentFiveHourTokens = selected?.ActiveBlock?.TotalTokens;
        WeekTokens = selected?.WeekTotal?.TotalTokens;
        MonthTokens = selected?.MonthTotal?.TotalTokens;

        var reportsCost = selected?.ReportsCost == true;
        TodayCost = reportsCost ? selected?.Today?.TotalCost : null;
        WeekCost = reportsCost ? selected?.WeekTotal?.TotalCost : null;
        MonthCost = reportsCost ? selected?.MonthTotal?.TotalCost : null;

        TotalTodayTokens = _store.TodayTotalTokens;
        TotalWeekTokens = _store.WeekTotalTokens;
        TotalMonthTokens = _store.MonthTotalTokens;
        TotalTodayCost = _store.TodayCostTotal;
        TotalWeekCost = _store.WeekCostTotal;
        TotalMonthCost = _store.MonthCostTotal;
        ShowsCost = _store.ShowsCost;
        ErrorMessage = _store.LastErrorDescription;
        LastUpdated = _store.LastUpdated;
        LastUpdatedText = FormatRelative(_store.LastUpdated);
        var providerStatus = ProviderStatuses.FirstOrDefault(status =>
            status.ProviderId == selected?.ProviderId);
        if (providerStatus is not null)
        {
            var runtime = IsOfficialLimitsStale(providerStatus.ProviderId)
                ? ProviderRuntimeStatus.Stale
                : providerStatus.RuntimeStatus;
            ProviderStatusText = _localization.RuntimeStatus(runtime);
            ProviderAuthStatusText = _localization.AuthStatus(providerStatus.AuthStatus);
        }
        else
        {
            ProviderStatusText = null;
            ProviderAuthStatusText = null;
        }
        ApplyOfficialLimits(selected?.ProviderId);
    }

    private void ApplyOfficialLimits(string? selectedProviderId)
    {
        double? primaryUsed = null;
        double? secondaryUsed = null;
        DateTimeOffset? primaryReset = null;
        DateTimeOffset? secondaryReset = null;
        var rows = new List<OfficialLimitRow>();
        OfficialLimitsMetadataText = null;
        AntigravityLimitRows = Array.Empty<OfficialLimitRow>();
        CreditsText = null;
        BurnRateText = null;
        ForecastText = null;

        if (selectedProviderId == "codex")
        {
            var status = _store.CodexRateLimits;
            var snapshot = status?.RateLimits.HasVisibleLimit == true
                ? status.RateLimits
                : status?.VisibleSnapshots.FirstOrDefault();
            primaryUsed = snapshot?.Primary?.UsedPercent;
            secondaryUsed = snapshot?.Secondary?.UsedPercent;
            primaryReset = snapshot?.Primary?.ResetsAt;
            secondaryReset = snapshot?.Secondary?.ResetsAt;
            var buckets = status?.VisibleSnapshots ?? [];
            foreach (var bucket in buckets)
            {
                var prefix = buckets.Count > 1 ? $"{BucketLabel(bucket)} · " : "";
                if (bucket.Primary is { } primary)
                {
                    rows.Add(LimitRow(prefix + WindowLabel(primary.WindowDurationMinutes),
                        primary.UsedPercent, primary.ResetsAt));
                }
                if (bucket.Secondary is { } secondary)
                {
                    rows.Add(LimitRow(prefix + WindowLabel(secondary.WindowDurationMinutes),
                        secondary.UsedPercent, secondary.ResetsAt));
                }
                if (bucket.IndividualLimit is { } spend)
                {
                    rows.Add(LimitRow(prefix + _localization.PersonalSpendLimit,
                        spend.UsedPercent, spend.ResetsAt, $"{spend.Used} / {spend.Limit}"));
                }
            }

            var credits = status?.Snapshots.Select(static bucket => bucket.Credits)
                .FirstOrDefault(static value => value?.Unlimited == true ||
                    (value?.HasCredits == true && !string.IsNullOrWhiteSpace(value.Balance)));
            CreditsText = credits?.Unlimited == true
                ? $"{_localization.Credits}: ∞"
                : credits?.Balance is { Length: > 0 } balance
                    ? $"{_localization.Credits}: {balance}"
                    : null;
            var plan = status?.RateLimits.PlanType ?? status?.VisibleSnapshots.FirstOrDefault()?.PlanType;
            OfficialLimitsMetadataText = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(plan) ? null : $"{_localization.Plan}: {plan}",
                status?.VisibleSnapshots.Any(static bucket => bucket.RateLimitReachedType is not null) == true
                    ? _localization.LimitReached : null,
                _store.CodexRateLimitsStale ? _localization.Stale : null,
            }.Where(static value => value is not null)!);
            if (OfficialLimitsMetadataText.Length == 0) OfficialLimitsMetadataText = null;
        }
        else if (selectedProviderId == "claude_code")
        {
            var status = _store.ClaudeRateLimits;
            primaryUsed = status?.FiveHour?.UsedPercent;
            secondaryUsed = status?.SevenDay?.UsedPercent;
            primaryReset = status?.FiveHour?.ResetsAt;
            secondaryReset = status?.SevenDay?.ResetsAt;
            OfficialLimitsMetadataText = string.Join(
                " · ",
                new[] { status?.PlanDisplay, status?.AccountDisplay }
                    .Where(static value => !string.IsNullOrWhiteSpace(value))!);
            if (OfficialLimitsMetadataText.Length == 0)
            {
                OfficialLimitsMetadataText = null;
            }
            if (status?.FiveHour is { } fiveHour)
            {
                rows.Add(LimitRow(_localization.FiveHourSession, fiveHour.UsedPercent, fiveHour.ResetsAt));
            }
            if (status?.SevenDay is { } weekly)
            {
                rows.Add(LimitRow(_localization.Weekly, weekly.UsedPercent, weekly.ResetsAt));
            }
            if (_store.ClaudeBurnPerMinute is double burn && burn > 0 && double.IsFinite(burn))
            {
                BurnRateText = $"{_localization.BurnRate}: {UsageValueFormatter.Compact((long)Math.Round(burn))}/min";
            }
            if (status?.FiveHour is not null || _store.ClaudeBurnPerMinute is not null)
            {
                var forecast = _store.ClaudeFiveHourForecast;
                ForecastText = forecast?.BeforeReset == true
                    ? $"{_localization.Forecast}: {forecast.DepletionTime.ToLocalTime():HH:mm}"
                    : $"{_localization.Forecast}: {_localization.NoProjection}";
            }
        }
        else if (selectedProviderId == "antigravity")
        {
            AntigravityLimitRows = _store.AntigravityRateLimits?.Groups
                .SelectMany(group => group.Buckets.Select(bucket => new OfficialLimitRow(
                    $"{group.DisplayName} · {bucket.DisplayName}",
                    DisplayPercent(bucket.UsedPercent, _limitDisplayMode),
                    LimitText(bucket.UsedPercent),
                    FormatReset(bucket.ResetsAt))))
                .ToArray() ?? Array.Empty<OfficialLimitRow>();
            rows.AddRange(AntigravityLimitRows);
        }

        OfficialLimitRows = rows;

        HasFiveHourLimit = primaryUsed is not null;
        FiveHourRemainingPercent = DisplayPercent(primaryUsed, _limitDisplayMode);
        FiveHourRemainingText = primaryUsed is null ? null : LimitText(primaryUsed.Value);
        FiveHourResetText = FormatReset(primaryReset);

        HasWeeklyLimit = secondaryUsed is not null;
        WeeklyRemainingPercent = DisplayPercent(secondaryUsed, _limitDisplayMode);
        WeeklyRemainingText = secondaryUsed is null ? null : LimitText(secondaryUsed.Value);
        WeeklyResetText = FormatReset(secondaryReset);
        HasCodexRateLimits = HasOfficialLimitRows || CreditsText is not null || HasBurnForecast;
    }

    private OfficialLimitRow LimitRow(
        string label,
        double usedPercent,
        DateTimeOffset? reset,
        string? detail = null) =>
        new(label, DisplayPercent(usedPercent, _limitDisplayMode), LimitText(usedPercent),
            FormatReset(reset), detail);

    private string WindowLabel(int? minutes) => minutes switch
    {
        300 => _localization.FiveHourSession,
        10_080 => _localization.Weekly,
        int value when value >= 60 && value % 60 == 0 => _localization.HourWindow(value / 60),
        int value => _localization.MinuteWindow(value),
        null => _localization.Limit,
    };

    private static string BucketLabel(CodexRateLimitSnapshot snapshot)
    {
        var raw = snapshot.LimitName ?? snapshot.LimitId ?? "Codex";
        var spaced = raw.Replace('_', ' ');
        return spaced.Length == 0 ? "Codex" : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private bool IsOfficialLimitsStale(string providerId) => providerId switch
    {
        "codex" => _store.CodexRateLimitsStale,
        "claude_code" => _store.ClaudeRateLimitsStale,
        "antigravity" => _store.AntigravityRateLimitsStale,
        _ => false,
    };

    private string LimitText(double usedPercent)
    {
        var suffix = _limitDisplayMode == LimitDisplayMode.Remaining
            ? _localization.Remaining
            : _localization.Used;
        if (_localization.Language == AppLanguage.En) suffix = suffix.ToLowerInvariant();
        return $"{DisplayPercent(usedPercent, _limitDisplayMode)}% {suffix}";
    }

    private string? FormatReset(DateTimeOffset? timestamp)
    {
        if (timestamp is not DateTimeOffset value)
        {
            return null;
        }

        var remaining = value.ToUniversalTime() - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return _localization.ResetDue;
        }

        if (remaining < TimeSpan.FromMinutes(1))
        {
            return _localization.ResetsIn("<1m");
        }

        if (remaining >= TimeSpan.FromDays(1))
        {
            return _localization.ResetsIn($"{(int)remaining.TotalDays}d {remaining.Hours}h");
        }

        if (remaining >= TimeSpan.FromHours(1))
        {
            return _localization.ResetsIn($"{(int)remaining.TotalHours}h {remaining.Minutes}m");
        }

        return _localization.ResetsIn($"{(int)remaining.TotalMinutes}m");
    }

    private string? FormatRelative(DateTimeOffset? timestamp)
    {
        if (timestamp is not DateTimeOffset value)
        {
            return null;
        }

        var elapsed = _timeProvider.GetUtcNow() - value.ToUniversalTime();
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return _localization.JustNow;
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return _localization.MinutesAgo(minutes);
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return _localization.HoursAgo(hours);
        }

        var days = (int)elapsed.TotalDays;
        return _localization.DaysAgo(days);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class RefreshCompletedEventArgs(
    bool scheduleEmptyRetry,
    bool succeeded) : EventArgs
{
    public bool ScheduleEmptyRetry { get; } = scheduleEmptyRetry;

    public bool Succeeded { get; } = succeeded;
}
