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
        IsVisibleChanged += OnVisibilityChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Activated -= OnActivated;
        IsVisibleChanged -= OnVisibilityChanged;
        CompanionSprite.Dispose();
        DetailSprite.Dispose();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_viewModel.Support is { } support)
            AppReliability.Run(support.CheckAsync(TimeSpan.FromMinutes(30)), "window-update");
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel.Settings.DiscardDifficultyDraft();

    private void OnTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource))
            _viewModel?.Settings.DiscardDifficultyDraft();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
