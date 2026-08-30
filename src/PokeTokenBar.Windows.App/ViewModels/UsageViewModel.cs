using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.App.Formatting;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class UsageViewModel : INotifyPropertyChanged
{
    private readonly UsageStore _store;
    private readonly TimeProvider _timeProvider;

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
    private int _fiveHourLimitPercent;
    private string? _fiveHourLimitText;
    private string? _fiveHourResetText;
    private bool _hasWeeklyLimit;
    private int _weeklyLimitPercent;
    private string? _weeklyLimitText;
    private string? _weeklyResetText;

    public UsageViewModel(
        UsageStore store,
        string? preferredProviderId = null,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _preferredProviderId = preferredProviderId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => !IsRefreshing,
            exception => ErrorMessage = exception.Message);
        ApplyStoreState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        private set => SetField(ref _providerName, value);
    }

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

    public int FiveHourLimitPercent
    {
        get => _fiveHourLimitPercent;
        private set => SetField(ref _fiveHourLimitPercent, value);
    }

    public string? FiveHourLimitText
    {
        get => _fiveHourLimitText;
        private set => SetField(ref _fiveHourLimitText, value);
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

    public int WeeklyLimitPercent
    {
        get => _weeklyLimitPercent;
        private set => SetField(ref _weeklyLimitPercent, value);
    }

    public string? WeeklyLimitText
    {
        get => _weeklyLimitText;
        private set => SetField(ref _weeklyLimitText, value);
    }

    public string? WeeklyResetText
    {
        get => _weeklyResetText;
        private set => SetField(ref _weeklyResetText, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        Exception? refreshError = null;
        try
        {
            await _store.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
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
        }
    }

    private void ApplyStoreState()
    {
        Providers = new ReadOnlyCollection<ProviderSnapshot>(_store.Snapshots.ToArray());
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
        ApplyOfficialLimits(selected?.ProviderId);
    }

    private void ApplyOfficialLimits(string? selectedProviderId)
    {
        var status = selectedProviderId == "codex" ? _store.CodexRateLimits : null;
        var snapshot = status?.RateLimits.HasVisibleLimit == true
            ? status.RateLimits
            : status?.VisibleSnapshots.FirstOrDefault();
        var primary = snapshot?.Primary;
        var secondary = snapshot?.Secondary;

        HasFiveHourLimit = primary is not null;
        FiveHourLimitPercent = Math.Clamp(primary?.UsedPercent ?? 0, 0, 100);
        FiveHourLimitText = primary is null ? null : $"{primary.UsedPercent}%";
        FiveHourResetText = FormatReset(primary?.ResetsAt);

        HasWeeklyLimit = secondary is not null;
        WeeklyLimitPercent = Math.Clamp(secondary?.UsedPercent ?? 0, 0, 100);
        WeeklyLimitText = secondary is null ? null : $"{secondary.UsedPercent}%";
        WeeklyResetText = FormatReset(secondary?.ResetsAt);
        HasCodexRateLimits = HasFiveHourLimit || HasWeeklyLimit;
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
            return "Reset due";
        }

        if (remaining < TimeSpan.FromMinutes(1))
        {
            return "Resets in <1m";
        }

        if (remaining >= TimeSpan.FromDays(1))
        {
            return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        if (remaining >= TimeSpan.FromHours(1))
        {
            return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"Resets in {(int)remaining.TotalMinutes}m";
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
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "1 day ago" : $"{days} days ago";
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
