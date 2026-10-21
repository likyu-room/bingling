// Draws what the add background, redact and annotate tools put on a picture, with SkiaSharp. The editor window draws
// its preview with it and ImageEncoder draws the saved picture with it, so the window shows what is saved (spec §3,
// "preview = result"). The order is the spec's: boxes are hidden, then drawn on, then the picture is set on its backdrop.

using Filee.Core.Formats;
using Filee.Core.Presets;
using SkiaSharp;

namespace Filee.Engines.Magick;

/// <summary>Draws an <see cref="ImageEdit"/> (its hidden boxes and its backdrop) onto a picture, at any scale.</summary>
public static class ImageEditRenderer
{
    /// <summary>The backdrop gradients, in the order the window offers them: id, top left colour, bottom right colour.</summary>
    public static readonly IReadOnlyList<(string Id, SKColor Start, SKColor End)> Gradients =
    [
        ("peach", new SKColor(0xFFFFD3A5), new SKColor(0xFFFD6585)),
        ("sunset", new SKColor(0xFFFF9A8B), new SKColor(0xFFFF6A88)),
        ("gold", new SKColor(0xFFF6D365), new SKColor(0xFFFDA085)),
        ("mint", new SKColor(0xFFD4FC79), new SKColor(0xFF96E6A1)),
        ("sky", new SKColor(0xFF89F7FE), new SKColor(0xFF66A6FF)),
        ("ocean", new SKColor(0xFF2E3192), new SKColor(0xFF1BFFFF)),
        ("lavender", new SKColor(0xFFA18CD1), new SKColor(0xFFFBC2EB)),
        ("night", new SKColor(0xFF232526), new SKColor(0xFF414345)),
    ];

    private static readonly Lock BackdropLock = new();

    /// <summary>The last backdrop picture read, kept because the preview draws it again on every frame.</summary>
    private static (string Path, DateTime Stamp, SKImage? Image)? _backdrop;

    /// <summary>Output size and where the picture sits in it (pixels), for a w×h picture and an optional background.</summary>
    public static (SKSizeI Output, SKRect Picture) Layout(int width, int height, BackgroundEdit? background)
    {
        if (background is null)
            return (new SKSizeI(width, height), SKRect.Create(width, height));
        var padding = (int)Math.Round(Math.Clamp(background.Padding, 0, 0.4) * Math.Min(width, height), MidpointRounding.AwayFromZero);
        var w = width + 2 * padding;
        var h = height + 2 * padding;
        if (Ratio(background.Aspect) is var (a, b))
        {
            // The smallest canvas of k·a × k·b pixels that holds the picture and its padding: exactly the ratio.
            var k = Math.Max((w + a - 1) / a, (h + b - 1) / b);
            (w, h) = (k * a, k * b);
        }
        return (new SKSizeI(w, h), SKRect.Create((w - width) / 2, (h - height) / 2, width, height));
    }

    /// <summary>Draws the whole result into <paramref name="output"/> (any scale): background, then the picture,
    /// its redaction and its annotations. Preview and save both call this.</summary>
    /// <param name="picture">The picture after its orientation and crop, at any size (the preview's is smaller).</param>
    /// <param name="fullShortSide">The short side of the full-size picture in pixels, which sizes the mosaic cells and the
    /// layout the same at every scale; 0 for <paramref name="picture"/>'s own.</param>
    public static void Draw(SKCanvas canvas, SKImage picture, SKRect output, ImageEdit edit, int fullShortSide)
    {
        var shortSide = Math.Min(picture.Width, picture.Height);
        var full = fullShortSide > 0 ? fullShortSide : shortSide;
        // Laid out at full size: rounded on a small copy, a 16:9 canvas would grow in other steps than the saved one.
        var scale = (double)full / shortSide;
        var (size, place) = Layout((int)Math.Round(picture.Width * scale), (int)Math.Round(picture.Height * scale), edit.Background);
        var sx = output.Width / size.Width;
        var sy = output.Height / size.Height;
        var dest = new SKRect(output.Left + place.Left * sx, output.Top + place.Top * sy,
            output.Left + place.Right * sx, output.Top + place.Bottom * sy);

        canvas.Save();
        canvas.ClipRect(output);
        if (edit.Background is { } background)
        {
            DrawBackdrop(canvas, output, background);
            var shorter = Math.Min(dest.Width, dest.Height);
            using var card = new SKRoundRect(dest, (float)(Math.Clamp(background.Corner, 0, 0.25) * shorter));
            DrawShadow(canvas, card, Math.Clamp(background.Shadow, 0, 100), shorter);
            canvas.ClipRoundRect(card, antialias: true);
        }

        var boxes = edit.Redact is { } redact ? Boxes(redact, picture.Width, picture.Height) : [];
        SKRect Shown((int Left, int Top, int Right, int Bottom) box) => new(
            dest.Left + box.Left * dest.Width / picture.Width, dest.Top + box.Top * dest.Height / picture.Height,
            dest.Left + box.Right * dest.Width / picture.Width, dest.Top + box.Bottom * dest.Height / picture.Height);

        // The picture is never drawn under its boxes, so a see-through mosaic cell shows the backdrop, not what it hides.
        canvas.Save();
        foreach (var box in boxes)
            canvas.ClipRect(Shown(box), SKClipOperation.Difference);
        canvas.DrawImage(picture, dest, Sampling(dest.Width < picture.Width));
        canvas.Restore();

        var mosaic = edit.Redact?.Mode == RedactMode.Mosaic;
        // max(8, short side / 40) pixels of the full-size picture, rounded up (never finer), in this picture's pixels.
        var cell = Math.Max(8, Math.Ceiling(full / 40.0)) * shortSide / full;
        using var paint = new SKPaint { Color = SKColors.Black };
        foreach (var box in boxes)
        {
            if (mosaic)
                Mosaic(canvas, picture, box, cell, paint, Shown);
            else
                canvas.DrawRect(Shown(box), paint);
        }

        // Drawings go here: on the picture, over its hidden boxes and inside its rounded corners.
        canvas.Restore();
    }

