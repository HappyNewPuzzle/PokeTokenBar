using System.Windows;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Tray;

namespace PokeTokenBar.Windows.App;

public partial class App : System.Windows.Application
{
    private ApplicationComposition? _composition;
    private SystemTrayController? _trayController;
    private InitialRefreshController? _initialRefresh;
    private InitialCompanionController? _initialCompanion;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _composition = AppComposition.CreateApplication();
        var viewModel = _composition.ViewModel;
        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;

        try
        {
            _trayController = new SystemTrayController(
                new NotifyIconTrayIcon(),
                new WpfTrayWindow(mainWindow),
                viewModel.Usage,
                Shutdown);
        }
        catch (Exception)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }

        _initialRefresh = new InitialRefreshController(viewModel.Usage);
        _initialCompanion = new InitialCompanionController(viewModel.Companion);
        _ = _initialRefresh.StartAsync();
        _ = _initialCompanion.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayController?.Dispose();
        _initialCompanion?.Dispose();
        (MainWindow as IDisposable)?.Dispose();
        _composition?.Dispose();
        base.OnExit(e);
    }
}
