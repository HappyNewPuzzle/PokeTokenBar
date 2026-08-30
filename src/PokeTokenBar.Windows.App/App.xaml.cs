using System.Windows;
using PokeTokenBar.Windows.App.FloatingPet;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Tray;

namespace PokeTokenBar.Windows.App;

public partial class App : System.Windows.Application
{
    private ApplicationComposition? _composition;
    private SystemTrayController? _trayController;
    private FloatingPetController? _floatingPet;
    private InitialRefreshController? _initialRefresh;
    private InitialCompanionController? _initialCompanion;
    private PowerLifecycleController? _powerLifecycle;

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

        _floatingPet = new FloatingPetController(
            new FloatingPokemonWindow(_composition.FloatingPet),
            viewModel.Settings,
            () =>
            {
                if (_trayController is not null)
                {
                    _trayController.ShowWindow();
                }
                else
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                }
            });
        _floatingPet.Start();

        try
        {
            _powerLifecycle = new PowerLifecycleController(
                new WindowsPowerModeEventSource(),
                viewModel.Usage.RefreshAsync,
                viewModel.Companion.RefreshAsync,
                _floatingPet.SetDisplayAwake,
                action => Dispatcher.BeginInvoke(action));
        }
        catch (Exception)
        {
            // Power notifications are an optional lifecycle optimization; the tray app remains usable.
        }

        _initialRefresh = new InitialRefreshController(viewModel.Usage);
        _initialCompanion = new InitialCompanionController(viewModel.Companion);
        _ = _initialRefresh.StartAsync();
        _ = _initialCompanion.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _powerLifecycle?.Dispose();
        _trayController?.Dispose();
        _floatingPet?.Dispose();
        _initialCompanion?.Dispose();
        (MainWindow as IDisposable)?.Dispose();
        _composition?.Dispose();
        base.OnExit(e);
    }
}
