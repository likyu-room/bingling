// What the image tools change in a picture (the crop so far) and the geometry of that crop.

namespace Filee.Core.Presets;

/// <summary>Which tool a <see cref="PresetKind.Tool"/> preset opens. A kind from a newer Filee reads as <see cref="Crop"/>.</summary>
public enum ImageToolKind
{
    /// <summary>Cuts a box out of the picture (the crop window).</summary>
    Crop,
    AddBackground,
    Redact,
    Annotate,
}

/// <summary>
/// A box to keep, as fractions (0 to 1) of the picture after its EXIF orientation, rotation and flip are applied, so
/// a box drawn on the picture as it is shown means the same thing to every engine.
/// </summary>
public sealed class CropRect
{
    /// <summary>Left edge, as a fraction of the width.</summary>
    public double X { get; set; }

    /// <summary>Top edge, as a fraction of the height.</summary>
    public double Y { get; set; }

    /// <summary>Width, as a fraction of the picture's width.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Height, as a fraction of the picture's height.</summary>
    public double Height { get; set; } = 1;

    /// <summary>
    /// A fixed ratio (width in pixels / height in pixels) the box keeps on every picture, or null for a free box. The
    /// fractions alone can't say it: half the width and half the height are a square on one picture and not on another.
    /// </summary>
    public double? Aspect { get; set; }

    public CropRect Clone() => (CropRect)MemberwiseClone();
}

/// <summary>Edits an image tool made to a picture, applied by the engine that writes it (before resizing).</summary>
public sealed class ImageEdit
{
    /// <summary>The box to keep, or null to keep the whole picture.</summary>
    public CropRect? Crop { get; set; }

    public ImageEdit Clone() => new() { Crop = Crop?.Clone() };
}

/// <summary>Geometry of the crop tool: ratios, the first box, a box on another picture, and pixels.</summary>
public static class CropMath
{
    /// <summary>The ratio choices of the crop window, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> AspectIds = ["free", "original", "1:1", "16:9", "9:16", "4:3", "3:4"];

    /// <summary>
    /// The ratio (width / height) a choice of <see cref="AspectIds"/> means for a picture of this size; null for
    /// "free" and for anything unknown, so a setting from a newer Filee gives a free box.
    /// </summary>
    public static double? RatioOf(string aspectId, int width, int height) => aspectId switch
    {
        "original" when width > 0 && height > 0 => (double)width / height,
        "1:1" => 1,
        "16:9" => 16.0 / 9,
        "9:16" => 9.0 / 16,
        "4:3" => 4.0 / 3,
        "3:4" => 3.0 / 4,
        _ => null,
    };

    /// <summary>
    /// The box the crop window starts with: the largest one of the ratio, in the middle; the whole picture when free.
    /// </summary>
    public static CropRect Initial(double? aspect, int width, int height) =>
        Refit(new CropRect { Aspect = aspect }, width, height);

    /// <summary>
    /// <paramref name="crop"/> on a picture of this size, for a box kept over several files. A free box keeps its
    /// fractions. A fixed ratio keeps its centre and its width, and gets the height the ratio asks on this picture (a
    /// square of half the width of a landscape photo is not half the height of a portrait one); too tall, it takes the
    /// whole height instead, and the box never leaves the picture.
    /// </summary>
    public static CropRect Refit(CropRect crop, int width, int height)
    {
        if (crop.Aspect is not > 0 || width <= 0 || height <= 0)
        {
            var x = Math.Clamp(crop.X, 0, 1);
            var y = Math.Clamp(crop.Y, 0, 1);
            return new CropRect
            {
                X = x,
                Y = y,
                Width = Math.Clamp(crop.Width, 0, 1 - x),
                Height = Math.Clamp(crop.Height, 0, 1 - y),
            };
        }

        // Height as a fraction of the picture's height for a width of 1 (a fraction of the width).
        var k = (double)width / (crop.Aspect.Value * height);
        var cx = crop.X + crop.Width / 2;
        var cy = crop.Y + crop.Height / 2;
        var w = Math.Clamp(crop.Width, 0, 1);
        var h = w * k;
        if (h > 1)
        {
            h = 1;
            w = 1 / k;
        }
        return new CropRect
        {
            X = Math.Clamp(cx - w / 2, 0, 1 - w),
            Y = Math.Clamp(cy - h / 2, 0, 1 - h),
            Width = w,
            Height = h,
            Aspect = crop.Aspect,
        };
    }

    /// <summary>
    /// <paramref name="crop"/> as a pixel box on a picture of this size (after <see cref="Refit"/>): rounded, at least
    /// one pixel, inside the picture.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ToPixels(CropRect crop, int width, int height)
    {
        var r = Refit(crop, width, height);
        var x = Math.Min((int)Math.Round(r.X * width, MidpointRounding.AwayFromZero), width - 1);
        var y = Math.Min((int)Math.Round(r.Y * height, MidpointRounding.AwayFromZero), height - 1);
        var w = Math.Clamp((int)Math.Round(r.Width * width, MidpointRounding.AwayFromZero), 1, width - x);
        var h = Math.Clamp((int)Math.Round(r.Height * height, MidpointRounding.AwayFromZero), 1, height - y);
        return (x, y, w, h);
    }
}
