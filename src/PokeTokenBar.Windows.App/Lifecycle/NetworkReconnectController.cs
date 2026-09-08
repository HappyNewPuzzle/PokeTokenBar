namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class NetworkReconnectController : IDisposable
{
    internal static readonly TimeSpan ReconnectCooldown = TimeSpan.FromSeconds(5);

    private readonly INetworkAvailabilityEventSource _events;
    private readonly Action _requestRefresh;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private bool _isAvailable;
    private long? _lastReconnectTimestamp;
    private int _activeCallbacks;
    private bool _disposed;

    public NetworkReconnectController(
        INetworkAvailabilityEventSource events,
        Action requestRefresh,
        TimeProvider? timeProvider = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _requestRefresh = requestRefresh ?? throw new ArgumentNullException(nameof(requestRefresh));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isAvailable = events.IsAvailable;
        _events.AvailabilityChanged += OnAvailabilityChanged;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _events.AvailabilityChanged -= OnAvailabilityChanged;
        _events.Dispose();

        lock (_sync)
        {
            while (_activeCallbacks != 0)
            {
                Monitor.Wait(_sync);
            }
        }
    }

    private void OnAvailabilityChanged(bool isAvailable)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var wasAvailable = _isAvailable;
            _isAvailable = isAvailable;
            if (wasAvailable || !isAvailable)
            {
                return;
            }

            var now = _timeProvider.GetTimestamp();
            if (_lastReconnectTimestamp is { } last &&
                _timeProvider.GetElapsedTime(last, now) < ReconnectCooldown)
            {
                return;
            }

            _lastReconnectTimestamp = now;
            _activeCallbacks++;
        }

        try
        {
            _requestRefresh();
        }
        catch (Exception)
        {
            // Reconnect refresh is best effort; the normal polling cycle remains authoritative.
        }
        finally
        {
            lock (_sync)
            {
                _activeCallbacks--;
                if (_activeCallbacks == 0)
                {
                    Monitor.PulseAll(_sync);
                }
            }
        }
    }
}
