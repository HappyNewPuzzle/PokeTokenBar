namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class MainViewModel : IDisposable
{
    private bool _disposed;

    public MainViewModel(UsageViewModel usage, CompanionViewModel companion)
    {
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
        Companion = companion ?? throw new ArgumentNullException(nameof(companion));
    }

    public UsageViewModel Usage { get; }

    public CompanionViewModel Companion { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Companion.Dispose();
    }
}
