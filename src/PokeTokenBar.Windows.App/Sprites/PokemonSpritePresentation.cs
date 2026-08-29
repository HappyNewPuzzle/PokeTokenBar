using System.Windows.Media.Imaging;

namespace PokeTokenBar.Windows.App.Sprites;

public sealed record AnimatedSpriteFrame(BitmapSource Image, TimeSpan Duration);

public sealed record PokemonSpritePresentation(
    BitmapSource StaticImage,
    IReadOnlyList<AnimatedSpriteFrame> Frames,
    bool LoopsContinuously)
{
    public bool IsAnimated => Frames.Count >= 2;
}

public interface IPokemonSpriteDecoder
{
    PokemonSpritePresentation? Decode(
        PokeTokenBar.Windows.Infrastructure.PokemonSpriteAsset asset);
}
