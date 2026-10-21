// Pixel-level check that slice labels sit on their slice's centre line.
// Regression: labels were centred on the measured text width while being laid out centred in a wider box,
// so short labels drifted right (reported with a screenshot of the donut editor). Also where a long name breaks.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Filee.App.Controls;

namespace Filee.App.Tests;

public class DonutLabelPlacementTests
{
    private const double OuterRadius = 150;
    private const double HoleRatio = 0.42;

    [AvaloniaFact]
    public void Labels_are_centred_on_their_slices()
    {
        TestServices.EnsureInitialized("en");
        var donut = new DonutMenu
        {
            OuterRadius = OuterRadius,
            HoleRatio = HoleRatio,
            Items =
            [
                new DonutItem { Id = "a", Label = "PNG" },
                new DonutItem { Id = "b", Label = "JPG" },
                new DonutItem { Id = "c", Label = "PDF" },
                new DonutItem { Id = "d", Label = "ICO" },
            ],
            SliceBrush = Brushes.White,
            CenterBrush = Brushes.White,
            Foreground = Brushes.Black,
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight, Background = Brushes.White };
        window.Show();
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        using var frame = window.CaptureRenderedFrame()!;
        var (pixels, width, height) = Read(frame);
        var origin = donut.TranslatePoint(new Point(0, 0), window)!.Value;
        var center = new Point(origin.X + donut.Bounds.Width / 2, origin.Y + donut.Bounds.Height / 2);
        var labelRadius = (OuterRadius + OuterRadius * HoleRatio) / 2;

        for (var i = 0; i < 4; i++)
        {
            var anchor = DonutGeometry.PointAt(center, labelRadius, DonutGeometry.MidAngle(i, 4));
            var (cx, cy) = DarkCentroid(pixels, width, height, anchor, 34);
            Assert.True(Math.Abs(cx - anchor.X) < 3, $"slice {i}: text centre x {cx:0.0} vs slice centre {anchor.X:0.0}");
            Assert.True(Math.Abs(cy - anchor.Y) < 5, $"slice {i}: text centre y {cy:0.0} vs slice centre {anchor.Y:0.0}");
        }
        window.Close();
    }

    /// <summary>
    /// A slice with a picture draws it above its name, in the name's colour, and the two together stay centred on the
    /// slice; a slice without one is drawn as before.
    /// </summary>
    [AvaloniaFact]
    public void An_icon_is_drawn_above_the_label_of_its_slice()
    {
        TestServices.EnsureInitialized("en");
        var square = Geometry.Parse("M2,2H22V22H2Z");
        var donut = new DonutMenu
        {
            OuterRadius = OuterRadius,
            HoleRatio = HoleRatio,
            Items =
            [
                new DonutItem { Id = "a", Label = "PNG", Icon = square },
                new DonutItem { Id = "b", Label = "JPG" },
                new DonutItem { Id = "c", Label = "PDF", Icon = square },
                new DonutItem { Id = "d", Label = "ICO" },
            ],
            SliceBrush = Brushes.White,
            CenterBrush = Brushes.White,
            Foreground = Brushes.Black,
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight, Background = Brushes.White };
        window.Show();
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        using var frame = window.CaptureRenderedFrame()!;
        var (pixels, width, height) = Read(frame);
        var origin = donut.TranslatePoint(new Point(0, 0), window)!.Value;
        var center = new Point(origin.X + donut.Bounds.Width / 2, origin.Y + donut.Bounds.Height / 2);
        var labelRadius = (OuterRadius + OuterRadius * HoleRatio) / 2;

        var plain = DarkCount(pixels, width, height, DonutGeometry.PointAt(center, labelRadius, DonutGeometry.MidAngle(1, 4)), 34);
        foreach (var i in new[] { 0, 2 })
        {
            var anchor = DonutGeometry.PointAt(center, labelRadius, DonutGeometry.MidAngle(i, 4));
            var withIcon = DarkCount(pixels, width, height, anchor, 34);
            Assert.True(withIcon > plain + 200, $"slice {i}: {withIcon} dark pixels with the icon, {plain} for a plain label");
            // The 18 px square is the heavier part and sits on top: the dark pixels' centre is above the slice's centre.
            var (_, cy) = DarkCentroid(pixels, width, height, anchor, 34);
            Assert.True(cy < anchor.Y - 3, $"slice {i}: dark centre y {cy:0.0} vs slice centre {anchor.Y:0.0}");
        }
        window.Close();
    }

