using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App.Sprites;

public sealed class WpfPokemonSpriteDecoder : IPokemonSpriteDecoder
{
    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan MinimumValidFrameDuration = TimeSpan.FromSeconds(0.02);

    public PokemonSpritePresentation? Decode(PokemonSpriteAsset asset)
    {
        if (asset.Data.IsEmpty)
        {
            return null;
        }

        try
        {
            return asset.IsAnimated
                ? DecodeGif(asset.Data)
                : DecodeStatic(asset.Data);
        }
        catch (Exception exception) when (IsDecodeFailure(exception))
        {
            return null;
        }
    }

    private static PokemonSpritePresentation? DecodeStatic(ReadOnlyMemory<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        Freeze(image);
        return new PokemonSpritePresentation(image, Array.Empty<AnimatedSpriteFrame>(), false);
    }

    private static PokemonSpritePresentation? DecodeGif(ReadOnlyMemory<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            return null;
        }

        var frames = new List<AnimatedSpriteFrame>(decoder.Frames.Count);
        foreach (var frame in decoder.Frames)
        {
            Freeze(frame);
            frames.Add(new AnimatedSpriteFrame(frame, ReadFrameDuration(frame.Metadata)));
        }

        var first = frames[0].Image;
        return frames.Count < 2
            ? new PokemonSpritePresentation(first, Array.Empty<AnimatedSpriteFrame>(), false)
            : new PokemonSpritePresentation(first, frames, true);
    }

    internal static TimeSpan NormalizeFrameDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < MinimumValidFrameDuration.TotalSeconds)
        {
            return DefaultFrameDuration;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan ReadFrameDuration(ImageMetadata? metadata)
    {
        if (metadata is not BitmapMetadata bitmapMetadata)
        {
            return DefaultFrameDuration;
        }

        try
        {
            var raw = bitmapMetadata.GetQuery("/grctlext/Delay");
            if (raw is null)
            {
                return DefaultFrameDuration;
            }

            var hundredths = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return NormalizeFrameDuration(hundredths / 100d);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or InvalidOperationException or FormatException or OverflowException)
        {
            return DefaultFrameDuration;
        }
    }

    private static void Freeze(BitmapSource image)
    {
        if (image.CanFreeze)
        {
            image.Freeze();
        }
    }

    private static bool IsDecodeFailure(Exception exception) =>
        exception is ArgumentException or InvalidOperationException or IOException or
            NotSupportedException or FormatException;
}
