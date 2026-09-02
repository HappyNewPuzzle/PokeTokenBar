using System.ComponentModel;
using PokeTokenBar.Windows.App.Tray;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using System.Windows.Media.Imaging;

namespace PokeTokenBar.Windows.Tests;

public sealed class SystemTrayControllerTests
{
    [Fact]
    public void Construction_ShowsTrayIconWithoutShowingWindow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow();

        using var controller = CreateController(tray, window);

        Assert.True(tray.Visible);
        Assert.False(window.IsVisible);
        Assert.Equal(0, window.ShowNearTrayCalls);
    }

    [Fact]
    public void FirstToggleRequest_ShowsPositionsAndActivatesTheSameWindow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow();
        using var controller = CreateController(tray, window);

        tray.RequestToggle();

        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowNearTrayCalls);
        Assert.Equal(1, window.ActivateCalls);
    }

    [Fact]
    public void SecondToggleRequest_HidesVisibleWindow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow();
        using var controller = CreateController(tray, window);

        tray.RequestToggle();
        tray.RequestToggle();

        Assert.False(window.IsVisible);
        Assert.Equal(1, window.ShowNearTrayCalls);
        Assert.Equal(1, window.HideCalls);
        Assert.Equal(1, window.ActivateCalls);
    }

    [Fact]
    public void OpenMenuRequest_DoesNotToggleAnAlreadyVisibleWindow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow { IsVisible = true };
        using var controller = CreateController(tray, window);

        tray.RequestOpen();

        Assert.True(window.IsVisible);
        Assert.Equal(0, window.ShowNearTrayCalls);
        Assert.Equal(0, window.HideCalls);
        Assert.Equal(1, window.ActivateCalls);
    }

    [Fact]
    public void ToggleRequest_DoesNotImplicitlyRefreshUsage()
    {
        var provider = new TestUsageProvider();
        var tray = new FakeTrayIcon();
        using var controller = new SystemTrayController(
            tray,
            new FakeTrayWindow(),
            new UsageViewModel(new UsageStore([provider])),
            () => { });

        tray.RequestToggle();

        Assert.Equal(0, provider.DailyCalls);
    }

    [Fact]
    public void OpenRequest_RestoresMinimizedWindowBeforeActivation()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow
        {
            IsVisible = true,
            IsMinimized = true,
        };
        using var controller = CreateController(tray, window);

        tray.RequestOpen();

        Assert.False(window.IsMinimized);
        Assert.Equal(1, window.RestoreCalls);
        Assert.Equal(0, window.ShowNearTrayCalls);
        Assert.Equal(1, window.ActivateCalls);
    }

    [Fact]
    public void WindowDeactivation_HidesTransientPopupWithoutShutdown()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow { IsVisible = true };
        var shutdownCalls = 0;
        using var controller = CreateController(tray, window, () => shutdownCalls++);

        window.RequestDeactivation();

        Assert.False(window.IsVisible);
        Assert.Equal(1, window.HideCalls);
        Assert.Equal(0, shutdownCalls);
        Assert.True(tray.Visible);
    }

    [Fact]
    public void WindowClose_HidesWindowAndKeepsApplicationRunning()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow { IsVisible = true };
        var shutdownCalls = 0;
        using var controller = CreateController(tray, window, () => shutdownCalls++);

        var cancelled = window.RequestUserClose();

        Assert.True(cancelled);
        Assert.False(window.IsVisible);
        Assert.Equal(1, window.HideCalls);
        Assert.Equal(0, shutdownCalls);
        Assert.True(tray.Visible);
        Assert.False(controller.IsExiting);
    }

    [Fact]
    public void ExitRequest_ClosesWindowDisposesIconAndShutsDownExactlyOnce()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow { IsVisible = true };
        var shutdownCalls = 0;
        using var controller = CreateController(tray, window, () => shutdownCalls++);

        tray.RequestExit();
        controller.Exit();
        window.RequestDeactivation();

        Assert.True(controller.IsExiting);
        Assert.False(tray.Visible);
        Assert.Equal(1, tray.DisposeCalls);
        Assert.Equal(1, window.CloseCalls);
        Assert.Equal(0, window.HideCalls);
        Assert.Equal(1, shutdownCalls);
    }

    [Fact]
    public void Dispose_IsIdempotentAndStopsTrayEvents()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeTrayWindow();
        var controller = CreateController(tray, window);

        controller.Dispose();
        controller.Dispose();
        tray.RequestOpen();
        tray.RequestRefresh();
        tray.RequestExit();

        Assert.Equal(1, tray.DisposeCalls);
        Assert.False(tray.Visible);
        Assert.Equal(0, window.ShowNearTrayCalls);
        Assert.Equal(0, window.CloseCalls);
    }

    [Fact]
    public async Task RefreshRequest_ExecutesTheExistingViewModelCommand()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestUsageProvider(async cancellationToken =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new DailyUsage("2026-08-29", 80, 10, 0, 40, 130, 0);
        });
        var viewModel = new UsageViewModel(new UsageStore([provider]));
        var tray = new FakeTrayIcon();
        using var controller = new SystemTrayController(
            tray,
            new FakeTrayWindow(),
            viewModel,
            () => { });

        tray.RequestRefresh();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(viewModel.RefreshCommand.IsExecuting);

        release.SetResult();
        await WaitUntilAsync(
            () => !viewModel.RefreshCommand.IsExecuting,
            TimeSpan.FromSeconds(3));

        Assert.Equal(1, provider.DailyCalls);
        Assert.Equal(130, viewModel.TodayTokens);
    }

    [Fact]
    public async Task RepeatedRefreshRequests_DoNotOverlapExistingCommandExecution()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestUsageProvider(async cancellationToken =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new DailyUsage("2026-08-29", 10, 0, 0, 0, 10, 0);
        });
        var tray = new FakeTrayIcon();
        var viewModel = new UsageViewModel(new UsageStore([provider]));
        using var controller = new SystemTrayController(
            tray,
            new FakeTrayWindow(),
            viewModel,
            () => { });

        tray.RequestRefresh();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        tray.RequestRefresh();
        release.SetResult();
        await WaitUntilAsync(
            () => !viewModel.RefreshCommand.IsExecuting,
            TimeSpan.FromSeconds(3));

        Assert.Equal(1, provider.DailyCalls);
    }

    [Fact]
    public void PresentationFailureDoesNotTearDownTrayLifecycle()
    {
        var tray = new FakeTrayIcon { ThrowOnPresentation = true };
        var window = new FakeTrayWindow();

        using var controller = CreateController(tray, window);

        Assert.True(tray.Visible);
        tray.RequestOpen();
        Assert.True(window.IsVisible);
    }

    private static SystemTrayController CreateController(
        FakeTrayIcon tray,
        FakeTrayWindow window,
        Action? shutdown = null) =>
        new(
            tray,
            window,
            new UsageViewModel(new UsageStore([new TestUsageProvider()])),
            shutdown ?? (() => { }));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class FakeTrayIcon : ITrayIcon
    {
        public event EventHandler? ToggleRequested;
        public event EventHandler? OpenRequested;
        public event EventHandler? RefreshRequested;
        public event EventHandler? ExitRequested;

        public bool Visible { get; set; }
        private string _text = "";
        public string Text
        {
            get => _text;
            set
            {
                if (ThrowOnPresentation) throw new InvalidOperationException("shell unavailable");
                _text = value;
            }
        }
        public bool ThrowOnPresentation { get; init; }
        public int DisposeCalls { get; private set; }
        public NotificationMessage? LastNotification { get; private set; }

        public void RequestToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
        public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
        public void ShowNotification(NotificationMessage message) => LastNotification = message;
        public void SetMenuText(string open, string refresh, string exit)
        {
            if (ThrowOnPresentation) throw new InvalidOperationException("shell unavailable");
        }
        public void SetCompanionFrame(BitmapSource? frame)
        {
            if (ThrowOnPresentation) throw new InvalidOperationException("shell unavailable");
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class FakeTrayWindow : ITrayWindow
    {
        public event CancelEventHandler? Closing;
        public event EventHandler? Deactivated;

        public bool IsVisible { get; set; }
        public bool IsMinimized { get; set; }
        public int ShowNearTrayCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int ActivateCalls { get; private set; }
        public int CloseCalls { get; private set; }

        public void ShowNearTray()
        {
            ShowNearTrayCalls++;
            IsVisible = true;
        }

        public void Hide()
        {
            HideCalls++;
            IsVisible = false;
        }

        public void Restore()
        {
            RestoreCalls++;
            IsMinimized = false;
        }

        public void Activate() => ActivateCalls++;

        public void RequestDeactivation() =>
            Deactivated?.Invoke(this, EventArgs.Empty);

        public void Close()
        {
            CloseCalls++;
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (!args.Cancel)
            {
                IsVisible = false;
            }
        }

        public bool RequestUserClose()
        {
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (!args.Cancel)
            {
                IsVisible = false;
            }

            return args.Cancel;
        }
    }

    private sealed class TestUsageProvider(
        Func<CancellationToken, Task<DailyUsage?>>? dailyHandler = null) : IUsageProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public bool ReportsCost => false;
        public int DailyCalls { get; private set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default)
        {
            DailyCalls++;
            return dailyHandler?.Invoke(cancellationToken) ?? Task.FromResult<DailyUsage?>(null);
        }

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }
}
