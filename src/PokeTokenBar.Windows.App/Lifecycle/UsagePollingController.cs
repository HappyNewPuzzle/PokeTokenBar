using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class UsagePollingController : IDisposable
{
    internal static readonly TimeSpan EmptyRetryDelay = TimeSpan.FromSeconds(20);

    private readonly UsageViewModel _usage;
    private readonly SettingsViewModel _settings;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Func<Task>, Task> _dispatchAsync;
    private readonly object _sync = new();
    private ITimer? _pollTimer;
    private ITimer? _emptyRetryTimer;
    private CancellationTokenSource? _refreshCancellation;
    private bool _started;
    private bool _paused;
    private bool _disposed;

    public UsagePollingController(
        UsageViewModel usage,
        SettingsViewModel settings,
        TimeProvider? timeProvider = null,
        Func<Func<Task>, Task>? dispatchAsync = null)
    {
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatchAsync = dispatchAsync ?? (operation => operation());
        _usage.RefreshCompleted += OnRefreshCompleted;
        _settings.RefreshIntervalChanged += Reschedule;
    }

    internal bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
            ReplacePollingTimer();
        }

        EvaluateEmptyRetry(schedule: true);
    }

    public void Pause()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed || _paused)
            {
                return;
            }

            _paused = true;
            DisposeTimer(ref _pollTimer);
            DisposeTimer(ref _emptyRetryTimer);
            cancellation = _refreshCancellation;
            _refreshCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_disposed || !_paused)
            {
                return;
            }

            _paused = false;
            if (_started)
            {
                ReplacePollingTimer();
            }
        }
    }

    public void Reschedule(RefreshIntervalMode interval)
    {
        lock (_sync)
        {
            if (_disposed || !_started || _paused)
            {
                return;
            }

            ReplacePollingTimer(interval);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeTimer(ref _pollTimer);
            DisposeTimer(ref _emptyRetryTimer);
            cancellation = _refreshCancellation;
            _refreshCancellation = null;
        }

        _usage.RefreshCompleted -= OnRefreshCompleted;
        _settings.RefreshIntervalChanged -= Reschedule;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ReplacePollingTimer() => ReplacePollingTimer(_settings.SelectedRefreshInterval);

    private void ReplacePollingTimer(RefreshIntervalMode interval)
    {
        DisposeTimer(ref _pollTimer);
        if (interval == RefreshIntervalMode.Manual)
        {
            return;
        }

        var period = TimeSpan.FromSeconds((int)interval);
        _pollTimer = _timeProvider.CreateTimer(
            static state => ((UsagePollingController)state!).StartBackgroundRefresh(scheduleEmptyRetry: true),
            this,
            period,
            period);
    }

    private void OnRefreshCompleted(object? sender, RefreshCompletedEventArgs args) =>
        EvaluateEmptyRetry(args.ScheduleEmptyRetry);

    private void EvaluateEmptyRetry(bool schedule)
    {
        lock (_sync)
        {
            DisposeTimer(ref _emptyRetryTimer);
            if (!schedule || !_started || _paused || _disposed ||
                _usage.IsRefreshing || _usage.LastUpdated is null ||
                _usage.Providers.Count != 0 || _usage.ErrorMessage is not null)
            {
                return;
            }

            _emptyRetryTimer = _timeProvider.CreateTimer(
                static state => ((UsagePollingController)state!).StartEmptyRetry(),
                this,
                EmptyRetryDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StartEmptyRetry()
    {
        lock (_sync)
        {
            DisposeTimer(ref _emptyRetryTimer);
        }

        StartBackgroundRefresh(scheduleEmptyRetry: false);
    }

    private void StartBackgroundRefresh(bool scheduleEmptyRetry)
    {
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            if (_disposed || _paused || _refreshCancellation is not null)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _refreshCancellation = cancellation;
        }

        _ = RefreshSafelyAsync(scheduleEmptyRetry, cancellation);
    }

    private async Task RefreshSafelyAsync(
        bool scheduleEmptyRetry,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _dispatchAsync(() => _usage.RefreshAsync(scheduleEmptyRetry, cancellation.Token))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Polling is best effort; the view model retains the refresh error for the UI.
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_refreshCancellation, cancellation))
                {
                    _refreshCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private static void DisposeTimer(ref ITimer? timer)
    {
        timer?.Dispose();
        timer = null;
    }
}
