// Small pictures the image tool tests run through the engines: a marked picture, a phone photo turned by its EXIF
// data, EXIF data with a preview, noise. Shared by ImageToolTests and ImageEditTests.

using Filee.Core.Presets;
using ImageMagick;
using ImageMagick.Drawing;

namespace Filee.Engines.Tests;

internal static class ImageSamples
{
    /// <summary>A built-in preset by id.</summary>
    public static Preset Tool(string id) => BuiltInData.CreatePresets().Single(p => p.Id == id);

    /// <summary>A 40 × 20 white image with a red 10 × 10 square in the top left corner.</summary>
    public static MagickImage Marked()
    {
        var image = new MagickImage(MagickColors.White, 40, 20);
        new Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 9, 9).Draw(image);
        return image;
    }

    public static bool IsRed(IMagickImage<byte> image, int x, int y) =>
        image.GetPixels().GetPixel(x, y).ToColor() is { } c && c.R > 200 && c.G < 80 && c.B < 80;

    /// <summary>
    /// Writes <paramref name="image"/> (and disposes it) as <paramref name="name"/>, whose extension names the format, in
    /// a fresh folder. <paramref name="compressionLevel"/> sets the zlib level of a PNG.
    /// </summary>
    public static string Write(this EngineFixture fx, MagickImage image, string name, int? compressionLevel = null)
    {
        var path = Path.Combine(fx.NewFolder(), name);
        using (image)
        {
            if (compressionLevel is { } level)
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", level);
            image.Write(path);
        }
        return path;
    }

    /// <summary>
    /// A JPEG as phones save it: stored 40 × 20, shown turned right (EXIF orientation 6), so on screen 20 × 40 with the
    /// red square at the top right. <paramref name="scale"/> makes it that many times as big.
    /// </summary>
    public static string WritePhone(this EngineFixture fx, uint quality = 90, uint scale = 1)
    {
        var image = Marked();
        if (scale > 1)
            image.Scale(image.Width * scale, image.Height * scale);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)OrientationType.RightTop);
        image.SetProfile(exif);
        image.Orientation = OrientationType.RightTop; // ImageMagick writes this into the profile
        image.Quality = quality;
        return fx.Write(image, "phone.jpg");
    }

    /// <summary>Random pixels from a few levels per channel: a picture that zlib packs better the harder it tries.</summary>
    public static MagickImage Noise(uint width, uint height)
    {
        var random = new Random(1);
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(random.Next(8) * 32);
        return new MagickImage(pixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGB));
    }

    /// <summary>
    /// EXIF data with orientation <paramref name="orientation"/> and a small JPEG preview, as cameras and phones write
    /// it: a TIFF header, IFD0 with the orientation, IFD1 pointing at the preview.
    /// </summary>
    public static ExifProfile ExifWithThumbnail(ushort orientation)
    {
        byte[] thumbnail;
        using (var small = new MagickImage(MagickColors.Blue, 8, 4))
            thumbnail = small.ToByteArray(MagickFormat.Jpeg);
        var data = new List<byte>();
        void U16(int v) => data.AddRange(BitConverter.GetBytes((ushort)v));
        void U32(int v) => data.AddRange(BitConverter.GetBytes((uint)v));
        data.AddRange("II"u8.ToArray());
        U16(42);
        U32(8);
        U16(1); // IFD0 at 8: one entry
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U32(26); // IFD1 at 8 + 2 + 12 + 4
        U16(2); // IFD1: the preview's offset and length
        U16(0x0201); U16(4); U32(1); U32(26 + 2 + 24 + 4);
        U16(0x0202); U16(4); U32(1); U32(thumbnail.Length);
        U32(0);
        data.AddRange(thumbnail);
        return new ExifProfile([.. "Exif\0\0"u8.ToArray(), .. data]);
    }
}
