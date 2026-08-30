using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App.Sprites;

namespace PokeTokenBar.Windows.Tests;

public sealed class SpriteAnimationControllerTests
{
    [Fact]
    public void StaticPresentationShowsImageWithoutCreatingTimer()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var image = Image(1);

        controller.SetActive(true);
        controller.SetPresentation(Static(image));

        Assert.Same(image, controller.CurrentImage);
        Assert.False(controller.IsAnimating);
        Assert.Empty(factory.Timers);
    }

    [Fact]
    public void AnimatedPresentationShowsFirstFrameImmediately()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 250));

        controller.SetActive(true);
        controller.SetPresentation(presentation);

        Assert.Same(presentation.Frames[0].Image, controller.CurrentImage);
        Assert.True(controller.IsAnimating);
        Assert.Single(factory.Timers);
        Assert.Equal(TimeSpan.FromMilliseconds(100), factory.Timers[0].Interval);
        Assert.Equal(1, factory.Timers[0].StartCalls);
    }

    [Fact]
    public void TickAdvancesInOrderAndUsesDurationOfDisplayedFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 250), (3, 400));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        var timer = factory.Timers.Single();

        timer.Fire();
        Assert.Same(presentation.Frames[1].Image, controller.CurrentImage);
        Assert.Equal(TimeSpan.FromMilliseconds(250), timer.Interval);

        timer.Fire();
        Assert.Same(presentation.Frames[2].Image, controller.CurrentImage);
        Assert.Equal(TimeSpan.FromMilliseconds(400), timer.Interval);
    }

    [Fact]
    public void LastFrameLoopsBackToFirstFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 200));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        var timer = factory.Timers.Single();

        timer.Fire();
        timer.Fire();

        Assert.Same(presentation.Frames[0].Image, controller.CurrentImage);
        Assert.NotNull(controller.CurrentImage);
        Assert.Equal(TimeSpan.FromMilliseconds(100), timer.Interval);
    }

    [Fact]
    public void EveryLoopTransitionKeepsAVisibleFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 100));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        var images = new List<BitmapSource?> { controller.CurrentImage };
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SpriteAnimationController.CurrentImage))
            {
                images.Add(controller.CurrentImage);
            }
        };

        factory.Timers.Single().Fire();
        factory.Timers.Single().Fire();

        Assert.All(images, Assert.NotNull);
        Assert.Equal([presentation.Frames[0].Image, presentation.Frames[1].Image, presentation.Frames[0].Image], images);
    }

    [Fact]
    public void ReplacingAnimatedSpriteStopsOldTimerAndShowsNewFirstFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        controller.SetActive(true);
        controller.SetPresentation(Animated((1, 100), (2, 100)));
        var oldTimer = factory.Timers.Single();
        var replacement = Animated((3, 150), (4, 150));

        controller.SetPresentation(replacement);

        Assert.True(oldTimer.IsDisposed);
        Assert.Same(replacement.Frames[0].Image, controller.CurrentImage);
        Assert.Equal(2, factory.Timers.Count);
    }

    [Fact]
    public void LateTickFromReplacedSpriteCannotOverwriteReplacement()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        controller.SetActive(true);
        controller.SetPresentation(Animated((1, 100), (2, 100)));
        var oldTimer = factory.Timers.Single();
        var replacement = Animated((3, 100), (4, 100));
        controller.SetPresentation(replacement);

        oldTimer.FireEvenIfDisposed();

        Assert.Same(replacement.Frames[0].Image, controller.CurrentImage);
    }

    [Fact]
    public void AnimatedToStaticStopsAnimation()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        controller.SetActive(true);
        controller.SetPresentation(Animated((1, 100), (2, 100)));
        var timer = factory.Timers.Single();
        var image = Image(3);

        controller.SetPresentation(Static(image));

        Assert.True(timer.IsDisposed);
        Assert.False(controller.IsAnimating);
        Assert.Same(image, controller.CurrentImage);
    }

    [Fact]
    public void StaticToAnimatedStartsAnimationAtFirstFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        controller.SetActive(true);
        controller.SetPresentation(Static(Image(1)));
        var animated = Animated((2, 100), (3, 100));

        controller.SetPresentation(animated);

        Assert.Same(animated.Frames[0].Image, controller.CurrentImage);
        Assert.True(controller.IsAnimating);
        Assert.Single(factory.Timers);
    }

    [Fact]
    public void OneFramePresentationIsStaticAndHasNoTimer()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var image = Image(1);
        var presentation = new PokemonSpritePresentation(
            image,
            [new AnimatedSpriteFrame(image, TimeSpan.FromMilliseconds(100))],
            true);

        controller.SetActive(true);
        controller.SetPresentation(presentation);

        Assert.False(presentation.IsAnimated);
        Assert.Same(image, controller.CurrentImage);
        Assert.Empty(factory.Timers);
    }

    [Fact]
    public void HiddenStateStopsTimerAndResetsToFirstFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 100));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        var timer = factory.Timers.Single();
        timer.Fire();
        Assert.Same(presentation.Frames[1].Image, controller.CurrentImage);

        controller.SetActive(false);

        Assert.True(timer.IsDisposed);
        Assert.False(controller.IsAnimating);
        Assert.Same(presentation.Frames[0].Image, controller.CurrentImage);
    }

    [Fact]
    public void ResumeCreatesNewTimerAndRestartsAtFirstFrame()
    {
        var factory = new FakeTimerFactory();
        using var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 100));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        factory.Timers[0].Fire();
        controller.SetActive(false);

        controller.SetActive(true);

        Assert.Equal(2, factory.Timers.Count);
        Assert.Same(presentation.Frames[0].Image, controller.CurrentImage);
        Assert.True(controller.IsAnimating);
    }

    [Fact]
    public void DisposeIsIdempotentAndLateTickIsIgnored()
    {
        var factory = new FakeTimerFactory();
        var controller = new SpriteAnimationController(factory);
        var presentation = Animated((1, 100), (2, 100));
        controller.SetPresentation(presentation);
        controller.SetActive(true);
        var timer = factory.Timers.Single();

        controller.Dispose();
        controller.Dispose();
        timer.FireEvenIfDisposed();

        Assert.True(timer.IsDisposed);
        Assert.False(controller.IsAnimating);
        Assert.Same(presentation.Frames[0].Image, controller.CurrentImage);
    }

    private static PokemonSpritePresentation Static(BitmapSource image) =>
        new(image, [], false);

    private static PokemonSpritePresentation Animated(
        params (byte Marker, int Milliseconds)[] definitions)
    {
        var frames = definitions
            .Select(definition => new AnimatedSpriteFrame(
                Image(definition.Marker),
                TimeSpan.FromMilliseconds(definition.Milliseconds)))
            .ToArray();
        return new PokemonSpritePresentation(frames[0].Image, frames, true);
    }

    private static BitmapSource Image(byte marker)
    {
        var image = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { marker, marker, marker, 255 },
            4);
        image.Freeze();
        return image;
    }

    private sealed class FakeTimerFactory : ISpriteAnimationTimerFactory
    {
        public List<FakeTimer> Timers { get; } = [];

        public ISpriteAnimationTimer Create(TimeSpan interval, EventHandler tick)
        {
            var timer = new FakeTimer(interval, tick);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class FakeTimer(TimeSpan interval, EventHandler tick) : ISpriteAnimationTimer
    {
        private readonly EventHandler _tick = tick;

        public TimeSpan Interval { get; set; } = interval;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public void Fire()
        {
            if (!IsDisposed)
            {
                _tick(this, EventArgs.Empty);
            }
        }

        public void FireEvenIfDisposed() => _tick(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;
    }
}
