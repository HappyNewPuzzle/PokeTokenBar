using System.Windows.Threading;

namespace PokeTokenBar.Windows.App.Sprites;

internal interface ISpriteAnimationTimerFactory
{
    ISpriteAnimationTimer Create(TimeSpan interval, EventHandler tick);
}

internal interface ISpriteAnimationTimer : IDisposable
{
    TimeSpan Interval { get; set; }

    void Start();

    void Stop();
}

internal sealed class DispatcherSpriteAnimationTimerFactory(Dispatcher dispatcher)
    : ISpriteAnimationTimerFactory
{
    private readonly Dispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public ISpriteAnimationTimer Create(TimeSpan interval, EventHandler tick) =>
        new DispatcherSpriteAnimationTimer(_dispatcher, interval, tick);
}

internal sealed class DispatcherSpriteAnimationTimer : ISpriteAnimationTimer
{
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _tick;
    private bool _disposed;

    public DispatcherSpriteAnimationTimer(
        Dispatcher dispatcher,
        TimeSpan interval,
        EventHandler tick)
    {
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
        {
            Interval = interval,
        };
        _timer.Tick += _tick;
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public void Start()
    {
        if (!_disposed)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= _tick;
    }
}
