using System.ComponentModel;
using System.Windows;

namespace PokeTokenBar.Windows.App.Sprites;

public sealed class AnimatedSpritePresenter : System.Windows.Controls.Image, IDisposable
{
    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(PokemonSpritePresentation),
            typeof(AnimatedSpritePresenter),
            new PropertyMetadata(null, OnPresentationChanged));

    private readonly SpriteAnimationController _controller;
    private bool _disposed;

    public AnimatedSpritePresenter()
    {
        _controller = new SpriteAnimationController(
            new DispatcherSpriteAnimationTimerFactory(Dispatcher));
        _controller.PropertyChanged += OnControllerPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        Stretch = System.Windows.Media.Stretch.Uniform;
        SnapsToDevicePixels = true;
    }

    public PokemonSpritePresentation? Presentation
    {
        get => (PokemonSpritePresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        IsVisibleChanged -= OnIsVisibleChanged;
        _controller.PropertyChanged -= OnControllerPropertyChanged;
        _controller.Dispose();
    }

    private static void OnPresentationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var presenter = (AnimatedSpritePresenter)dependencyObject;
        presenter._controller.SetPresentation((PokemonSpritePresentation?)args.NewValue);
        presenter.Source = presenter._controller.CurrentImage;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SpriteAnimationController.CurrentImage))
        {
            Source = _controller.CurrentImage;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args) =>
        _controller.SetActive(IsVisible);

    private void OnUnloaded(object sender, RoutedEventArgs args) =>
        _controller.SetActive(false);

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args) =>
        _controller.SetActive(IsLoaded && IsVisible);
}
