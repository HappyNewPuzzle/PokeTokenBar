using Microsoft.Win32;

namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class WindowsPowerModeEventSource : IPowerModeEventSource
{
    private bool _disposed;

    public WindowsPowerModeEventSource() => SystemEvents.PowerModeChanged += OnPowerModeChanged;

    public event EventHandler? Suspending;

    public event EventHandler? Resumed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        switch (args.Mode)
        {
            case PowerModes.Suspend:
                Suspending?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
