using System.Windows;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Tray;

namespace PokeTokenBar.Windows.App;

public partial class App : System.Windows.Application
{
    private SystemTrayController? _trayController;
    private InitialRefreshController? _initialRefresh;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var viewModel = AppComposition.CreateUsageViewModel();
        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;

        try
        {
            _trayController = new SystemTrayController(
                new NotifyIconTrayIcon(),
                new WpfTrayWindow(mainWindow),
                viewModel,
                Shutdown);
        }
        catch (Exception)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }

        _initialRefresh = new InitialRefreshController(viewModel);
        _ = _initialRefresh.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayController?.Dispose();
        base.OnExit(e);
    }
}
