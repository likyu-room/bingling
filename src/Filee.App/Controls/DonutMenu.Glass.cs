// Peach Glass: the screen behind the donut (captured before its window showed, blurred once: GlassBackdrop) drawn
// inside the slices, the centre and a group's ring, under their translucent peach, with a bright 1 px edge. Each shape
// keeps the picture where it lies on screen while it scales, turns or pops out. Without a picture the peach is flat.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Filee.App.Controls;

public sealed partial class DonutMenu
{
    public static readonly StyledProperty<IImage?> GlassBackdropProperty =
        AvaloniaProperty.Register<DonutMenu, IImage?>(nameof(GlassBackdrop));

    /// <summary>
    /// The blurred screen under the window, from the window's top-left corner, shown through the shapes while
    /// <see cref="IsGlass"/>; null leaves the flat translucent peach.
    /// </summary>
    public IImage? GlassBackdrop { get => GetValue(GlassBackdropProperty); set => SetValue(GlassBackdropProperty, value); }

    private static readonly IPen GlassEdge = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, 0.3), 1);

    private IImage? Glass => IsGlass ? GlassBackdrop : null;

    /// <summary>
    /// Draws the backdrop as it lies on screen, clipped to <paramref name="shape"/>, before the shape's colour.
    /// <paramref name="shapeTransform"/> is the transform the shape is drawn with: the picture is drawn through its
    /// inverse, so it stays where it is while the shape moves.
    /// </summary>
    /// <remarks>
    /// A clip and DrawImage, not an ImageBrush: with the brush a frame of the donut took about 50 times longer (Skia on
    /// the CPU), and with TileMode.None the brush painted nothing at an absolute DestinationRect.
    /// </remarks>
    private void DrawGlass(DrawingContext context, Geometry shape, Matrix shapeTransform)
    {
        if (Glass is not { } glass || !shapeTransform.TryInvert(out var inverse))
            return;
        using (context.PushGeometryClip(shape))
        using (context.PushTransform(inverse))
            context.DrawImage(glass, GlassArea(glass));
    }

    /// <summary>The bright edge round a glass shape, over its colour.</summary>
    private void DrawGlassEdge(DrawingContext context, Geometry shape)
    {
        if (Glass is not null)
            context.DrawGeometry(null, GlassEdge, shape);
    }

    /// <summary>The captured area in this control: it starts at the window's corner, (0, 0) unless the donut sits elsewhere.</summary>
    private Rect GlassArea(IImage glass)
    {
        var origin = TopLevel.GetTopLevel(this) is { } top ? this.TranslatePoint(default, top) ?? default : default;
        return new Rect(new Point(-origin.X, -origin.Y), glass.Size);
    }
}