    /// <summary>Renders at full size into a new bitmap (save path).</summary>
    /// <param name="picture">Straight (unpremultiplied) RGBA, after its orientation and crop.</param>
    public static SKBitmap Render(SKBitmap picture, ImageEdit edit)
    {
        var (size, _) = Layout(picture.Width, picture.Height, edit.Background);
        var result = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var pixels = picture.PeekPixels();
        using var image = SKImage.FromPixels(pixels); // the picture's own pixels, not a copy of them
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        Draw(canvas, image, SKRect.Create(size.Width, size.Height), edit, Math.Min(picture.Width, picture.Height));
        return result;
    }

    /// <summary>Mipmaps when the picture is drawn smaller (the preview); none at full size, where they would only cost memory.</summary>
    private static SKSamplingOptions Sampling(bool smaller) =>
        smaller ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(SKFilterMode.Linear);

    /// <summary>"a:b" as two positive whole numbers; null for "auto" and anything unknown.</summary>
    private static (int A, int B)? Ratio(string? aspect) =>
        aspect?.Split(':') is [var a, var b] && int.TryParse(a, out var x) && int.TryParse(b, out var y) && x > 0 && y > 0
            ? (x, y) : null;

    private static void DrawBackdrop(SKCanvas canvas, SKRect output, BackgroundEdit background)
    {
        using var paint = new SKPaint();
        switch (background.Kind)
        {
            case BackgroundKind.Solid:
                paint.Color = SKColor.TryParse(background.Value, out var color) ? color : SKColors.White;
                canvas.DrawRect(output, paint);
                break;
            case BackgroundKind.Image:
                // White under a picture with see-through parts, and in place of one that can't be read.
                paint.Color = SKColors.White;
                canvas.DrawRect(output, paint);
                if (Backdrop(background.ImagePath) is { } image)
                {
                    // Fills the canvas: scaled to cover it, the overflow cut off evenly on both sides.
                    var fit = Math.Max(output.Width / image.Width, output.Height / image.Height);
                    var source = SKRect.Create((image.Width - output.Width / fit) / 2, (image.Height - output.Height / fit) / 2,
                        output.Width / fit, output.Height / fit);
                    canvas.DrawImage(image, source, output, Sampling(fit < 1));
                }
                break;
            default:
                var gradient = Gradients.FirstOrDefault(g => g.Id == background.Value);
                if (gradient.Id is null)
                    gradient = Gradients[0]; // a gradient from a newer Filee
                // 135° as in CSS: the top left corner has the first colour, the bottom right one the last, and the lines of
                // one colour run at right angles to the 45° line through the middle.
                var d = (output.Width + output.Height) / 4;
                paint.Shader = SKShader.CreateLinearGradient(new SKPoint(output.MidX - d, output.MidY - d),
                    new SKPoint(output.MidX + d, output.MidY + d), [gradient.Start, gradient.End], SKShaderTileMode.Clamp);
                canvas.DrawRect(output, paint);
                break;
        }
    }

