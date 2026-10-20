// The Peach Glass donut's backdrop: the screen captured where the donut shows (before it did), blurred once into the
// bitmap DonutMenu draws inside its slices (DonutMenu.Glass.cs).

using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Filee.Core.Platform;
using SkiaSharp;

namespace Filee.App.Services;

/// <summary>Blurs a <see cref="ScreenCapture"/> into the glass behind the donut.</summary>
public static class GlassBackdrop
{
    /// <summary>Blur sigma at 100 % scale; a higher scale blurs as many physical pixels more, so it looks the same.</summary>
    private const double Sigma = 20;

    /// <summary>
    /// Taking and blurring a 600 × 600 px capture should fit in this; a larger one gets as much more as it has pixels.
    /// Once a machine takes longer it blurs at half resolution from then on. The first blur of a run is not timed: it
    /// pays for compiling the code and starting Skia, and would switch every PC.
    /// </summary>
    private const double BudgetMs = 15;
    private const double BudgetPixels = 600 * 600;

    // UI thread only.
    private static bool _halfResolution;
    private static bool _warmedUp;

    /// <summary>True once an open took longer than the budget: the glass is blurred at half resolution from then on.</summary>
    public static bool HalfResolution => _halfResolution;

    /// <summary>A fresh run, for tests: full resolution, and the next blur is the first one.</summary>
    internal static void Reset() => (_halfResolution, _warmedUp) = (false, false);

    /// <summary>The blurred capture, or null when it is (nearly) all black: DRM video, the secure desktop.</summary>
    /// <param name="capture">The screen under the window, its top-left corner at the window's.</param>
    /// <param name="scaling">The screen's scale: the bitmap's size in device-independent pixels is the window's.</param>
    public static Bitmap? Create(ScreenCapture capture, double scaling) => Create(capture, scaling, 0, out _);

    /// <inheritdoc cref="Create(ScreenCapture, double)"/>
    /// <param name="capture">The screen under the window, its top-left corner at the window's.</param>
    /// <param name="scaling">The screen's scale: the bitmap's size in device-independent pixels is the window's.</param>
    /// <param name="captureMs">How long taking the capture took: the time budget is for taking and blurring together.</param>
    /// <param name="milliseconds">How long blurring took, for the log.</param>
    public static Bitmap? Create(ScreenCapture capture, double scaling, double captureMs, out double milliseconds)
    {
        var clock = Stopwatch.StartNew();
        milliseconds = 0;
        if (capture.Width <= 0 || capture.Height <= 0 || capture.Bgra.Length < capture.Width * capture.Height * 4 || IsBlack(capture))
            return null;

        // At half resolution the canvas is scaled down: Skia blurs the smaller picture (the sigma scaled with it), and
        // the donut stretches it back over the window, since its size in device-independent pixels stays the window's.
        var factor = _halfResolution ? 0.5 : 1;
        var width = Math.Max(1, (int)Math.Ceiling(capture.Width * factor));
        var height = Math.Max(1, (int)Math.Ceiling(capture.Height * factor));
        var dpi = 96 * scaling * factor;
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var image = SKImage.FromPixelCopy(new SKImageInfo(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Premul), capture.Bgra))
        using (var buffer = bitmap.Lock())
        using (var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), buffer.Address, buffer.RowBytes))
        using (var blur = SKImageFilter.CreateBlur((float)(Sigma * scaling), (float)(Sigma * scaling), SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { ImageFilter = blur })
        {
            surface.Canvas.Scale((float)factor);
            surface.Canvas.DrawImage(image, 0, 0, paint);
        }

        milliseconds = clock.Elapsed.TotalMilliseconds;
        // ponytail: one slow open after the first (a GC pause, say) switches for good; BudgetMs is the knob, an average of
        // opens if that bites.
        if (_warmedUp && (captureMs + milliseconds) * BudgetPixels / Math.Max(BudgetPixels, (double)capture.Width * capture.Height) > BudgetMs)
            _halfResolution = true;
        _warmedUp = true;
        return bitmap;
    }

    /// <summary>True when the average of the colour channels is below 1 of 255.</summary>
    private static bool IsBlack(ScreenCapture capture)
    {
        var bytes = capture.Bgra;
        long sum = 0;
        for (var i = 0; i < capture.Width * capture.Height * 4; i += 4)
            sum += bytes[i] + bytes[i + 1] + bytes[i + 2];
        return sum < 3L * capture.Width * capture.Height;
    }
}
