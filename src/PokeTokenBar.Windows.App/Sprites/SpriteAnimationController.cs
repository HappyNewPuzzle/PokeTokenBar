using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace PokeTokenBar.Windows.App.Sprites;

internal sealed class SpriteAnimationController : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromMilliseconds(100);
    private readonly ISpriteAnimationTimerFactory _timerFactory;
    private ISpriteAnimationTimer? _timer;
    private PokemonSpritePresentation? _presentation;
    private BitmapSource? _currentImage;
    private int _frameIndex;
    private long _generation;
    private bool _isActive;
    private bool _isAnimating;
    private bool _disposed;
    private TimeSpan _minimumFrameDuration = DefaultFrameDuration;

    public SpriteAnimationController(ISpriteAnimationTimerFactory timerFactory)
    {
        _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (ReferenceEquals(_currentImage, value))
            {
                return;
            }

            _currentImage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnimating
    {
        get => _isAnimating;
        private set
        {
            if (_isAnimating == value)
            {
                return;
            }

            _isAnimating = value;
            OnPropertyChanged();
        }
    }

    public void SetPresentation(PokemonSpritePresentation? presentation)
    {
        if (_disposed || ReferenceEquals(_presentation, presentation))
        {
            return;
        }

        StopTimer();
        _presentation = presentation;
        ResetToFirstFrame();
        if (_isActive)
        {
            StartTimerIfAnimated();
        }
    }

    public void SetActive(bool active)
    {
        if (_disposed || _isActive == active)
        {
            return;
        }

        _isActive = active;
        StopTimer();
        ResetToFirstFrame();
        if (active)
        {
            StartTimerIfAnimated();
        }
    }

    public void SetMinimumFrameDuration(TimeSpan duration)
    {
        duration = duration > TimeSpan.Zero ? duration : DefaultFrameDuration;
        if (_disposed || duration == _minimumFrameDuration) return;
        _minimumFrameDuration = duration;
        if (_timer is not null && _presentation is { Frames.Count: > 0 })
        {
            _timer.Interval = Normalize(_presentation.Frames[_frameIndex].Duration);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isActive = false;
        StopTimer();
    }

    private void ResetToFirstFrame()
    {
        _frameIndex = 0;
        CurrentImage = _presentation is { IsAnimated: true, Frames.Count: > 0 }
            ? _presentation.Frames[0].Image
            : _presentation?.StaticImage;
    }

    private void StartTimerIfAnimated()
    {
        if (_presentation is not { IsAnimated: true, Frames.Count: >= 2 })
        {
            return;
        }

        var generation = ++_generation;
        _timer = _timerFactory.Create(
            Normalize(_presentation.Frames[_frameIndex].Duration),
            (_, _) => Advance(generation));
        _timer.Start();
        IsAnimating = true;
    }

    private void Advance(long generation)
    {
        if (_disposed || !_isActive || generation != _generation ||
            _presentation is not { IsAnimated: true, Frames.Count: >= 2 } presentation)
        {
            return;
        }

        var next = _frameIndex + 1;
        if (next >= presentation.Frames.Count)
        {
            if (!presentation.LoopsContinuously)
            {
                StopTimer();
                return;
            }

            next = 0;
        }

        _frameIndex = next;
        CurrentImage = presentation.Frames[next].Image;
        if (_timer is not null)
        {
            _timer.Interval = Normalize(presentation.Frames[next].Duration);
        }
    }

    private void StopTimer()
    {
        _generation++;
        var timer = Interlocked.Exchange(ref _timer, null);
        timer?.Stop();
        timer?.Dispose();
        IsAnimating = false;
    }

    private TimeSpan Normalize(TimeSpan duration) =>
        duration > _minimumFrameDuration ? duration : _minimumFrameDuration;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