    /// <summary>The icon and the label of a slice of a full donut stay inside its wedge; where they would not, the icon is left out.</summary>
    [AvaloniaFact]
    public void Icon_and_label_together_fit_inside_a_slice_of_a_full_donut()
    {
        var center = new Point(300, 300);
        var shown = 0;
        var left = 0;
        var cases =
            from outer in new[] { 100.0, 120, 150, 200, 240 }
            from slices in new[] { 6, 8, 12 }
            from textHeight in new[] { 17.0, 34, 47 }
            from textWidth in new[] { 25.0, 60 }
            from i in Enumerable.Range(0, slices)
            select (outer, slices, textHeight, textWidth, i);
        foreach (var (outer, slices, textHeight, textWidth, i) in cases)
        {
            var inner = outer * HoleRatio;
            var mid = (inner + outer) / 2;
            var angle = DonutGeometry.MidAngle(i, slices);
            var anchor = DonutGeometry.PointAt(center, mid, angle);
            // What the main ring passes in (see DonutMenu.DrawLabel): the arc at mid radius, less 10, and the ring's thickness.
            var (iconShown, icon, text) = DonutMenu.LabelLayout(true, center, mid, angle,
                DonutGeometry.Span(slices) * mid - 10, outer - inner, textWidth, textHeight);

            var what = $"R{outer} x{slices} #{i} text {textWidth}x{textHeight}";
            if (iconShown)
            {
                shown++;
                Assert.Equal(18, icon.Width);
                Assert.Equal(18, icon.Height);
                Assert.True(icon.Bottom <= text.Top, $"{what}: icon over the label");
                Assert.Equal(anchor.Y, (icon.Top + text.Bottom) / 2, 6);
                Assert.Equal(anchor.X, icon.Center.X, 6);
                Assert.Equal(anchor.X, text.Center.X, 6);
                // The icon and the column it makes down through the name (the name itself may be wider than a slice, as before).
                foreach (var corner in new[] { icon.TopLeft, icon.TopRight, new Point(icon.Left, text.Bottom), new Point(icon.Right, text.Bottom) })
                {
                    var distance = Math.Sqrt(Math.Pow(corner.X - center.X, 2) + Math.Pow(corner.Y - center.Y, 2));
                    var off = Math.Abs(Math.IEEERemainder(DonutGeometry.AngleOf(center, corner) - angle, 2 * Math.PI));
                    Assert.True(distance >= inner && distance <= outer, $"{what}: {corner} is {distance:0.0} from the middle, the ring is {inner}-{outer}");
                    Assert.True(off <= DonutGeometry.Span(slices) / 2, $"{what}: {corner} is outside the wedge's sides");
                }
            }
            else
            {
                left++;
                Assert.Equal(anchor.Y - textHeight / 2, text.Top, 6);
                Assert.Equal(textHeight, text.Height, 6);
            }
        }
        Assert.True(shown > 0 && left > 0, $"the sweep should have both ({shown} shown, {left} left out)");

        // The smallest ring with all twelve slices: 27 px of slice at the most, no room for a picture. A bigger one has it.
        bool Shown(double outer, int slices, int index, double textWidth, double textHeight)
        {
            var mid = outer * (1 + HoleRatio) / 2;
            return DonutMenu.LabelLayout(true, center, mid, DonutGeometry.MidAngle(index, slices),
                DonutGeometry.Span(slices) * mid - 10, outer * (1 - HoleRatio), textWidth, textHeight).IconShown;
        }
        Assert.DoesNotContain(Enumerable.Range(0, 12), i => Shown(100, 12, i, 25, 17));
        Assert.True(Shown(150, 12, 0, 25, 17));
        // A name of two lines (tall) goes with an icon at the top of the donut, but not at its side, where the
        // slice is only as tall as it is wide.
        Assert.True(Shown(150, 12, 0, 25, 34));
        Assert.False(Shown(150, 12, 3, 25, 34));
        Assert.True(Shown(150, 12, 3, 25, 17));
    }

