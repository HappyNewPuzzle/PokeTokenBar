using System.ComponentModel;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IAppSettingsPersistence _persistence;
    private readonly IAutoStartService _autoStart;
    private AppSettings _settings;
    private bool _isFloatingPetEnabled;
    private bool _isLaunchAtStartupEnabled;
    private string? _errorMessage;

    public SettingsViewModel(
        IAppSettingsPersistence persistence,
        IAutoStartService autoStartService)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _autoStart = autoStartService ?? throw new ArgumentNullException(nameof(autoStartService));
        _settings = LoadSettings();
        _isFloatingPetEnabled = _settings.FloatingPetEnabled;
        _isLaunchAtStartupEnabled = ReadAutoStartState();
        _settings = _settings with { LaunchAtStartup = _isLaunchAtStartupEnabled };
        ResetFloatingPetPositionCommand = new AsyncCommand(_ =>
        {
            ResetFloatingPetPosition();
            return Task.CompletedTask;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? FloatingPetPositionResetRequested;

    public bool IsFloatingPetEnabled
    {
        get => _isFloatingPetEnabled;
        set
        {
            if (!SetField(ref _isFloatingPetEnabled, value))
            {
                return;
            }

            _settings = _settings with { FloatingPetEnabled = value };
            SaveSettings();
        }
    }

    public bool IsLaunchAtStartupAvailable => _autoStart.IsAvailable;

    public bool IsLaunchAtStartupEnabled
    {
        get => _isLaunchAtStartupEnabled;
        set
        {
            if (value == _isLaunchAtStartupEnabled)
            {
                return;
            }

            try
            {
                _autoStart.SetEnabled(value);
                var actual = _autoStart.IsEnabled;
                SetField(ref _isLaunchAtStartupEnabled, actual);
                _settings = _settings with { LaunchAtStartup = actual };
                ErrorMessage = actual == value
                    ? null
                    : "Windows did not retain the requested startup setting.";
                SaveSettings();
            }
            catch (Exception exception)
            {
                SetField(ref _isLaunchAtStartupEnabled, ReadAutoStartState());
                _settings = _settings with
                {
                    LaunchAtStartup = _isLaunchAtStartupEnabled,
                };
                ErrorMessage = exception.Message;
            }
        }
    }

    public FloatingPetPosition? SavedFloatingPetPosition =>
        _settings.FloatingPetPosition;

    public AsyncCommand ResetFloatingPetPositionCommand { get; }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    internal void SaveFloatingPetPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        _settings = _settings with
        {
            FloatingPetPosition = new FloatingPetPosition(left, top),
        };
        OnPropertyChanged(nameof(SavedFloatingPetPosition));
        SaveSettings();
    }

    private void ResetFloatingPetPosition()
    {
        _settings = _settings with { FloatingPetPosition = null };
        OnPropertyChanged(nameof(SavedFloatingPetPosition));
        SaveSettings();
        FloatingPetPositionResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return _persistence.Load() ?? AppSettings.Default;
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            return AppSettings.Default;
        }
    }

    private bool ReadAutoStartState()
    {
        try
        {
            return _autoStart.IsEnabled;
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            return false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _persistence.Save(_settings);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
