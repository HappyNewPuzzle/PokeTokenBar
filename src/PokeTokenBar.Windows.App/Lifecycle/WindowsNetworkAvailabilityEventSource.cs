using System.Net.NetworkInformation;

namespace PokeTokenBar.Windows.App.Lifecycle;

internal interface INetworkAvailabilityEventSource : IDisposable
{
    bool IsAvailable { get; }

    event Action<bool>? AvailabilityChanged;
}

internal sealed class WindowsNetworkAvailabilityEventSource : INetworkAvailabilityEventSource
{
    private int _disposed;

    public WindowsNetworkAvailabilityEventSource()
    {
        IsAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
    }

    public bool IsAvailable { get; private set; }

    public event Action<bool>? AvailabilityChanged;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            IsAvailable = args.IsAvailable;
            AvailabilityChanged?.Invoke(args.IsAvailable);
        }
    }
}
