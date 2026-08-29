using System.ComponentModel;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App.Tray;

internal sealed class SystemTrayController : IDisposable
{
    private readonly ITrayIcon _trayIcon;
    private readonly ITrayWindow _window;
    private readonly UsageViewModel _viewModel;
    private readonly Action _shutdown;
    private bool _isExiting;
    private bool _disposed;

    public SystemTrayController(
        ITrayIcon trayIcon,
        ITrayWindow window,
        UsageViewModel viewModel,
        Action shutdown)
    {
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));

        _trayIcon.ToggleRequested += OnToggleRequested;
        _trayIcon.OpenRequested += OnOpenRequested;
        _trayIcon.RefreshRequested += OnRefreshRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _window.Closing += OnWindowClosing;
        _window.Deactivated += OnWindowDeactivated;
        _trayIcon.Visible = true;
    }

    public bool IsExiting => _isExiting;

    public void ShowWindow()
    {
        if (_window.IsMinimized)
        {
            _window.Restore();
        }

        if (!_window.IsVisible)
        {
            _window.ShowNearTray();
        }

        _window.Activate();
    }

    public void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            return;
        }

        ShowWindow();
    }

    public void Refresh() => _ = _viewModel.RefreshCommand.ExecuteAsync();

    public void Exit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Dispose();
        _window.Close();
        _shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.ToggleRequested -= OnToggleRequested;
        _trayIcon.OpenRequested -= OnOpenRequested;
        _trayIcon.RefreshRequested -= OnRefreshRequested;
        _trayIcon.ExitRequested -= OnExitRequested;
        _window.Closing -= OnWindowClosing;
        _window.Deactivated -= OnWindowDeactivated;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _window.Hide();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isExiting)
        {
            _window.Hide();
        }
    }

    private void OnToggleRequested(object? sender, EventArgs e) => ToggleWindow();

    private void OnOpenRequested(object? sender, EventArgs e) => ShowWindow();

    private void OnRefreshRequested(object? sender, EventArgs e) => Refresh();

    private void OnExitRequested(object? sender, EventArgs e) => Exit();
}
