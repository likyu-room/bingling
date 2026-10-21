// The picture on its backdrop in the add background window: zoom and pan are PictureView's, and there is nothing to drag
// on the picture. It only sizes what is drawn as the whole result (the picture with its padding, and the shape asked
// for), so the preview has the saved picture's proportions and fits, zooms and pans as one.

using Avalonia;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using SkiaSharp;

namespace Filee.App.Controls;

/// <summary>Shows the result of the add background tool: the picture set on its backdrop.</summary>
public sealed class BackgroundCanvas : PictureView
{
    /// <summary>The whole result's size, in pixels of the file (not of the smaller preview picture).</summary>
    protected override Size ContentSize => OutputSize(Picture, FullShortSide, Edit?.Background);

    /// <summary>Ctrl+1 shows the result pixel for pixel: its width, not the picture's.</summary>
    protected override double ActualSizeWidth(int fileWidth) => ContentSize.Width;

    /// <summary>
    /// The size of the saved picture for a <paramref name="picture"/> (a preview of a file whose short side is
    /// <paramref name="fullShortSide"/>) set on <paramref name="background"/>; empty without a picture. It is laid out at
    /// the file's size, as the renderer does, so the shape is the saved one's down to the pixel.
    /// </summary>
    internal static Size OutputSize(SKImage? picture, int fullShortSide, BackgroundEdit? background)
    {
        if (picture is null)
            return default;
        var shortSide = Math.Min(picture.Width, picture.Height);
        var scale = fullShortSide > 0 ? (double)fullShortSide / shortSide : 1;
        var (size, _) = ImageEditRenderer.Layout((int)Math.Round(picture.Width * scale), (int)Math.Round(picture.Height * scale), background);
        return new Size(size.Width, size.Height);
    }
}
