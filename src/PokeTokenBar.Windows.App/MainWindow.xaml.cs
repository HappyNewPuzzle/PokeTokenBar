using System.Windows;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _disposed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        Activated += OnActivated;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Activated -= OnActivated;
        CompanionSprite.Dispose();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_viewModel.Support is { } support)
            AppReliability.Run(support.CheckAsync(TimeSpan.FromMinutes(30)));
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
