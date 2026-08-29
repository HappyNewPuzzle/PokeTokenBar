using System.Windows;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

public partial class MainWindow : Window, IDisposable
{
    private bool _disposed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompanionSprite.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
