using PokeTokenBar.Windows.App.FloatingPet;

namespace PokeTokenBar.Windows.Tests;

public sealed class FloatingPetControllerTests
{
    [Fact]
    public void StartShowsSingleWindowAtDefaultPositionOnlyOnce()
    {
        var window = new FakeWindow();
        using var controller = new FloatingPetController(window);

        controller.Start();
        controller.Start();

        Assert.True(controller.HasStarted);
        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowCalls);
    }

    [Fact]
    public void DisposeClosesWindowAndAnimationOwnerExactlyOnce()
    {
        var window = new FakeWindow();
        var controller = new FloatingPetController(window);
        controller.Start();

        controller.Dispose();
        controller.Dispose();

        Assert.False(window.IsVisible);
        Assert.Equal(1, window.CloseCalls);
        Assert.Equal(1, window.DisposeCalls);
    }

    [Fact]
    public void DisposedControllerCannotShowWindowLater()
    {
        var window = new FakeWindow();
        var controller = new FloatingPetController(window);
        controller.Dispose();

        controller.Start();

        Assert.False(controller.HasStarted);
        Assert.Equal(0, window.ShowCalls);
    }

    private sealed class FakeWindow : IFloatingPetWindow
    {
        public bool IsVisible { get; private set; }
        public int ShowCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public void ShowAtDefaultPosition()
        {
            ShowCalls++;
            IsVisible = true;
        }

        public void Close()
        {
            CloseCalls++;
            IsVisible = false;
        }

        public void Dispose() => DisposeCalls++;
    }
}
