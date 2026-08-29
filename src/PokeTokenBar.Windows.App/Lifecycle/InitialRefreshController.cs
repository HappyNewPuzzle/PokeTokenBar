using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App.Lifecycle;

public sealed class InitialRefreshController
{
    private readonly UsageViewModel _viewModel;
    private int _started;

    public InitialRefreshController(UsageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool HasStarted => Volatile.Read(ref _started) != 0;

    public Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return _viewModel.RefreshCommand.ExecuteAsync();
    }
}
