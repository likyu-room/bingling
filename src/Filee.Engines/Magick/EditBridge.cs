// Hands a picture from ImageMagick to ImageEditRenderer and back, so saving draws an edit with the same code as the
// editor window's preview. Part of ImageEncoder.ApplyOptions; the renderer also reads backdrop pictures through it.

using System.Runtime.InteropServices;
using Filee.Core.Presets;
using ImageMagick;
using SkiaSharp;

namespace Filee.Engines.Magick;

internal static class EditBridge
{
    /// <summary>Rows copied at a time, so a picture never needs a second full-size copy on its way to Skia.</summary>
    private const int StripRows = 256;

    /// <summary>
    /// Draws <paramref name="edit"/> onto <paramref name="image"/> in place: the picture keeps its profiles, density and
    /// format settings, and takes the result's size (larger with a backdrop).
    /// </summary>
    public static void Apply(IMagickImage<byte> image, ImageEdit edit)
    {
        var opaque = !image.HasAlpha;
        SKBitmap result;
        using (var picture = ToBitmap(image))
        {
            // ImageMagick's pixels are not needed while Skia draws: shrunk to one, a 50-megapixel photo takes two
            // full-size buffers (the picture and the result) instead of three. It grows back to the result's size below.
            image.Sample(1, 1);
            result = ImageEditRenderer.Render(picture, edit);
        }
        using (result)
        {
            // Sampled, not extended: Extent fills with the background colour, which in a GIF is transparent and would
            // give an opaque picture an alpha channel of zeros.
            image.Sample(new MagickGeometry((uint)result.Width, (uint)result.Height) { IgnoreAspectRatio = true });
            image.ResetPage();
            // Hidden boxes and backdrops make nothing see-through, so an opaque picture skips ("P") the alpha bytes: given an
            // alpha channel, ImageMagick would copy all its pixels once more, and the file would be saved with one.
            image.ImportPixels(result.GetPixelSpan(),
                new PixelImportSettings((uint)result.Width, (uint)result.Height, StorageType.Char, opaque ? "RGBP" : "RGBA"));
        }
    }

    /// <summary>
    /// The pixels of <paramref name="image"/> as straight (unpremultiplied) RGBA. A CMYK or grey picture is made RGB
    /// first (through its colour profile when it has one), so colours drawn on it stay colours.
    /// </summary>
    public static SKBitmap ToBitmap(IMagickImage<byte> image)
    {
        if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB) && !image.TransformColorSpace(ColorProfiles.SRGB))
            image.ColorSpace = ColorSpace.sRGB;
        var width = (int)image.Width;
        var height = (int)image.Height;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var pixels = image.GetPixelsUnsafe();
        for (var y = 0; y < height; y += StripRows)
        {
            var rows = Math.Min(StripRows, height - y);
            var strip = pixels.ToByteArray(0, y, (uint)width, (uint)rows, PixelMapping.RGBA)!;
            Marshal.Copy(strip, 0, bitmap.GetPixels() + y * bitmap.RowBytes, strip.Length);
        }
        return bitmap;
    }
}
