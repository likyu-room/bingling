// Peach Glass: the screen behind the donut, captured before its window shows, blurred once and drawn inside the
// slices, the centre and a group's ring. Without a capture (failed, black, window already up) the donut is flat peach.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Tests;

public class GlassTests
{
    [Fact]
    public void A_black_capture_gives_no_glass()
    {
        var black = new ScreenCapture(64, 64, new byte[64 * 64 * 4]);
        Assert.Null(GlassBackdrop.Create(black, 1));
    }

    [AvaloniaFact]
    public void Glass_shows_the_screen_behind_blurred_and_falls_back_to_flat_peach()
    {
        using var _ = PeachGlass();
        var vm = AppHost.Get<RadialViewModel>();
        vm.Load([ImageToolWindowTests.TestImages.Png(64, 64)], clickMode: true);
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        Pump();
        var donut = window.DonutControl;
        // The pop-in runs on the real clock, and 40 quick frames may not be enough of it: sample the finished donut.
        for (var i = 0; i < 120; i++)
            donut.Advance(1 / 60.0);
        var side = (int)donut.Bounds.Width;
        donut.GlassBackdrop = GlassBackdrop.Create(HalfRedHalfBlue(side, side), 1);
        Assert.NotNull(donut.GlassBackdrop);
        Pump();

        var (left, right) = RingPoints(donut);
        var withGlass = Snapshot(window);
        var l = withGlass.At(left);
        var r = withGlass.At(right);
        // The red half shows through the slice on the left, the blue half on the right: the picture is in place.
        Assert.True(l.R > l.B + 40 && r.B > r.R + 20, $"{l} / {r}");
        // Across the seam at the top it is blurred: a sharp picture would be red on one side and blue on the other.
        var (seamLeft, seamRight) = SeamPoints(donut);
        var sl = withGlass.At(seamLeft);
        var sr = withGlass.At(seamRight);
        Assert.True(Math.Abs(sl.B - sr.B) < 40 && Math.Abs(sl.R - sr.R) < 40, $"{sl} / {sr}");
        Save(window, "donut-glass.png");

        donut.GlassBackdrop = null;
        Pump();
        var flat = Snapshot(window);
        Assert.Equal(flat.At(left), flat.At(right), new ColorTolerance(6));
        window.Close();
    }

    [AvaloniaFact]
    public void The_screen_is_captured_before_the_window_shows_and_let_go_when_it_closes()
    {
        using var _ = PeachGlass();
        var platform = new CapturingPlatform();
        var radial = NewRadial(platform, AppHost.Get<ILogger<RadialController>>());
        platform.Window = () => radial.Window;
        string[] files = [ImageToolWindowTests.TestImages.Png(64, 64)];

        radial.ShowForFiles(400, 400, files);
        var donut = radial.Window!.DonutControl;
        Assert.Equal([false], platform.VisibleAtCapture);
        var vm = AppHost.Get<RadialViewModel>();
        Assert.Equal(DonutMenu.SideFor(vm.OuterRadius, vm.ReserveSubRing), platform.LastSize); // the whole window, at 100 %
        Assert.NotNull(donut.GlassBackdrop);

        // Already on screen: a capture would hold the donut itself, so it is flat peach this time.
        radial.ShowForFiles(420, 400, files);
        Assert.Equal([false], platform.VisibleAtCapture);
        Assert.Null(donut.GlassBackdrop);

        radial.Close(animate: false);
        radial.ShowForFiles(400, 400, files);
        Assert.Equal([false, false], platform.VisibleAtCapture);
        Assert.NotNull(donut.GlassBackdrop);
        radial.Close(animate: false);
        Assert.Null(donut.GlassBackdrop);

        // Classic draws no glass, so it captures nothing.
        AppHost.Get<UserDataStore>().Settings.Theme.Style = ThemeStyle.Classic;
        radial.ShowForFiles(400, 400, files);
        Assert.Equal(2, platform.VisibleAtCapture.Count);
        Assert.Null(donut.GlassBackdrop);
        radial.Close(animate: false);
    }

