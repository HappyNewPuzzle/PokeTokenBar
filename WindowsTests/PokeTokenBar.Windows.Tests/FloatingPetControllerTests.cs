using PokeTokenBar.Windows.App.FloatingPet;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class FloatingPetControllerTests
{
    [Fact]
    public void DisabledSettingKeepsWindowHiddenAtStartup()
    {
        var window = new FakeWindow();
        using var controller = CreateController(window, new AppSettings());
        controller.Start();
        Assert.True(controller.HasStarted);
        Assert.False(window.IsVisible);
        Assert.Equal(0, window.ShowCalls);
    }

    [Fact]
    public void EnabledSettingRestoresSavedPositionAtStartup()
    {
        var position = new FloatingPetPosition(120, 240);
        var window = new FakeWindow();
        using var controller = CreateController(window, new AppSettings(true, position, false));
        controller.Start();
        Assert.True(window.IsVisible);
        Assert.Equal(position, window.LastPosition);
    }

    [Fact]
    public void DisplaySleepHidesEnabledPetAndWakeRestoresSavedPosition()
    {
        var position = new FloatingPetPosition(120, 240);
        var window = new FakeWindow();
        using var controller = CreateController(window, new AppSettings(true, position, false));
        controller.Start();

        controller.SetDisplayAwake(false);
        controller.SetDisplayAwake(true);

        Assert.True(window.IsVisible);
        Assert.Equal(2, window.ShowCalls);
        Assert.Equal(position, window.LastPosition);
    }

    [Fact]
    public void SettingChangesImmediatelyShowAndHideSameWindow()
    {
        var window = new FakeWindow();
        var persistence = new FakeSettingsPersistence(new AppSettings());
        var settings = new SettingsViewModel(persistence, new FakeAutoStart());
        using var controller = new FloatingPetController(window, settings, () => { });
        controller.Start();
        settings.IsFloatingPetEnabled = true;
        settings.IsFloatingPetEnabled = false;
        Assert.Equal(1, window.ShowCalls);
        Assert.Equal(2, window.HideCalls);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void ClickOpensPopupAndContextHideDisablesPet()
    {
        var opens = 0;
        var window = new FakeWindow();
        var settings = new SettingsViewModel(
            new FakeSettingsPersistence(new AppSettings(true)),
            new FakeAutoStart());
        using var controller = new FloatingPetController(window, settings, () => opens++);
        controller.Start();
        window.RequestOpen();
        window.RequestHide();
        Assert.Equal(1, opens);
        Assert.False(settings.IsFloatingPetEnabled);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void DragCommitPersistsPositionOnlyWhenWindowCommitsIt()
    {
        var window = new FakeWindow();
        var persistence = new FakeSettingsPersistence(new AppSettings(true));
        var settings = new SettingsViewModel(persistence, new FakeAutoStart());
        using var controller = new FloatingPetController(window, settings, () => { });
        controller.Start();
        window.CommitPosition(new FloatingPetPosition(-120, 480));
        Assert.Equal(new FloatingPetPosition(-120, 480), settings.SavedFloatingPetPosition);
        Assert.Equal(new FloatingPetPosition(-120, 480), persistence.LastSaved?.FloatingPetPosition);
    }

    [Fact]
    public async Task ResetCommandMovesEnabledPetToCurrentMonitorDefault()
    {
        var window = new FakeWindow();
        var settings = new SettingsViewModel(
            new FakeSettingsPersistence(new AppSettings(true, new FloatingPetPosition(99, 88), false)),
            new FakeAutoStart());
        using var controller = new FloatingPetController(window, settings, () => { });
        controller.Start();
        await settings.ResetFloatingPetPositionCommand.ExecuteAsync();
        Assert.Equal(1, window.ResetCalls);
        Assert.Equal(new FloatingPetPosition(1800, 920), settings.SavedFloatingPetPosition);
    }

    [Fact]
    public void DisposeClosesWindowAndStopsLaterSettingChanges()
    {
        var window = new FakeWindow();
        var settings = new SettingsViewModel(
            new FakeSettingsPersistence(new AppSettings(true)),
            new FakeAutoStart());
        var controller = new FloatingPetController(window, settings, () => { });
        controller.Start();
        controller.Dispose();
        controller.Dispose();
        settings.IsFloatingPetEnabled = false;
        settings.IsFloatingPetEnabled = true;
        Assert.Equal(1, window.CloseCalls);
        Assert.Equal(1, window.DisposeCalls);
        Assert.Equal(1, window.ShowCalls);
    }

    private static FloatingPetController CreateController(FakeWindow window, AppSettings settings) =>
        new(window, new SettingsViewModel(new FakeSettingsPersistence(settings), new FakeAutoStart()), () => { });

    private sealed class FakeWindow : IFloatingPetWindow
    {
        public event EventHandler? OpenRequested;
        public event EventHandler? HideRequested;
        public event EventHandler<FloatingPetPositionEventArgs>? PositionCommitted;
        public bool IsVisible { get; private set; }
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public FloatingPetPosition? LastPosition { get; private set; }
        public void ShowAtPosition(FloatingPetPosition? position) { ShowCalls++; LastPosition = position; IsVisible = true; }
        public void ResetToDefaultPosition()
        {
            ResetCalls++;
            CommitPosition(new FloatingPetPosition(1800, 920));
        }
        public void Hide() { HideCalls++; IsVisible = false; }
        public void Close() { CloseCalls++; IsVisible = false; }
        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RequestHide() => HideRequested?.Invoke(this, EventArgs.Empty);
        public void CommitPosition(FloatingPetPosition position) =>
            PositionCommitted?.Invoke(this, new FloatingPetPositionEventArgs(position));
        public void Dispose() => DisposeCalls++;
    }

    private sealed class FakeSettingsPersistence(AppSettings settings) : IAppSettingsPersistence
    {
        public AppSettings? LastSaved { get; private set; }
        public AppSettings? Load() => settings;
        public void Save(AppSettings value) => LastSaved = value;
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
}
