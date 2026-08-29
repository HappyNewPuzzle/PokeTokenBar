using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class WpfPokemonSpriteDecoderTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly WpfPokemonSpriteDecoder _decoder = new();

    [Fact]
    public void Decode_ValidPngPreservesDimensionsAndOwnsItsData()
    {
        var presentation = _decoder.Decode(Asset(OnePixelPng, animated: false));

        Assert.NotNull(presentation);
        Assert.False(presentation.IsAnimated);
        Assert.Empty(presentation.Frames);
        Assert.Equal(1, presentation.StaticImage.PixelWidth);
        Assert.Equal(1, presentation.StaticImage.PixelHeight);
        Assert.True(presentation.StaticImage.IsFrozen);

        var pixels = new byte[4];
        var converted = new FormatConvertedBitmap(
            presentation.StaticImage,
            PixelFormats.Bgra32,
            null,
            0);
        converted.CopyPixels(pixels, 4, 0);
        Assert.NotEmpty(pixels);
    }

    [Theory]
    [MemberData(nameof(InvalidPngData))]
    public void Decode_InvalidPngReturnsNull(byte[] data)
    {
        Assert.Null(_decoder.Decode(Asset(data, animated: false)));
    }

    [Fact]
    public void Decode_AnimatedGifPreservesFrameOrderDurationsAndFrozenImages()
    {
        var presentation = _decoder.Decode(
            Asset(CreateGif([10, 20], [0, 1]), animated: true));

        Assert.NotNull(presentation);
        Assert.True(presentation.IsAnimated);
        Assert.True(presentation.LoopsContinuously);
        Assert.Equal(2, presentation.Frames.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), presentation.Frames[0].Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(200), presentation.Frames[1].Duration);
        Assert.All(presentation.Frames, frame => Assert.True(frame.Image.IsFrozen));
        Assert.Equal(0, ReadRed(presentation.Frames[0].Image));
        Assert.Equal(255, ReadRed(presentation.Frames[1].Image));
        Assert.Same(presentation.Frames[0].Image, presentation.StaticImage);
    }

    [Fact]
    public void Decode_GifWithoutDelayUsesOneTenthSecondFallback()
    {
        var presentation = _decoder.Decode(
            Asset(CreateGif([null, null], [0, 1]), animated: true));

        Assert.NotNull(presentation);
        Assert.All(
            presentation.Frames,
            frame => Assert.Equal(TimeSpan.FromMilliseconds(100), frame.Duration));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Decode_GifDelayBelowTwentyMillisecondsUsesFallback(ushort delay)
    {
        var presentation = _decoder.Decode(
            Asset(CreateGif([delay, 10], [0, 1]), animated: true));

        Assert.NotNull(presentation);
        Assert.Equal(TimeSpan.FromMilliseconds(100), presentation.Frames[0].Duration);
    }

    [Fact]
    public void Decode_SingleFrameGifIsStaticPresentation()
    {
        var presentation = _decoder.Decode(
            Asset(CreateGif([10], [0]), animated: true));

        Assert.NotNull(presentation);
        Assert.False(presentation.IsAnimated);
        Assert.Empty(presentation.Frames);
        Assert.True(presentation.StaticImage.IsFrozen);
    }

    [Fact]
    public void InfrastructureAssemblyDoesNotReferenceWpf()
    {
        var references = typeof(PokemonSpriteLoader).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name is
                "PresentationCore" or "PresentationFramework" or "WindowsBase");
    }

    public static TheoryData<byte[]> InvalidPngData =>
        new()
        {
            Array.Empty<byte>(),
            new byte[] { 1, 2, 3, 4 },
            OnePixelPng[..12],
        };

    private static PokemonSpriteAsset Asset(byte[] data, bool animated) =>
        new(
            data,
            new Uri($"https://example.test/sprite.{(animated ? "gif" : "png")}"),
            animated ? "image/gif" : "image/png",
            animated,
            IsShiny: false);

    private static byte[] CreateGif(
        IReadOnlyList<ushort?> delaysInHundredths,
        IReadOnlyList<byte> colorIndexes)
    {
        Assert.Equal(delaysInHundredths.Count, colorIndexes.Count);
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
        bytes.AddRange([1, 0, 1, 0, 0x80, 0, 0]);
        bytes.AddRange([0, 0, 0, 255, 255, 255]);

        for (var index = 0; index < colorIndexes.Count; index++)
        {
            if (delaysInHundredths[index] is ushort delay)
            {
                bytes.AddRange(
                [
                    0x21, 0xF9, 0x04, 0,
                    (byte)(delay & 0xFF), (byte)(delay >> 8),
                    0, 0,
                ]);
            }

            bytes.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
            bytes.AddRange(
                colorIndexes[index] == 0
                    ? [0x02, 0x02, 0x44, 0x01, 0]
                    : [0x02, 0x02, 0x4C, 0x01, 0]);
        }

        bytes.Add(0x3B);
        return bytes.ToArray();
    }

    private static byte ReadRed(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[4];
        converted.CopyPixels(pixels, 4, 0);
        return pixels[2];
    }
}
