using System.ComponentModel;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App.FloatingPet;

internal sealed class FloatingPetController : IDisposable
{
    private readonly IFloatingPetWindow _window;
    private readonly SettingsViewModel _settings;
    private readonly Action _openPopup;
    private bool _started;
    private bool _displayAwake = true;
    private bool _disposed;

    public FloatingPetController(
        IFloatingPetWindow window,
        SettingsViewModel settings,
        Action openPopup)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openPopup = openPopup ?? throw new ArgumentNullException(nameof(openPopup));
        _window.OpenRequested += OnOpenRequested;
        _window.HideRequested += OnHideRequested;
        _window.PositionCommitted += OnPositionCommitted;
        _settings.PropertyChanged += OnSettingsChanged;
        _settings.FloatingPetPositionResetRequested += OnPositionResetRequested;
    }

    public bool HasStarted => _started;

    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        SyncVisibility();
    }

    public void SetDisplayAwake(bool awake)
    {
        if (_disposed || _displayAwake == awake)
        {
            return;
        }

        _displayAwake = awake;
        SyncVisibility();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.OpenRequested -= OnOpenRequested;
        _window.HideRequested -= OnHideRequested;
        _window.PositionCommitted -= OnPositionCommitted;
        _settings.PropertyChanged -= OnSettingsChanged;
        _settings.FloatingPetPositionResetRequested -= OnPositionResetRequested;
        _window.Close();
        _window.Dispose();
    }

    private void SyncVisibility()
    {
        if (!_started || _disposed)
        {
            return;
        }

        if (_settings.IsFloatingPetEnabled && _displayAwake)
        {
            if (!_window.IsVisible)
            {
                _window.ShowAtPosition(_settings.SavedFloatingPetPosition);
            }
        }
        else
        {
            _window.Hide();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.IsFloatingPetEnabled))
        {
            SyncVisibility();
        }
    }

    private void OnOpenRequested(object? sender, EventArgs args) => _openPopup();

    private void OnHideRequested(object? sender, EventArgs args) =>
        _settings.IsFloatingPetEnabled = false;

    private void OnPositionCommitted(object? sender, FloatingPetPositionEventArgs args) =>
        _settings.SaveFloatingPetPosition(args.Position.Left, args.Position.Top);

    private void OnPositionResetRequested(object? sender, EventArgs args)
    {
        if (_settings.IsFloatingPetEnabled)
        {
            _window.ResetToDefaultPosition();
        }
    }
}
