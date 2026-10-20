// The donut's progress ring: timing rules and rendered states (running / ✓ / !).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;

namespace Filee.App.Tests;

[Collection("Motion")]
public class DonutProgressTests
{
    private const double Frame = 1 / 60.0;

    private static (Window Window, DonutMenu Donut) Create()
    {
        TestServices.EnsureInitialized("ko");
        var donut = new DonutMenu
        {
            Items = new[] { "PNG", "JPG", "WEBP", "PDF", "TIFF", "BMP" }.Select(l => new DonutItem { Id = l, Label = l }).ToList(),
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, donut);
    }

    /// <summary>Steps the animation clock until <paramref name="until"/> is true or the time budget runs out.</summary>
    private static double Run(DonutMenu donut, Func<bool> until, double maxSeconds = 10)
    {
        var elapsed = 0.0;
        while (!until() && elapsed < maxSeconds)
        {
            donut.Advance(Frame);
            elapsed += Frame;
        }
        return elapsed;
    }

    [AvaloniaFact]
    public void Fast_success_still_shows_the_ring_then_a_check_then_finishes()
    {
        var (window, donut) = Create();
        var done = false;

        donut.BeginProgress();
        donut.CompleteProgress(DonutProgressOutcome.Succeeded, () => done = true); // finished instantly

        var untilCheck = Run(donut, () => donut.ShownOutcome is not null);
        Assert.Equal(DonutProgressOutcome.Succeeded, donut.ShownOutcome);
        Assert.True(untilCheck >= 0.6, $"check appeared after {untilCheck:0.00}s; the ring must be visible for at least 0.6s");
        Assert.False(done);

        var untilDone = Run(donut, () => done);
        Assert.True(done);
        Assert.InRange(untilDone, 0.9, 1.6); // hold + fade
        window.Close();
    }

    [AvaloniaFact]
    public void Hand_off_to_the_toast_is_quick()
    {
        var (window, donut) = Create();
        var done = false;
        donut.BeginProgress();
        donut.SetProgress(0.3);
        Run(donut, () => false, 1.0);

        donut.HandOffProgress(() => done = true);
        var elapsed = Run(donut, () => done);

        Assert.True(done);
        Assert.True(elapsed < 0.4);
        window.Close();
    }

    [AvaloniaFact]
    public void Opening_the_donut_again_leaves_progress_mode()
    {
        var (window, donut) = Create();
        donut.BeginProgress();
        Assert.True(donut.IsInProgressMode);

        donut.PlayOpenAnimation();

        Assert.False(donut.IsInProgressMode);
        window.Close();
    }

    [AvaloniaFact]
    public void Reduced_motion_skips_animation_but_keeps_the_check_visible_for_a_moment()
    {
        var (window, donut) = Create(); // applies the theme, which sets Motion from settings: switch it off afterwards
        Motion.Set(false);
        try
        {
            var done = false;
            donut.BeginProgress();
            donut.CompleteProgress(DonutProgressOutcome.Succeeded, () => done = true);

            donut.Advance(Frame);
            Assert.False(done);
            var elapsed = Run(donut, () => done);
            Assert.True(done);
            Assert.InRange(elapsed, 1.0, 2.0); // min ring time + hold, no animation time
            window.Close();
        }
        finally
        {
            Motion.Set(true);
        }
    }

    [AvaloniaFact]
    public void Ring_states_render()
    {
        var (window, donut) = Create();

        donut.BeginProgress();
        donut.SetProgress(0.42);
        Run(donut, () => false, 1.2);
        Save(window, "progress-running.png");

        donut.CompleteProgress(DonutProgressOutcome.Succeeded, () => { });
        Run(donut, () => donut.ShownOutcome is not null);
        Run(donut, () => false, 0.4); // let the ✓ pop settle
        Save(window, "progress-success.png");

        donut.BeginProgress();
        donut.SetProgress(0.6);
        Run(donut, () => false, 1.0);
        donut.CompleteProgress(DonutProgressOutcome.Failed, () => { });
        Run(donut, () => donut.ShownOutcome is not null);
        Run(donut, () => false, 0.4);
        Save(window, "progress-failed.png");

        // Mid-collapse frame, to see the slices being sucked in.
        donut.PlayOpenAnimation();
        Run(donut, () => false, 0.6);
        donut.BeginProgress();
        Run(donut, () => false, 0.15);
        Save(window, "progress-collapsing.png");
        window.Close();
    }

    /// <summary>
    /// The check and exclamation marks sit on the success or danger disc, not on the accent: they must read on the
    /// disc whatever the accent's text colour is (dark brown under Peach Glass would be ~2.7:1 on light mode's green and red).
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_outcome_glyph_reads_on_its_disc(string variant)
    {
        TestServices.EnsureInitialized("ko");
        var theme = variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        foreach (var key in new[] { "SuccessBrush", "DangerBrush" })
        {
            Assert.True(Application.Current!.TryGetResource(key, theme, out var value));
            var disc = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
            var glyph = Assert.IsAssignableFrom<ISolidColorBrush>(DonutMenu.BadgeGlyphBrush((IBrush)value!)).Color;
            Assert.True(Contrast(glyph, disc) >= 4.5, $"{key} {variant}: {glyph} on {disc} is {Contrast(glyph, disc):0.0}:1");
            if (variant == "Light")
                Assert.Equal(Colors.White, glyph); // as before Peach Glass
        }
    }

    /// <summary>The drawn badge uses that glyph colour: an accent text colour nobody can read on the disc never shows up.</summary>
    [AvaloniaTheory]
    [InlineData(DonutProgressOutcome.Succeeded)]
    [InlineData(DonutProgressOutcome.Failed)]
    public void The_drawn_outcome_badge_ignores_the_accents_text_colour(DonutProgressOutcome outcome)
    {
        var (window, donut) = Create();
        donut.OnAccentBrush = Brushes.Magenta; // a colour nothing else in the donut uses
        donut.SuccessBrush = new SolidColorBrush(Color.Parse("#0F7A5A"));
        donut.DangerBrush = new SolidColorBrush(Color.Parse("#C73A39"));
        donut.BeginProgress();
        donut.CompleteProgress(outcome, () => { });
        Run(donut, () => donut.ShownOutcome is not null);
        Run(donut, () => false, 0.4);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        int magenta = 0, white = 0;
        using (var frame = window.CaptureRenderedFrame()!)
        using (var buffer = frame.Lock())
        {
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            var rgba = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            var middle = donut.Bounds.Center;
            for (var y = (int)middle.Y - 18; y <= (int)middle.Y + 18; y++)
                for (var x = (int)middle.X - 18; x <= (int)middle.X + 18; x++)
                {
                    var i = y * buffer.RowBytes + x * 4;
                    var (r, g, b) = rgba ? (bytes[i], bytes[i + 1], bytes[i + 2]) : (bytes[i + 2], bytes[i + 1], bytes[i]);
                    if (r > 200 && b > 200 && g < 90)
                        magenta++;
                    else if (r > 230 && g > 230 && b > 230)
                        white++;
                }
        }
        Assert.Equal(0, magenta);
        Assert.True(white > 20, $"{white} white glyph pixels");
        window.Close();
    }

    /// <summary>WCAG contrast ratio of two colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Lin(byte v) => v / 255.0 is var c && c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double L(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new PngBitmapEncoderOptions());
    }
}