    [AvaloniaFact]
    public void The_icon_needs_28_px_of_slice_and_24_px_of_ring_beside_the_label_and_shows_bigger_when_hot()
    {
        // The top slice of a donut with a ring 60 thick, so the wedge itself is no limit.
        var center = new Point(300, 300);
        bool Shown(bool hasIcon, double sliceWidth, double textHeight) =>
            DonutMenu.LabelLayout(hasIcon, center, 200, 0, sliceWidth, 60, 18, textHeight).IconShown;
        Assert.True(Shown(true, 28, 36));
        Assert.False(Shown(true, 27.9, 36));
        Assert.False(Shown(true, 100, 36.5));
        Assert.False(Shown(false, 100, 17));

        var plain = DonutMenu.LabelLayout(true, center, 200, 0, 100, 100, 18, 17);
        var hot = DonutMenu.LabelLayout(true, center, 200, 0, 100, 100, 18, 17, iconScale: 1.12);
        Assert.True(plain.IconShown && hot.IconShown);
        Assert.Equal(18 * 1.12, hot.Icon.Width, 6);
        Assert.Equal(plain.Icon.Center.X, hot.Icon.Center.X, 6);
        Assert.Equal(plain.Icon.Center.Y, hot.Icon.Center.Y, 6);
        // Only the picture grows; the label stays where it is.
        Assert.Equal(plain.Text, hot.Text);
    }

    private static int DarkCount(byte[] bgra, int width, int height, Point at, int half)
    {
        var n = 0;
        for (var y = Math.Max(0, (int)at.Y - half); y < Math.Min(height, (int)at.Y + half); y++)
            for (var x = Math.Max(0, (int)at.X - half); x < Math.Min(width, (int)at.X + half); x++)
            {
                var o = (y * width + x) * 4;
                if (0.0722 * bgra[o] + 0.7152 * bgra[o + 1] + 0.2126 * bgra[o + 2] < 110)
                    n++;
            }
        return n;
    }

    /// <summary>Average position of dark (text) pixels in a square around <paramref name="at"/>.</summary>
    private static (double X, double Y) DarkCentroid(byte[] bgra, int width, int height, Point at, int half)
    {
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = Math.Max(0, (int)at.Y - half); y < Math.Min(height, (int)at.Y + half); y++)
        {
            for (var x = Math.Max(0, (int)at.X - half); x < Math.Min(width, (int)at.X + half); x++)
            {
                var o = (y * width + x) * 4;
                var luminance = 0.0722 * bgra[o] + 0.7152 * bgra[o + 1] + 0.2126 * bgra[o + 2];
                if (luminance < 110)
                {
                    sx += x;
                    sy += y;
                    n++;
                }
            }
        }
        Assert.True(n > 20, $"no label found near {at}");
        return (sx / n, sy / n);
    }

    /// <summary>
    /// A name too wide for its slice breaks at a space, whole words to a line; only a word too wide on its own is left to
    /// the text layout, which may break Korean between any two syllables. Measured here as 10 px a character.
    /// </summary>
    [AvaloniaTheory] // on the UI thread: DonutMenu's static fields belong to it
    [InlineData("50% 축소", 45, "50%\n축소")]   // not "50% 축" / "소"
    [InlineData("흑백 선화", 45, "흑백\n선화")]
    [InlineData("PDF 합치기", 70, "PDF 합치기")] // fits: as it is
    [InlineData("PNG", 20, "PNG")]               // one word: the layout's own business
    [InlineData("a bb ccc dddd", 60, "a bb\nccc dddd")] // the rest on the second line, where the two-line limit cuts it
    [InlineData("abcdefgh ij", 40, "abcdefgh\nij")]     // a first word too wide alone still keeps the next one off its line
    public void A_name_breaks_at_spaces_before_it_breaks_inside_a_word(string name, double width, string expected) =>
        Assert.Equal(expected, DonutMenu.WrapAtSpaces(name, width, text => text.Length * 10));

    /// <summary>
    /// The centre's "PNG 1개 · 620 B" breaks at the dot when it is too wide, so no dot is left hanging at the end of the
    /// first line. One line is as it was.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("PNG 1개 · 620 B", 100, "PNG 1개\n620 B")]       // 15 characters × 10 px against 100
    [InlineData("PNG 1개 · 620 B", 150, "PNG 1개 · 620 B")]      // fits: the spec's "1개 · JPG · 1.2 MB" shape stays
    [InlineData("1개 · JPG · 1.2 MB", 120, "1개 · JPG\n1.2 MB")] // the last dot is the one before the size
    [InlineData("파일 3개", 20, "파일 3개")]                        // no dot: the layout's own business
    [InlineData("", 20, "")]
    public void A_centre_title_breaks_at_its_last_dot_when_too_wide(string title, double width, string expected) =>
        Assert.Equal(expected, DonutMenu.BreakAtLastDot(title, width, text => text.Length * 10));

    private static (byte[] Pixels, int Width, int Height) Read(Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var width = buffer.Size.Width;
        var height = buffer.Size.Height;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * width * 4, width * 4);
        return (pixels, width, height);
    }
}