    private static void DrawShadow(SKCanvas canvas, SKRoundRect card, int strength, float shortSide)
    {
        if (strength <= 0)
            return;
        var sigma = strength / 100f * 0.03f * shortSide;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha((byte)Math.Round(255 * 0.45 * strength / 100)),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
        };
        using var shadow = new SKRoundRect(card);
        shadow.Offset(0, sigma * 0.5f);
        canvas.Save();
        // Only outside the picture: a see-through picture shows the backdrop, not a grey card under it.
        canvas.ClipRoundRect(card, SKClipOperation.Difference, antialias: true);
        canvas.DrawRoundRect(shadow, paint);
        canvas.Restore();
    }

    /// <summary>
    /// The boxes in pixels of a <paramref name="width"/> × <paramref name="height"/> picture, inside it, each taking every
    /// pixel it touches so none is left half hidden; empty boxes are left out.
    /// </summary>
    private static List<(int Left, int Top, int Right, int Bottom)> Boxes(RedactEdit redact, int width, int height)
    {
        // A hair of slack, so 0.1 × 300 (30.000000000000004 in doubles) is pixel 30, not 31.
        static int Floor(double v, int size) => (int)Math.Floor(Math.Clamp(v, 0, 1) * size + 1e-6);
        static int Ceiling(double v, int size) => (int)Math.Ceiling(Math.Clamp(v, 0, 1) * size - 1e-6);

        var boxes = new List<(int, int, int, int)>();
        foreach (var box in redact.Boxes)
        {
            var left = Floor(Math.Min(box.X, box.X + box.Width), width);
            var right = Ceiling(Math.Max(box.X, box.X + box.Width), width);
            var top = Floor(Math.Min(box.Y, box.Y + box.Height), height);
            var bottom = Ceiling(Math.Max(box.Y, box.Y + box.Height), height);
            if (right > left && bottom > top)
                boxes.Add((left, top, right, bottom));
        }
        return boxes;
    }

    /// <summary>
    /// Fills a box with cells of <paramref name="cell"/> pixels on a grid from the picture's top left corner, each the
    /// average colour of the picture under it. The picture is read a row of cells at a time, not copied whole.
    /// </summary>
    private static void Mosaic(SKCanvas canvas, SKImage picture, (int Left, int Top, int Right, int Bottom) box, double cell,
        SKPaint paint, Func<(int, int, int, int), SKRect> shown)
    {
        var columns = Cuts(box.Left, box.Right, cell);
        var rows = Cuts(box.Top, box.Bottom, cell);
        var width = box.Right - box.Left;
        using var strip = new SKBitmap(new SKImageInfo(width, (int)Math.Ceiling(cell) + 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var j = 0; j + 1 < rows.Count; j++)
        {
            var height = rows[j + 1] - rows[j];
            var read = picture.ReadPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                strip.GetPixels(), strip.RowBytes, box.Left, rows[j]);
            for (var i = 0; i + 1 < columns.Count; i++)
            {
                // A picture that can't be read is hidden all the same.
                paint.Color = read ? Average(strip, columns[i] - box.Left, columns[i + 1] - box.Left, height) : SKColors.Black;
                canvas.DrawRect(shown((columns[i], rows[j], columns[i + 1], rows[j + 1])), paint);
            }
        }
    }

    /// <summary>The grid lines (multiples of <paramref name="cell"/>, rounded) between <paramref name="from"/> and <paramref name="to"/>, with both ends.</summary>
    private static List<int> Cuts(int from, int to, double cell)
    {
        var cuts = new List<int> { from };
        for (var k = (int)(from / cell) + 1; ; k++)
        {
            var cut = (int)Math.Round(k * cell, MidpointRounding.AwayFromZero);
            if (cut >= to)
                break;
            if (cut > cuts[^1])
                cuts.Add(cut);
        }
        cuts.Add(to);
        return cuts;
    }

    /// <summary>The average straight colour of columns <paramref name="from"/> to <paramref name="to"/> of a premultiplied strip.</summary>
    private static SKColor Average(SKBitmap strip, int from, int to, int rows)
    {
        var pixels = strip.GetPixelSpan();
        long r = 0, g = 0, b = 0, a = 0;
        for (var y = 0; y < rows; y++)
        {
            var row = pixels.Slice(y * strip.RowBytes + from * 4, (to - from) * 4);
            for (var i = 0; i < row.Length; i += 4)
            {
                r += row[i];
                g += row[i + 1];
                b += row[i + 2];
                a += row[i + 3];
            }
        }
        if (a == 0)
            return SKColors.Transparent;
        // Premultiplied sums weigh each pixel by its opacity; divided by the opacity sum they give the straight colour.
        long count = rows * (to - from);
        return new SKColor((byte)((r * 255 + a / 2) / a), (byte)((g * 255 + a / 2) / a), (byte)((b * 255 + a / 2) / a),
            (byte)((a + count / 2) / count));
    }

    /// <summary>The user's backdrop picture, upright, or null when it can't be read (missing, locked, not a picture).</summary>
    private static SKImage? Backdrop(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        lock (BackdropLock)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path); // a missing file has a fixed stamp, a replaced one a new one
                if (_backdrop is { } kept && kept.Path == path && kept.Stamp == stamp)
                    return kept.Image;
                _backdrop = (path, stamp, null); // one that can't be read is not tried again on every frame
                using var frames = ImageReader.Read(path, FormatRegistry.Detect(path)?.Id ?? "", new Preset(), firstFrameOnly: true);
                frames[0].AutoOrient();
                using var bitmap = EditBridge.ToBitmap(frames[0]);
                bitmap.SetImmutable(); // the image shares the bitmap's pixels instead of copying them
                var image = SKImage.FromBitmap(bitmap);
                // ponytail: one picture kept, at full size, until another is asked for; the one it replaces is left to the
                // finalizer, as a save on another thread may still be drawing it. Downscale or evict if memory matters.
                _backdrop = (path, stamp, image);
                return image;
            }
            catch (Exception)
            {
                return null; // drawn white instead
            }
        }
    }
}
