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

        var canvasWidth = ReadMetadataInt(
            decoder.Metadata,
            "/logscrdesc/Width",
            decoder.Frames.Max(frame => frame.PixelWidth));
        var canvasHeight = ReadMetadataInt(
            decoder.Metadata,
            "/logscrdesc/Height",
            decoder.Frames.Max(frame => frame.PixelHeight));
        var canvas = new byte[checked(canvasWidth * canvasHeight * 4)];
        var frames = new List<AnimatedSpriteFrame>(decoder.Frames.Count);
        foreach (var frame in decoder.Frames)
        {
            var left = ReadMetadataInt(frame.Metadata, "/imgdesc/Left", 0);
            var top = ReadMetadataInt(frame.Metadata, "/imgdesc/Top", 0);
            var disposal = ReadMetadataInt(frame.Metadata, "/grctlext/Disposal", 0);
            var restore = disposal == 3 ? canvas.ToArray() : null;

            Composite(frame, canvas, canvasWidth, canvasHeight, left, top);
            var composed = BitmapSource.Create(
                canvasWidth,
                canvasHeight,
                frame.DpiX,
                frame.DpiY,
                PixelFormats.Pbgra32,
                null,
                canvas,
                canvasWidth * 4);
            Freeze(composed);
            frames.Add(new AnimatedSpriteFrame(composed, ReadFrameDuration(frame.Metadata)));

            if (disposal == 2)
            {
                Clear(canvas, canvasWidth, canvasHeight, left, top, frame.PixelWidth, frame.PixelHeight);
            }
            else if (restore is not null)
            {
                canvas = restore;
            }
        }

        var first = frames[0].Image;
        return frames.Count < 2
            ? new PokemonSpritePresentation(first, Array.Empty<AnimatedSpriteFrame>(), false)
            : new PokemonSpritePresentation(first, frames, true);
    }

    private static void Composite(
        BitmapSource frame,
        byte[] canvas,
        int canvasWidth,
        int canvasHeight,
        int left,
        int top)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var y = 0; y < converted.PixelHeight && top + y < canvasHeight; y++)
        {
            if (top + y < 0)
            {
                continue;
            }

            for (var x = 0; x < converted.PixelWidth && left + x < canvasWidth; x++)
            {
                if (left + x < 0)
                {
                    continue;
                }

                var source = (y * stride) + (x * 4);
                var destination = (((top + y) * canvasWidth) + left + x) * 4;
                var inverseAlpha = 255 - pixels[source + 3];
                for (var channel = 0; channel < 4; channel++)
                {
                    canvas[destination + channel] = (byte)Math.Min(
                        255,
                        pixels[source + channel] +
                        ((canvas[destination + channel] * inverseAlpha + 127) / 255));
                }
            }
        }
    }

    private static void Clear(
        byte[] canvas,
        int canvasWidth,
        int canvasHeight,
        int left,
        int top,
        int width,
        int height)
    {
        var startX = Math.Clamp(left, 0, canvasWidth);
        var endX = Math.Clamp(left + width, 0, canvasWidth);
        var startY = Math.Clamp(top, 0, canvasHeight);
        var endY = Math.Clamp(top + height, 0, canvasHeight);
        for (var y = startY; y < endY; y++)
        {
            Array.Clear(canvas, ((y * canvasWidth) + startX) * 4, (endX - startX) * 4);
        }
    }

    private static int ReadMetadataInt(ImageMetadata? metadata, string query, int fallback)
    {
        if (metadata is not BitmapMetadata bitmapMetadata)
        {
            return fallback;
        }

        try
        {
            var value = bitmapMetadata.GetQuery(query);
            return value is null
                ? fallback
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or InvalidOperationException or FormatException or OverflowException)
        {
            return fallback;
        }
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
