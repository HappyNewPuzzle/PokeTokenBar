using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.Tray;

internal sealed class SystemTrayController : IDisposable, INotificationService
{
    private readonly ITrayIcon _trayIcon;
    private readonly ITrayWindow _window;
    private readonly UsageViewModel _viewModel;
    private readonly Action _shutdown;
    private readonly SettingsViewModel? _settings;
    private readonly CompanionViewModel? _companion;
    private readonly SpriteAnimationController _trayAnimation;
    private bool _isExiting;
    private bool _disposed;

    public SystemTrayController(
        ITrayIcon trayIcon,
        ITrayWindow window,
        UsageViewModel viewModel,
        Action shutdown,
        SettingsViewModel? settings = null,
        CompanionViewModel? companion = null,
        ISpriteAnimationTimerFactory? animationTimerFactory = null)
    {
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _settings = settings;
        _companion = companion;
        _trayAnimation = new SpriteAnimationController(
            animationTimerFactory ?? new DispatcherSpriteAnimationTimerFactory(Dispatcher.CurrentDispatcher));

        _trayIcon.ToggleRequested += OnToggleRequested;
        _trayIcon.OpenRequested += OnOpenRequested;
        _trayIcon.RefreshRequested += OnRefreshRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _window.Closing += OnWindowClosing;
        _window.Deactivated += OnWindowDeactivated;
        _viewModel.PropertyChanged += OnPresentationChanged;
        if (_settings is not null) _settings.PropertyChanged += OnPresentationChanged;
        if (_settings is not null) _settings.Localization.PropertyChanged += OnPresentationChanged;
        if (_companion is not null) _companion.PropertyChanged += OnPresentationChanged;
        _trayAnimation.PropertyChanged += OnTrayAnimationChanged;
        _trayAnimation.SetActive(true);
        UpdatePresentation();
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
        _trayAnimation.SetActive(false);
    }

    public void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            _trayAnimation.SetActive(true);
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

    public Task ShowAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { _trayIcon.ShowNotification(message); }
        catch (Exception) { }
        return Task.CompletedTask;
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
        _viewModel.PropertyChanged -= OnPresentationChanged;
        if (_settings is not null) _settings.PropertyChanged -= OnPresentationChanged;
        if (_settings is not null) _settings.Localization.PropertyChanged -= OnPresentationChanged;
        if (_companion is not null) _companion.PropertyChanged -= OnPresentationChanged;
        _trayAnimation.PropertyChanged -= OnTrayAnimationChanged;
        _trayAnimation.Dispose();
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
        _trayAnimation.SetActive(true);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isExiting)
        {
            _window.Hide();
            _trayAnimation.SetActive(true);
        }
    }

    private void OnToggleRequested(object? sender, EventArgs e) => ToggleWindow();

    private void OnOpenRequested(object? sender, EventArgs e) => ShowWindow();

    private void OnRefreshRequested(object? sender, EventArgs e) => Refresh();

    private void OnExitRequested(object? sender, EventArgs e) => Exit();

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs args) => UpdatePresentation();

    private void OnTrayAnimationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SpriteAnimationController.CurrentImage))
            _trayIcon.SetCompanionFrame(_trayAnimation.CurrentImage);
    }

    private void UpdatePresentation()
    {
        var text = _settings?.Localization;
        _trayIcon.SetMenuText(text?.Open ?? "Open", text?.Refresh ?? "Refresh", text?.Exit ?? "Exit");
        _trayIcon.Text = $"PokeTokenBar · {_viewModel.ProviderName ?? text?.NoUsageData ?? "No usage data"} · {text?.Today ?? "Today"} {_viewModel.TotalTodayTokensText}";
        _trayAnimation.SetMinimumFrameDuration((_settings?.SelectedAnimationQuality ?? AnimationQuality.PowerSaver) switch
        {
            AnimationQuality.Smooth => TimeSpan.FromMilliseconds(100),
            AnimationQuality.Balanced => TimeSpan.FromMilliseconds(200),
            _ => TimeSpan.FromMilliseconds(400),
        });
        _trayAnimation.SetPresentation(_companion?.Sprite ?? EggPresentation);
    }

    internal static PokemonSpritePresentation EggPresentation { get; } = new(
        EggFrame(0),
        [new(EggFrame(0), TimeSpan.FromMilliseconds(500)),
         new(EggFrame(-1), TimeSpan.FromMilliseconds(500))],
        true);

    private static BitmapSource EggFrame(double y)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var text = new FormattedText("🥚", CultureInfo.GetCultureInfo("en-US"),
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Emoji"),
                22, System.Windows.Media.Brushes.White, 1);
            drawing.DrawText(text, new System.Windows.Point((32 - text.Width) / 2, y));
        }
        var image = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
