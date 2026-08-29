using System.Windows;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

public partial class MainWindow : Window
{
    private readonly InitialRefreshController _initialRefresh;

    public MainWindow(UsageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _initialRefresh = new InitialRefreshController(viewModel);
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _initialRefresh.StartAsync();
    }
}
