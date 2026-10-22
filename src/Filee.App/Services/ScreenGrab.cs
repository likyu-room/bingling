// A picture of the whole screen the cursor is on, taken before a screen tool's window covers it: the eyedropper reads
// its colours, and the capture window shows it frozen.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Filee.Core.Platform;
using SkiaSharp;

namespace Filee.App.Services;

/// <summary>
/// A screen's picture: <see cref="Image"/> holds its physical pixels, <see cref="Bounds"/> is where the screen is on the
/// desktop (physical pixels) and <see cref="Scaling"/> its scale, so a window over it is <c>Bounds / Scaling</c> DIPs big.
/// </summary>
public sealed record ScreenShot(SKImage Image, PixelRect Bounds, double Scaling) : IDisposable
{
    private readonly SKPixmap _pixels = Image.PeekPixels();

    /// <summary>
    /// The colour of the picture's <paramref name="pixel"/> (0, 0 is the screen's top-left one); null off the picture, and
    /// once it is let go of (a window still drawing it would otherwise read freed memory).
    /// </summary>
    public Color? ColorAt(PixelPoint pixel)
    {
        if (_pixels.Handle == IntPtr.Zero || pixel.X < 0 || pixel.Y < 0 || pixel.X >= Image.Width || pixel.Y >= Image.Height)
            return null;
        var color = _pixels.GetPixelColor(pixel.X, pixel.Y);
        return Color.FromRgb(color.Red, color.Green, color.Blue);
    }

    /// <summary>
    /// The picture's <paramref name="pixel"/> is see-through altogether: it has no colour, though <see cref="ColorAt"/> reads it
    /// as black. A screen never is; a picture in the editor can be.
    /// </summary>
    public bool IsClearAt(PixelPoint pixel) =>
        _pixels.Handle != IntPtr.Zero && pixel.X >= 0 && pixel.Y >= 0 && pixel.X < Image.Width && pixel.Y < Image.Height
        && _pixels.GetPixelColor(pixel.X, pixel.Y).Alpha == 0;

    /// <summary>A copy of the picture's <paramref name="area"/> (its pixels, inside it): one of its own, kept once the shot is let go of.</summary>
    public SKImage Crop(PixelRect area)
    {
        using var part = _pixels.ExtractSubset(new SKRectI(area.X, area.Y, area.Right, area.Bottom));
        return SKImage.FromPixelCopy(part);
    }

    public void Dispose()
    {
        _pixels.Dispose();
        Image.Dispose();
    }
}

public static class ScreenGrab
{
    /// <summary>
    /// The screen under <paramref name="cursor"/> (physical pixels), all of it (not just its working area); the main screen
    /// when the cursor is on none (the input hook is off and reported 0, 0). Null when it can't be taken or comes out black
    /// (DRM video, the secure desktop): then there is nothing to show or pick from.
    /// </summary>
    public static ScreenShot? Grab(IPlatformServices platform, Screens screens, PixelPoint cursor) =>
        (screens.ScreenFromPoint(cursor) ?? screens.Primary) is { } screen ? Grab(platform, screen.Bounds, screen.Scaling) : null;

    /// <summary>The screen at <paramref name="bounds"/> (physical pixels) and of this <paramref name="scaling"/>.</summary>
    internal static ScreenShot? Grab(IPlatformServices platform, PixelRect bounds, double scaling)
    {
        if (platform.CaptureScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height) is not { } capture
            || capture.Width != bounds.Width || capture.Height != bounds.Height
            || capture.Bgra is null || capture.Bgra.Length < capture.Width * capture.Height * 4 || GlassBackdrop.IsBlack(capture))
            return null;
        var image = SKImage.FromPixelCopy(new SKImageInfo(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Premul), capture.Bgra);
        return image is null ? null : new ScreenShot(image, bounds, scaling);
    }
}