    [AvaloniaFact]
    public void A_drag_whose_glass_fails_opens_the_donut_on_flat_peach_instead_of_throwing_into_explorer()
    {
        using var _ = PeachGlass();
        var platform = new CapturingPlatform { Broken = true };
        var radial = NewRadial(platform, AppHost.Get<ILogger<RadialController>>());
        platform.Window = () => radial.Window;

        radial.ShowForDrag(400, 400);
        radial.OnFilesEntered([ImageToolWindowTests.TestImages.Png(64, 64)]); // OLE DragEnter: blurring this capture throws

        Assert.Single(platform.VisibleAtCapture);
        Assert.Null(radial.Window!.DonutControl.GlassBackdrop);
        Assert.True(AppHost.Get<RadialViewModel>().HasFiles); // the donut is there all the same
        radial.Close(animate: false);
    }

    [AvaloniaFact]
    public void A_slow_glass_is_forgiven_once_then_blurs_at_half_resolution_and_says_so_once()
    {
        using var _ = PeachGlass();
        GlassBackdrop.Reset();
        try
        {
            // Taking the screen alone takes longer than the 15 ms budget of a 600 × 600 px window.
            var platform = new CapturingPlatform { Delay = TimeSpan.FromMilliseconds(40) };
            var log = new ListLogger();
            var radial = NewRadial(platform, log);
            platform.Window = () => radial.Window;
            string[] files = [ImageToolWindowTests.TestImages.Png(64, 64)];

            for (var open = 1; open <= 3; open++)
            {
                radial.ShowForFiles(400, 400, files);
                Assert.NotNull(radial.Window!.DonutControl.GlassBackdrop);
                radial.Close(animate: false);
                // The first open of a run pays for starting up: only the second one counts.
                Assert.Equal(open >= 2, GlassBackdrop.HalfResolution);
            }
            var line = Assert.Single(log.Lines, l => l.Contains("half resolution"));
            Assert.StartsWith("Information: ", line);
            Assert.Contains("captured in", line);
        }
        finally
        {
            GlassBackdrop.Reset();
        }
    }

    private static RadialController NewRadial(IPlatformServices platform, ILogger<RadialController> log) =>
        new(AppHost.Get<RadialViewModel>(), AppHost.Get<ConversionService>(), AppHost.Get<WindowService>(),
            AppHost.Get<UserDataStore>(), platform, log, AppHost.Get<Filee.Core.Localization.ILocalizer>());

