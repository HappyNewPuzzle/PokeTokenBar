using System.Windows;
using System.Windows.Threading;
using PokeTokenBar.Windows.App.FloatingPet;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.Tray;

namespace PokeTokenBar.Windows.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private ApplicationComposition? _composition;
    private SystemTrayController? _trayController;
    private FloatingPetController? _floatingPet;
    private InitialRefreshController? _initialRefresh;
    private InitialCompanionController? _initialCompanion;
    private PowerLifecycleController? _powerLifecycle;
    private NotificationController? _notifications;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceGuard.TryAcquire();
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

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
                Shutdown,
                viewModel.Settings,
                viewModel.Companion);
            _notifications = new NotificationController(
                viewModel.Usage,
                viewModel.Settings,
                _composition.FloatingPet,
                _trayController,
                _composition.CompanionStore);
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
                action => Dispatcher.BeginInvoke(action),
                _composition.UsagePolling.Pause,
                _composition.UsagePolling.Resume);
        }
        catch (Exception)
        {
            // Power notifications are an optional lifecycle optimization; the tray app remains usable.
        }

        _initialRefresh = new InitialRefreshController(viewModel.Usage);
        _initialCompanion = new InitialCompanionController(viewModel.Companion);
        AppReliability.Run(_initialRefresh.StartAsync());
        _composition.UsagePolling.Start();
        AppReliability.Run(_initialCompanion.StartAsync());
        if (viewModel.Support is { } support)
        {
            AppReliability.Run(support.CheckAsync(TimeSpan.FromMinutes(30)));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _powerLifecycle?.Dispose();
        _notifications?.Dispose();
        _trayController?.Dispose();
        _floatingPet?.Dispose();
        _initialCompanion?.Dispose();
        (MainWindow as IDisposable)?.Dispose();
        _composition?.Dispose();
        _singleInstance?.Dispose();
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        if (AppReliability.IsRecoverableDispatcherException(args.Exception)) args.Handled = true;
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        if (!AppReliability.IsFatal(args.Exception)) args.SetObserved();
    }
}
