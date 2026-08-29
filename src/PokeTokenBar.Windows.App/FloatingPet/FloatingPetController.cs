namespace PokeTokenBar.Windows.App.FloatingPet;

internal sealed class FloatingPetController : IDisposable
{
    private readonly IFloatingPetWindow _window;
    private bool _started;
    private bool _disposed;

    public FloatingPetController(IFloatingPetWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public bool HasStarted => _started;

    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _window.ShowAtDefaultPosition();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Close();
        _window.Dispose();
    }
}
