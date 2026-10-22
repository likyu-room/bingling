// The eyedropper's magnifier beside the cursor: 13 × 13 pixels of a screen's picture in a 120 DIP circle, the middle one
// (the cursor's) outlined, and its HEX code under the circle. The instant eyedropper draws it, and the capture window's.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Filee.App.Services;
using Filee.Core.Formats;

namespace Filee.App.Controls;

public sealed class Magnifier : Control
{
    /// <summary>Pixels across (and down) the circle; odd, so one is in the middle.</summary>
    public const int Cells = 13;

    /// <summary>The circle's diameter (DIPs).</summary>
    public const double Diameter = 120;

    private const double LabelGap = 6, LabelWidth = 76, LabelHeight = 22;

    /// <summary>How far from the cursor the magnifier sits (DIPs).</summary>
    private const double Offset = 24;

    private static readonly IBrush Outside = new ImmutableSolidColorBrush(Color.FromRgb(20, 16, 14));
    private static readonly IBrush LabelBack = new ImmutableSolidColorBrush(Color.FromArgb(217, 20, 16, 14));
    private static readonly IPen Ring = new ImmutablePen(Brushes.White, 3);
    private static readonly IPen RingEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);
    private static readonly IPen Middle = new ImmutablePen(Brushes.Black, 1.5);

    private ScreenShot? _shot;
    private PixelPoint _pixel;

    public Magnifier()
    {
        Width = Diameter;
        Height = Diameter + LabelGap + LabelHeight;
        IsHitTestVisible = false;
    }

    /// <summary>The screen's picture it magnifies.</summary>
    public ScreenShot? Shot
    {
        get => _shot;
        set
        {
            _shot = value;
            InvalidateVisual();
        }
    }

    /// <summary>The picture's pixel in the middle (the one under the cursor).</summary>
    public PixelPoint Pixel
    {
        get => _pixel;
        set
        {
            _pixel = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The magnifier's top-left corner for a <paramref name="cursor"/> in an <paramref name="area"/> (DIPs): above and to
    /// the right of it, left of it near the right edge and below it near the top, so it never covers what is picked.
    /// </summary>
    public static Point PlaceBeside(Point cursor, Size area)
    {
        const double width = Diameter, height = Diameter + LabelGap + LabelHeight;
        var x = cursor.X + Offset + width <= area.Width ? cursor.X + Offset : cursor.X - Offset - width;
        var y = cursor.Y - Offset - height >= 0 ? cursor.Y - Offset - height : cursor.Y + Offset;
        return new Point(x, y);
    }

    /// <summary>
    /// Magnifies the picture's <paramref name="pixel"/> and moves beside the <paramref name="cursor"/> on the canvas it is on,
    /// an <paramref name="area"/> big (DIPs).
    /// </summary>
    public void Follow(Point cursor, PixelPoint pixel, Size area)
    {
        Pixel = pixel;
        var at = PlaceBeside(cursor, area);
        Canvas.SetLeft(this, at.X);
        Canvas.SetTop(this, at.Y);
    }

    public override void Render(DrawingContext context)
    {
        if (_shot is not { } shot)
            return;
        var circle = new Rect(0, 0, Diameter, Diameter);
        const double cell = Diameter / Cells;
        const int half = Cells / 2;
        using (context.PushGeometryClip(new EllipseGeometry(circle)))
        {
            context.FillRectangle(Outside, circle); // off the screen's edge
            for (var row = 0; row < Cells; row++)
            for (var column = 0; column < Cells; column++)
            {
                if (shot.ColorAt(new PixelPoint(_pixel.X - half + column, _pixel.Y - half + row)) is { } color)
                    // A little bigger than a cell, so no seam of the background shows between two cells.
                    context.FillRectangle(new ImmutableSolidColorBrush(color), new Rect(column * cell, row * cell, cell + .5, cell + .5));
            }
        }
        context.DrawRectangle(Middle, new Rect(half * cell, half * cell, cell, cell));
        context.DrawEllipse(null, Ring, circle.Center, Diameter / 2, Diameter / 2);
        context.DrawEllipse(null, RingEdge, circle.Center, Diameter / 2 + 2, Diameter / 2 + 2);

        if (Caption is not { } caption)
            return;
        var text = new FormattedText(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.SemiBold), 12, Brushes.White);
        // As wide as the HEX code needs, or more for a longer word ("Transparente").
        var width = Math.Min(Diameter, Math.Max(LabelWidth, text.Width + 20));
        var label = new Rect((Diameter - width) / 2, Diameter + LabelGap, width, LabelHeight);
        context.DrawRectangle(LabelBack, null, label, LabelHeight / 2, LabelHeight / 2);
        context.DrawText(text, new Point(label.Center.X - text.Width / 2, label.Center.Y - text.Height / 2));
    }

    /// <summary>
    /// What is written under the circle: the middle pixel's HEX code, or "transparent" where that pixel is see-through
    /// altogether (a picture in the editor; it has no colour to pick, and its HEX would read as black); null off the picture.
    /// </summary>
    internal string? Caption
    {
        get
        {
            if (_shot is not { } shot || shot.ColorAt(_pixel) is not { } middle)
                return null;
            return shot.IsClearAt(_pixel) ? Transparent() : ColorCodes.Hex(middle.R, middle.G, middle.B);
        }
    }

    private static string Transparent() =>
        Application.Current?.TryGetResource("tool.eyedropper_clear", null, out var word) == true && word is string text ? text : "—";
}
