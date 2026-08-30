using System.Security.Principal;

namespace PokeTokenBar.Windows.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceGuard? TryAcquire()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return TryAcquire($@"Global\PokeTokenBar-{identity.User?.Value ?? Environment.UserName}");
    }

    internal static SingleInstanceGuard? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
