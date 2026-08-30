namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class PowerLifecycleController : IDisposable
{
    private readonly IPowerModeEventSource _events;
    private readonly Func<CancellationToken, Task> _refreshUsage;
    private readonly Func<CancellationToken, Task> _refreshCompanion;
    private readonly Action<bool> _setDisplayAwake;
    private readonly Action<Action> _dispatch;
    private readonly object _sync = new();
    private CancellationTokenSource? _recoveryCancellation;
    private Task _recoveryTask = Task.CompletedTask;
    private bool _suspended;
    private bool _disposed;

    public PowerLifecycleController(
        IPowerModeEventSource events,
        Func<CancellationToken, Task> refreshUsage,
        Func<CancellationToken, Task> refreshCompanion,
        Action<bool> setDisplayAwake,
        Action<Action> dispatch)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _refreshUsage = refreshUsage ?? throw new ArgumentNullException(nameof(refreshUsage));
        _refreshCompanion = refreshCompanion ?? throw new ArgumentNullException(nameof(refreshCompanion));
        _setDisplayAwake = setDisplayAwake ?? throw new ArgumentNullException(nameof(setDisplayAwake));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _events.Suspending += OnSuspending;
        _events.Resumed += OnResumed;
    }

    internal Task RecoveryTask
    {
        get
        {
            lock (_sync)
            {
                return _recoveryTask;
            }
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
            cancellation = _recoveryCancellation;
            _recoveryCancellation = null;
        }

        _events.Suspending -= OnSuspending;
        _events.Resumed -= OnResumed;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _events.Dispose();
    }

    private void OnSuspending(object? sender, EventArgs args) => _dispatch(Suspend);

    private void OnResumed(object? sender, EventArgs args) => _dispatch(Resume);

    private void Suspend()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed || _suspended)
            {
                return;
            }

            _suspended = true;
            cancellation = _recoveryCancellation;
            _recoveryCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        _setDisplayAwake(false);
    }

    private void Resume()
    {
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            if (_disposed || !_suspended)
            {
                return;
            }

            _suspended = false;
            cancellation = new CancellationTokenSource();
            _recoveryCancellation = cancellation;
        }

        _setDisplayAwake(true);
        var recovery = RecoverAsync(cancellation);
        lock (_sync)
        {
            _recoveryTask = recovery;
        }
    }

    private async Task RecoverAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.WhenAll(
                    _refreshUsage(cancellation.Token),
                    _refreshCompanion(cancellation.Token))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // A resume refresh failure must not tear down the tray application.
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_recoveryCancellation, cancellation))
                {
                    _recoveryCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }
}