    /// <summary>A log that keeps its lines ("Level: message").</summary>
    private sealed class ListLogger : ILogger<RadialController>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }

    /// <summary>Switches to Peach Glass and back to the settings before when disposed.</summary>
    private static IDisposable PeachGlass()
    {
        TestServices.EnsureInitialized("ko");
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.Theme.Style;
        store.Settings.Theme.Style = ThemeStyle.PeachGlass;
        AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
        return new Restore(() =>
        {
            store.Settings.Theme.Style = before;
            AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>A screen whose left half is red and right half blue.</summary>
    private static ScreenCapture HalfRedHalfBlue(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bytes[i + 3] = 255; // B G R A
                if (x < width / 2)
                    bytes[i + 2] = 255;
                else
                    bytes[i] = 255;
            }
        return new ScreenCapture(width, height, bytes);
    }

    /// <summary>
    /// A point on the slice nearest 9 o'clock and its mirror image near 3 o'clock, in the outer part of the ring and a
    /// little above the slice's middle, away from its label. Both slices are plain ones, so the flat donut colours them alike.
    /// </summary>
    private static (Point Left, Point Right) RingPoints(DonutMenu donut)
    {
        var items = donut.Items!;
        var n = items.Count;
        var span = DonutGeometry.Span(n);
        bool Plain(int i) => items[i] is { IsEnabled: true, IsAccent: false };
        var slice = Enumerable.Range(1, n / 2).Where(i => i != n - i && Plain(i) && Plain(n - i))
            .OrderBy(i => Math.Abs(i * span - Math.PI / 2)).First();
        var center = new Point(donut.Bounds.Width / 2, donut.Bounds.Height / 2);
        var right = DonutGeometry.PointAt(center, donut.OuterRadius - 10, slice * span - span * 0.35);
        var left = new Point(2 * center.X - right.X, right.Y);
        return (right.X > left.X ? left : right, right.X > left.X ? right : left);
    }

    /// <summary>Two points 12 px apart either side of the red / blue seam, on the top slice's outer edge.</summary>
    private static (Point Left, Point Right) SeamPoints(DonutMenu donut)
    {
        var center = new Point(donut.Bounds.Width / 2, donut.Bounds.Height / 2);
        var y = center.Y - donut.OuterRadius + 10;
        return (new Point(center.X - 6, y), new Point(center.X + 6, y));
    }

    private static void Pump()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>The window as rendered now.</summary>
    private static Frame Snapshot(TopLevel window)
    {
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        using var buffer = bitmap.Lock();
        Assert.True(buffer.Format == PixelFormat.Bgra8888 || buffer.Format == PixelFormat.Rgba8888, $"{buffer.Format}");
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return new Frame(bytes, buffer.RowBytes, buffer.Format == PixelFormat.Rgba8888);
    }

    private sealed class Frame(byte[] bytes, int rowBytes, bool rgba)
    {
        public Color At(Point p)
        {
            var i = (int)p.Y * rowBytes + (int)p.X * 4;
            var (r, b) = rgba ? (bytes[i], bytes[i + 2]) : (bytes[i + 2], bytes[i]);
            return Color.FromArgb(bytes[i + 3], r, bytes[i + 1], b);
        }
    }

    private static void Save(TopLevel window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Colours equal within <paramref name="tolerance"/> per channel.</summary>
    private sealed class ColorTolerance(int tolerance) : IEqualityComparer<Color>
    {
        public bool Equals(Color a, Color b) =>
            Math.Abs(a.A - b.A) <= tolerance && Math.Abs(a.R - b.R) <= tolerance &&
            Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

        public int GetHashCode(Color color) => 0;
    }

    /// <summary>
    /// A platform whose screen is white, recording whether the donut was on screen at each capture. <see cref="Delay"/>
    /// makes taking it slow; <see cref="Broken"/> hands back a capture without pixels, which the blur can't read.
    /// </summary>
    private sealed class CapturingPlatform : IPlatformServices
    {
        public Func<Window?> Window { get; set; } = () => null;
        public List<bool> VisibleAtCapture { get; } = [];
        public double LastSize { get; private set; }
        public TimeSpan Delay { get; init; }
        public bool Broken { get; init; }

        public ScreenCapture? CaptureScreen(int x, int y, int width, int height)
        {
            VisibleAtCapture.Add(Window()?.IsVisible == true);
            LastSize = width;
            Thread.Sleep(Delay);
            if (Broken)
                return new ScreenCapture(width, height, null!);
            var bytes = new byte[width * height * 4];
            Array.Fill(bytes, (byte)255);
            return new ScreenCapture(width, height, bytes);
        }

        public bool IsFileManagerAt(int x, int y) => false;
        public string? ProcessNameAt(int x, int y) => null;
        public IReadOnlyList<string> GetFileManagerSelection() => [];
        public int SystemDragThreshold => 4;
        public bool PrefersReducedMotion => false;
        public void SetStartWithSystem(bool enabled, string executablePath) { }
        public void SetContextMenu(bool enabled, string executablePath, string label) { }
        public ModernContextMenuState GetModernContextMenuState(string executablePath) => ModernContextMenuState.Unsupported;
        public Task<ModernContextMenuResult> SetModernContextMenuAsync(bool enabled, string executablePath) => Task.FromResult(new ModernContextMenuResult(false));
        public void RevealInFileManager(string path) { }
        public void RaiseTopmost(nint windowHandle) { }
    }
}
