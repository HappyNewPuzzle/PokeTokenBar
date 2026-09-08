using PokeTokenBar.Windows.App.Lifecycle;

namespace PokeTokenBar.Windows.Tests;

public sealed class NetworkReconnectControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InitialStateAndDuplicateEventDoNotRefresh(bool initialAvailability)
    {
        var events = new FakeNetworkEvents(initialAvailability);
        var refreshes = 0;
        using var controller = new NetworkReconnectController(events, () => refreshes++);

        events.Raise(initialAvailability);

        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void OnlineToOfflineDoesNotRefresh()
    {
        var events = new FakeNetworkEvents(true);
        var refreshes = 0;
        using var controller = new NetworkReconnectController(events, () => refreshes++);

        events.Raise(false);
        events.Raise(false);

        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void OfflineToOnlineRefreshesExactlyOnce()
    {
        var events = new FakeNetworkEvents(false);
        var refreshes = 0;
        using var controller = new NetworkReconnectController(events, () => refreshes++);

        events.Raise(true);
        events.Raise(true);

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void RapidFlappingIsSuppressedUntilCooldownExpires()
    {
        var events = new FakeNetworkEvents(false);
        var clock = new FakeTimeProvider();
        var refreshes = 0;
        using var controller = new NetworkReconnectController(events, () => refreshes++, clock);

        events.Raise(true);
        events.Raise(false);
        clock.Advance(NetworkReconnectController.ReconnectCooldown - TimeSpan.FromMilliseconds(1));
        events.Raise(true);
        events.Raise(false);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        events.Raise(true);

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public void ConcurrentOnlineEventsRefreshOnce()
    {
        var events = new FakeNetworkEvents(false);
        var refreshes = 0;
        using var controller = new NetworkReconnectController(
            events,
            () => Interlocked.Increment(ref refreshes),
            new FakeTimeProvider());

        Parallel.For(0, 20, _ => events.Raise(true));

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void DisposeUnsubscribesAndEventsAfterDisposeDoNotRefresh()
    {
        var events = new FakeNetworkEvents(false);
        var refreshes = 0;
        var controller = new NetworkReconnectController(events, () => refreshes++);
        Assert.Equal(1, events.SubscriberCount);

        controller.Dispose();
        controller.Dispose();
        events.Raise(true);

        Assert.True(events.IsDisposed);
        Assert.Equal(0, events.SubscriberCount);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task DisposeWaitsForAcceptedCallbackAndPreventsLaterRefreshes()
    {
        var events = new FakeNetworkEvents(false);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        var controller = new NetworkReconnectController(events, () =>
        {
            Interlocked.Increment(ref refreshes);
            callbackStarted.SetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
        });

        var raise = Task.Run(() => events.Raise(true));
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispose = Task.Run(controller.Dispose);
        await events.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(dispose.IsCompleted);

        releaseCallback.SetResult();
        await Task.WhenAll(raise, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        events.Raise(false);
        events.Raise(true);

        Assert.Equal(1, refreshes);
    }

    private sealed class FakeNetworkEvents(bool isAvailable) : INetworkAvailabilityEventSource
    {
        private Action<bool>? _availabilityChanged;
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable { get; private set; } = isAvailable;
        public bool IsDisposed { get; private set; }
        public Task Disposed => _disposed.Task;
        public int SubscriberCount => _availabilityChanged?.GetInvocationList().Length ?? 0;

        public event Action<bool>? AvailabilityChanged
        {
            add => _availabilityChanged += value;
            remove => _availabilityChanged -= value;
        }

        public void Raise(bool available)
        {
            IsAvailable = available;
            _availabilityChanged?.Invoke(available);
        }

        public void Dispose()
        {
            IsDisposed = true;
            _disposed.TrySetResult();
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan value) => _timestamp += value.Ticks;
    }
}
