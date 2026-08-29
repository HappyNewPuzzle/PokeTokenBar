using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App.Lifecycle;

public sealed class InitialCompanionController : IDisposable
{
    private readonly CompanionViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private int _started;
    private bool _disposed;

    public InitialCompanionController(CompanionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool HasStarted => Volatile.Read(ref _started) != 0;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token,
            cancellationToken);
        await _viewModel.InitializeAsync(linked.Token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
