// How the donut moves: the centre leading the slices open, the whole donut shrinking and fading as it closes, and the
// numbers that size a lit slice. Time is stepped by hand (Advance), so every frame here is the same on any machine.

using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;

namespace Filee.App.Tests;

[Collection("Motion")]
public class DonutMotionTests
{
    private const int Slices = 6;

    private static (Window Window, DonutMenu Donut) Create(bool motion = true)
    {
        TestServices.EnsureInitialized("ko");
        Motion.Configure(!motion, () => false); // whatever the machine reports: this is the user's choice
        var donut = new DonutMenu
        {
            Items = new[] { "PNG", "JPG", "WEBP", "PDF", "TIFF", "BMP" }.Select(l => new DonutItem { Id = l, Label = l }).ToList(),
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight, Background = Brushes.SteelBlue };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, donut);
    }

    /// <summary>
    /// Keeps the window's own frame loop out of it: a queued frame would add real time to the clock the test sets. The
    /// frame request the animation just made is for a loop number that is no longer the current one, so it is ignored,
    /// and the loop counts as having just had a frame (far into the future), so no animation call starts a new one.
    /// </summary>
    internal static void Freeze(DonutMenu donut)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var loop = typeof(DonutMenu).GetField("_loop", flags)!;
        loop.SetValue(donut, (int)loop.GetValue(donut)! + 1);
        typeof(DonutMenu).GetField("_lastTick", flags)!.SetValue(donut, Environment.TickCount64 + 3_600_000);
    }

    internal static void Save(Window window, string name)
    {
        (window.Content as Control)?.InvalidateVisual(); // the frame loop is frozen: nothing else asks for this frame
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>The donut's pixel at <paramref name="p"/> (premultiplied), drawn on its own, as on its see-through window.</summary>
    private static (byte R, byte G, byte B, byte A) PixelAt(DonutMenu donut, Point p)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)donut.Bounds.Width, (int)donut.Bounds.Height));
        bitmap.Render(donut);
        var pixel = new byte[4];
        var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect((int)p.X, (int)p.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    private static Point Center(DonutMenu donut) => new(donut.Bounds.Width / 2, donut.Bounds.Height / 2);

    [AvaloniaFact]
    public void Open_animation_frames()
    {
        var (window, donut) = Create();
        donut.PlayOpenAnimation();
        Freeze(donut);

        // 0.05 s: the centre circle is on its way, the slices have not started (they follow 0.06 s after it).
        donut.Advance(0.05);
        Assert.InRange(donut.CenterOpen, 0.01, 0.99);
        Assert.All(Enumerable.Range(0, Slices), i => Assert.Equal(0, donut.OpenAmount(i)));
        Save(window, "donut-open-0050ms.png");

        // 0.15 s: the first slice is popping out, the later ones lag behind it, the last has not started.
        donut.Advance(0.10);
        Assert.InRange(donut.OpenAmount(0), 0.01, 0.99);
        Assert.True(donut.OpenAmount(1) < donut.OpenAmount(0));
        Assert.Equal(0, donut.OpenAmount(Slices - 1));
        Save(window, "donut-open-0150ms.png");

        // 0.34 s: the centre is done, the slices are still going (the last, bouncing, is not at 1 yet).
        donut.Advance(0.19);
        Assert.InRange(donut.CenterOpen, 1 - 1e-6, 1 + 1e-6);
        Assert.True(Math.Abs(1 - donut.OpenAmount(Slices - 1)) > 1e-3, $"{donut.OpenAmount(Slices - 1):0.000}");
        Save(window, "donut-open-0340ms.png");

        // The first slice settles 0.06 + 0.34 s in; the last, which starts 5 * 0.022 s after it, 0.11 s later.
        donut.Advance(0.06);
        Assert.InRange(donut.OpenAmount(0), 1 - 1e-6, 1 + 1e-6);
        Assert.True(Math.Abs(1 - donut.OpenAmount(Slices - 1)) > 1e-3, $"{donut.OpenAmount(Slices - 1):0.000}");
        donut.Advance(0.022 * (Slices - 1));
        Assert.All(Enumerable.Range(0, Slices), i => Assert.InRange(donut.OpenAmount(i), 1 - 1e-6, 1 + 1e-6));
        Assert.False(donut.Advance(0.01)); // nothing left to animate
        Save(window, "donut-open-done.png");
        window.Close();
    }

    [AvaloniaFact]
    public void The_donut_bounces_less_than_before_when_it_opens()
    {
        var (window, donut) = Create();
        donut.PlayOpenAnimation();
        Freeze(donut);
        var highest = 0.0;
        for (var i = 0; i < 60; i++)
        {
            donut.Advance(1 / 120.0);
            highest = Math.Max(highest, donut.OpenAmount(0));
        }
        // The bounce overshoots by about 6 % (the old one, c1 = 1.55, by about 8.5 %).
        Assert.InRange(highest, 1.04, 1.07);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_shrinks_and_fades_the_whole_donut_in_a_fifth_of_a_second()
    {
        var (window, donut) = Create();
        var rim = new Point(Center(donut).X, Center(donut).Y - donut.OuterRadius + 6); // on the top slice, clear of its label
        Assert.True(PixelAt(donut, rim).A > 150); // open: opaque there
        var done = 0;
        donut.PlayCloseAnimation(() => done++);
        Freeze(donut);

        donut.Advance(0.09);
        Assert.InRange(donut.CloseAmount, 0.01, 0.99);
        Assert.Equal(0, done);
        Save(window, "donut-closing.png");

        donut.Advance(0.08); // 0.17 s: not yet; the slices are not collapsed one by one any more, the donut goes as one
        Assert.Equal(0, done);

        donut.Advance(0.02); // 0.19 s: the old close, 0.16 s plus 0.01 s per slice, was still going
        Assert.Equal(1, done);
        Assert.Equal(1, donut.CloseAmount);
        Assert.Equal(0, PixelAt(donut, rim).A); // gone
        donut.Advance(0.1);
        Assert.Equal(1, done); // told once
        window.Close();
    }

    [AvaloniaFact]
    public void Opening_again_after_a_close_brings_the_donut_back()
    {
        var (window, donut) = Create();
        donut.PlayCloseAnimation(() => { });
        Freeze(donut);
        donut.Advance(0.2);
        Assert.Equal(1, donut.CloseAmount);

        donut.PlayOpenAnimation();
        Assert.Equal(0, donut.CloseAmount);
        Freeze(donut);
        donut.Advance(1);
        Assert.Equal(0, donut.CloseAmount);
        Assert.All(Enumerable.Range(0, Slices), i => Assert.Equal(1, donut.OpenAmount(i), 6));
        window.Close();
    }

    [AvaloniaFact]
    public void With_animations_off_the_donut_is_open_at_once_and_closes_at_once()
    {
        var (window, donut) = Create(motion: false);
        try
        {
            donut.PlayOpenAnimation();
            Assert.Equal(1, donut.CenterOpen);
            Assert.All(Enumerable.Range(0, Slices), i => Assert.Equal(1, donut.OpenAmount(i)));

            var closed = false;
            donut.PlayCloseAnimation(() => closed = true);
            Assert.True(closed);
        }
        finally
        {
            Motion.Configure(false, () => false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_lit_slice_pops_out_9_px_and_its_icon_follows_the_highlight()
    {
        var (window, donut) = Create();
        Assert.Equal(9, DonutMenu.PopOut);
        Assert.Equal(2 * (donut.OuterRadius + 9 + 14), DonutMenu.SideFor(donut.OuterRadius, subRing: false));

        // The slice at the top reaches 9 px past the rim when lit, and the hit test allows for it.
        var top = Center(donut);
        Assert.Equal(DonutHitKind.Segment, donut.HitTest(new Point(top.X, top.Y - donut.OuterRadius - 8)).Kind);
        Assert.Equal(DonutHitKind.Outside, donut.HitTest(new Point(top.X, top.Y - donut.OuterRadius - 10)).Kind);

        // The icon grows 12 % when the slice is fully lit, and by the same share on the way there.
        Assert.Equal(1, DonutMenu.IconScale(0));
        Assert.Equal(1.12, DonutMenu.IconScale(1), 9);
        donut.SetExternalHighlight(1);
        donut.Advance(1 / 60.0);
        var lit = donut.LitAmount(1);
        Assert.InRange(lit, 0.001, 0.999);
        Assert.Equal(1 + 0.12 * lit, DonutMenu.IconScale(lit), 9);
        window.Close();
    }

    /// <summary>
    /// The icon of a lit slice grows 12 %, a disabled slice's stays: it pops out and grows a little like any slice, but
    /// does not light up. The icon is a solid square in the colour of the label, so its width is read off the pixels.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Only_the_icon_of_an_enabled_slice_grows_when_it_is_lit(bool enabled, bool grows)
    {
        TestServices.EnsureInitialized("ko");
        Motion.Configure(false, () => false);
        var square = Geometry.Parse("M0,0 H24 V24 H0 Z");
        var donut = new DonutMenu
        {
            Items = new[] { "PNG", "JPG", "WEBP", "PDF", "TIFF", "BMP" }
                .Select((l, i) => new DonutItem { Id = l, Label = l, Icon = square, IsEnabled = i != 0 || enabled }).ToList(),
            SecondaryForeground = Brushes.Magenta, // a disabled slice's name and icon
            OnAccentBrush = Brushes.Magenta,       // a lit one's
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        donut.SetExternalHighlight(0); // the slice at the top
        for (var i = 0; i < 120; i++)
            donut.Advance(1 / 60.0);
        Assert.Equal(1, donut.LitAmount(0), 3);

        var (pixels, width) = Read(donut);
        bool Magenta(int x, int y) => pixels[(y * width + x) * 4 + 2] > 200 && pixels[(y * width + x) * 4] > 200 && pixels[(y * width + x) * 4 + 1] < 100;
        var middle = (int)donut.Bounds.Width / 2;
        var top = Enumerable.Range(0, (int)donut.Bounds.Height / 2)
            .First(y => Enumerable.Range(middle - 40, 80).Count(x => Magenta(x, y)) >= 5);
        var across = Enumerable.Range(0, width).Count(x => Magenta(x, top + 4)); // inside the square, above the name

        // 18 px at the slice's own size of 1.035: 18.6, and with the icon's own 12 % 20.9.
        if (grows)
            Assert.InRange(across, 20, 23);
        else
            Assert.InRange(across, 17, 19);
        window.Close();
    }

    private static (byte[] Pixels, int Width) Read(DonutMenu donut)
    {
        Dispatcher.UIThread.RunJobs();
        var (width, height) = ((int)donut.Bounds.Width, (int)donut.Bounds.Height);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(donut);
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }
        return (pixels, width);
    }

    [AvaloniaFact]
    public void The_glass_stays_where_it_is_while_the_donut_shrinks_as_it_closes()
    {
        var (window, donut) = Create();
        var center = Center(donut);
        var side = (int)donut.Bounds.Width;
        // A point on the outer part of the slice at 300 degrees, clear of its label, and a picture whose red and blue
        // meet 3 px to its left: it shows blue there. A glass that shrank with the donut would show the picture from
        // 6 px further out, which is red.
        var point = DonutGeometry.PointAt(center, donut.OuterRadius - 10, DonutGeometry.MidAngle(5, Slices) - 0.35);
        var seam = (int)point.X - 3;
        donut.SliceBrush = Brushes.Transparent; // nothing between the glass and the eye
        donut.IsGlass = true;
        donut.GlassBackdrop = RedLeftOfBlueRight(side, seam);

        var (openR, _, openB, _) = PixelAt(donut, point);
        Assert.True(openB > openR + 60, $"open: R {openR} B {openB}");

        donut.PlayCloseAnimation(() => { });
        Freeze(donut);
        donut.Advance(0.143); // half way through the fade: the donut is at 0.96 of its size
        Assert.InRange(donut.CloseAmount, 0.4, 0.6);
        var (r, _, b, a) = PixelAt(donut, point);
        Assert.True(a > 60, $"faded out too far: A {a}");
        Assert.True(b > r + 30, $"the picture moved: R {r} B {b}");
        window.Close();
    }

    /// <summary>A picture the size of the window: red left of <paramref name="seam"/>, blue from there on. Sharp, unlike the real, blurred glass.</summary>
    private static WriteableBitmap RedLeftOfBlueRight(int side, int seam)
    {
        var bitmap = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var row = new byte[side * 4];
        for (var x = 0; x < side; x++)
        {
            row[x * 4 + 3] = 255; // B G R A
            row[x * 4 + (x < seam ? 2 : 0)] = 255;
        }
        for (var y = 0; y < side; y++)
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        return bitmap;
    }
}
