using PokeTokenBar.Windows.App.Lifecycle;

namespace PokeTokenBar.Windows.Tests;

public sealed class PowerLifecycleControllerTests
{
    [Fact]
    public async Task SuspendAndResumeGateDisplayAndRefreshUsageAndCompanion()
    {
        var events = new FakePowerEvents();
        var displayStates = new List<bool>();
        var usageCalls = 0;
        var companionCalls = 0;
        var pollingStates = new List<string>();
        using var controller = new PowerLifecycleController(
            events,
            _ => { usageCalls++; return Task.CompletedTask; },
            _ => { companionCalls++; return Task.CompletedTask; },
            displayStates.Add,
            action => action(),
            () => pollingStates.Add("paused"),
            () => pollingStates.Add("resumed"));

        events.Suspend();
        events.Resume();
        await controller.RecoveryTask;

        Assert.Equal([false, true], displayStates);
        Assert.Equal(1, usageCalls);
        Assert.Equal(1, companionCalls);
        Assert.Equal(["paused", "resumed"], pollingStates);
    }

    [Fact]
    public async Task RepeatedResumeWithoutAnotherSuspendDoesNotCreateRefreshStorm()
    {
        var events = new FakePowerEvents();
        var calls = 0;
        using var controller = Create(events, _ => { calls++; return Task.CompletedTask; });

        events.Suspend();
        events.Resume();
        events.Resume();
        events.Resume();
        await controller.RecoveryTask;

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RepeatedResumeRestoresPollingOnlyOnce()
    {
        var events = new FakePowerEvents();
        var pauses = 0;
        var resumes = 0;
        using var controller = new PowerLifecycleController(
            events,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            action => action(),
            () => pauses++,
            () => resumes++);

        events.Suspend();
        events.Resume();
        events.Resume();
        await controller.RecoveryTask;

        Assert.Equal(1, pauses);
        Assert.Equal(1, resumes);
    }

    [Fact]
    public async Task SuspendCancelsRecoveryOwnedWork()
    {
        var events = new FakePowerEvents();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = Create(events, async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        });

        events.Suspend();
        events.Resume();
        events.Suspend();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.RecoveryTask;
    }

    [Fact]
    public async Task RecoveryFailureIsContained()
    {
        var events = new FakePowerEvents();
        using var controller = new PowerLifecycleController(
            events,
            _ => Task.FromException(new IOException("usage failed")),
            _ => Task.FromException(new InvalidOperationException("companion failed")),
            _ => { },
            action => action());

        events.Suspend();
        events.Resume();

        await controller.RecoveryTask;
    }

    [Fact]
    public void DisposeUnsubscribesAndIsIdempotent()
    {
        var events = new FakePowerEvents();
        var displayChanges = 0;
        var controller = new PowerLifecycleController(
            events,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => displayChanges++,
            action => action());

        controller.Dispose();
        controller.Dispose();
        events.Suspend();
        events.Resume();

        Assert.True(events.IsDisposed);
        Assert.Equal(0, displayChanges);
    }

    private static PowerLifecycleController Create(
        FakePowerEvents events,
        Func<CancellationToken, Task> operation) =>
        new(events, operation, operation, _ => { }, action => action());

    private sealed class FakePowerEvents : IPowerModeEventSource
    {
        public event EventHandler? Suspending;
        public event EventHandler? Resumed;
        public bool IsDisposed { get; private set; }
        public void Suspend() => Suspending?.Invoke(this, EventArgs.Empty);
        public void Resume() => Resumed?.Invoke(this, EventArgs.Empty);
        public void Dispose() => IsDisposed = true;
    }
}
