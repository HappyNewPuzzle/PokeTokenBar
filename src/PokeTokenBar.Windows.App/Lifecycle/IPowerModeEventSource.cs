namespace PokeTokenBar.Windows.App.Lifecycle;

internal interface IPowerModeEventSource : IDisposable
{
    event EventHandler? Suspending;

    event EventHandler? Resumed;
}
