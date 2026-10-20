// The picture in the image tool window with the crop box over it: fitted to the control, darker outside the box,
// eight handles, a rule-of-thirds grid while dragging. A drag outside the box draws a new one, inside moves it, on a
// handle or an edge resizes it; a fixed ratio is kept (a free box keeps its own while Shift is down). The wheel zooms,
// Space + drag or the middle button pans. The box goes in and out as a CropRect (fractions of the picture), so it
// means the same at any zoom, on the small preview and on the full-size file.
//
// Drawn by hand like DonutMenu. The geometry is in small static methods (ImageToolWindowTests).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Filee.Core.Presets;

namespace Filee.App.Controls;

/// <summary>Shows a picture and lets the user draw, move and resize a crop box on it.</summary>
public sealed class ImageCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageCanvas, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<CropRect?> CropProperty =
        AvaloniaProperty.Register<ImageCanvas, CropRect?>(nameof(Crop), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Room around the fitted picture, so the handles on its edges are not cut off.</summary>
    internal const double Inset = 16;

    /// <summary>How far from an edge or a corner (in DIPs) the pointer still takes it.</summary>
    private const double Reach = 8;

    /// <summary>How far the pointer moves before a press becomes a drag: a click (or a double click) changes nothing.</summary>
    private const double DragThreshold = 3;

    private const double MinZoom = .2;
    private const double MaxZoom = 40;

    private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
    private static readonly IPen BoxPen = new Pen(Brushes.White, 1.5);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1);
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 1);

    /// <summary>The parts of the box the pointer can take: edges (a corner is two), the inside, or nothing.</summary>
    [Flags]
    internal enum Grip
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Move = 16,
    }

    /// <summary>A press in progress: what it took, where, and the box and pan it started from.</summary>
    private sealed record Press(Grip Grip, Point Start, CropRect Box, Vector Pan, bool Panning);

    private double _zoom = 1;
    private Vector _pan;
    private Press? _press;
    private CropRect? _live; // the box while it is dragged; the Crop property gets it on release
    private Grip? _hover;

    static ImageCanvas()
    {
        AffectsRender<ImageCanvas>(SourceProperty, CropProperty);
        FocusableProperty.OverrideDefaultValue<ImageCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<ImageCanvas>(true);
    }

    public ImageCanvas() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

    /// <summary>The picture (a preview, maybe smaller than the file).</summary>
    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The box, as fractions of the picture; set back once per drag, when the button is released.</summary>
    public CropRect? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    /// <summary>Space is held: a left drag pans (set by the window, which sees the keys wherever the focus is).</summary>
    internal bool PanKeyDown { get; set; }

    /// <summary>Raised on a double click inside the box (the window saves).</summary>
    public event EventHandler? BoxDoubleClicked;

    /// <summary>Shows the whole picture again (Ctrl+0).</summary>
    public void Fit()
    {
        (_zoom, _pan) = (1, default);
        InvalidateVisual();
    }

    /// <summary>
    /// One pixel of the file on one pixel of the screen (Ctrl+1), around the box. <paramref name="pixelWidth"/>: the
    /// file's width, as the preview may be smaller.
    /// </summary>
    public void ShowActualSize(int pixelWidth)
    {
        if (Source is not { } source || pixelWidth <= 0)
            return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var fitted = ImageRect(Bounds.Size, SizeOf(source), 1, default);
        _zoom = Math.Clamp(pixelWidth / scaling / fitted.Width, MinZoom, MaxZoom);
        var image = ImageRect(Bounds.Size, SizeOf(source), _zoom, default);
        _pan = Crop is { } crop ? new Rect(Bounds.Size).Center - ToScreen(crop, image).Center : default;
        InvalidateVisual();
    }

    private static Size SizeOf(Bitmap source) => new(source.PixelSize.Width, source.PixelSize.Height);

    private Rect ImageBounds(Bitmap source) => ImageRect(Bounds.Size, SizeOf(source), _zoom, _pan);

    // ───────── Geometry ─────────

    /// <summary>
    /// Where the picture is drawn: fitted into <paramref name="bounds"/> less <see cref="Inset"/> and centred, then
    /// <paramref name="zoom"/> times as large around the centre (1 = fitted) and moved by <paramref name="pan"/>.
    /// </summary>
    internal static Rect ImageRect(Size bounds, Size image, double zoom, Vector pan)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return default;
        var fit = Math.Min(Math.Max(bounds.Width - 2 * Inset, 1) / image.Width, Math.Max(bounds.Height - 2 * Inset, 1) / image.Height);
        var (w, h) = (image.Width * fit * zoom, image.Height * fit * zoom);
        return new Rect(bounds.Width / 2 - w / 2 + pan.X, bounds.Height / 2 - h / 2 + pan.Y, w, h);
    }

    /// <summary>A point of the control as fractions of the picture drawn at <paramref name="image"/> (not clamped).</summary>
    internal static Point ToFraction(Point p, Rect image) =>
        new((p.X - image.X) / image.Width, (p.Y - image.Y) / image.Height);

    /// <summary>Where <paramref name="crop"/> is on the control, for the picture drawn at <paramref name="image"/>.</summary>
    internal static Rect ToScreen(CropRect crop, Rect image) =>
        new(image.X + crop.X * image.Width, image.Y + crop.Y * image.Height, crop.Width * image.Width, crop.Height * image.Height);

    /// <summary>The pan that keeps the picture point under <paramref name="at"/> in place when the zoom becomes <paramref name="newZoom"/>.</summary>
    internal static Vector ZoomAt(Size bounds, Size image, double zoom, Vector pan, Point at, double newZoom)
    {
        var u = ToFraction(at, ImageRect(bounds, image, zoom, pan));
        var unpanned = ImageRect(bounds, image, newZoom, default);
        return at - new Point(unpanned.X + u.X * unpanned.Width, unpanned.Y + u.Y * unpanned.Height);
    }

    /// <summary>
    /// What a press at <paramref name="p"/> takes of <paramref name="box"/> (on the control): a corner, an edge (anywhere
    /// along it, within <paramref name="reach"/>), the inside, or nothing. On a box too small to tell, the nearer edge.
    /// </summary>
    internal static Grip HitTest(Rect box, Point p, double reach)
    {
        if (!box.Inflate(reach).Contains(p))
            return Grip.None;
        var grip = Grip.None;
        var (left, right) = (Math.Abs(p.X - box.Left), Math.Abs(p.X - box.Right));
        var (top, bottom) = (Math.Abs(p.Y - box.Top), Math.Abs(p.Y - box.Bottom));
        if (Math.Min(left, right) <= reach)
            grip |= left <= right ? Grip.Left : Grip.Right;
        if (Math.Min(top, bottom) <= reach)
            grip |= top <= bottom ? Grip.Top : Grip.Bottom;
        return grip != Grip.None ? grip : box.Contains(p) ? Grip.Move : Grip.None;
    }

    /// <summary>
    /// <paramref name="start"/> after the edges of <paramref name="grip"/> were dragged to <paramref name="to"/> (fractions;
    /// past the opposite edge the box turns over). With a <paramref name="ratio"/> (width / height as fractions of the
    /// picture) a corner keeps it from the opposite corner, the larger pull setting the size, and an edge grows the other
    /// side around the box's middle. The box never leaves the picture. A new box is drawn as a zero-size box at the
    /// press, dragged by its bottom-right corner.
    /// </summary>
    internal static CropRect Resize(CropRect start, Grip grip, Point to, double? ratio)
    {
        var (fx, fy) = (Math.Clamp(to.X, 0, 1), Math.Clamp(to.Y, 0, 1));
        var horizontal = (grip & (Grip.Left | Grip.Right)) != 0;
        var vertical = (grip & (Grip.Top | Grip.Bottom)) != 0;
        // The edges that stay where they are.
        var anchorX = grip.HasFlag(Grip.Left) ? start.X + start.Width : start.X;
        var anchorY = grip.HasFlag(Grip.Top) ? start.Y + start.Height : start.Y;

        if (ratio is not > 0)
        {
            var (left, width) = horizontal ? (Math.Min(anchorX, fx), Math.Abs(fx - anchorX)) : (start.X, start.Width);
            var (top, height) = vertical ? (Math.Min(anchorY, fy), Math.Abs(fy - anchorY)) : (start.Y, start.Height);
            return new CropRect { X = left, Y = top, Width = width, Height = height, Aspect = start.Aspect };
        }

        var r = ratio.Value;
        var (dirX, dirY) = (fx >= anchorX ? 1 : -1, fy >= anchorY ? 1 : -1);
        var (roomX, roomY) = (dirX > 0 ? 1 - anchorX : anchorX, dirY > 0 ? 1 - anchorY : anchorY);
        double w, h;
        if (horizontal && vertical)
        {
            w = Math.Max(Math.Abs(fx - anchorX), Math.Abs(fy - anchorY) * r);
            h = w / r;
        }
        else if (horizontal)
        {
            (w, h, roomY) = (Math.Abs(fx - anchorX), Math.Abs(fx - anchorX) / r, 1);
        }
        else
        {
            (h, w, roomX) = (Math.Abs(fy - anchorY), Math.Abs(fy - anchorY) * r, 1);
        }
        if (w > roomX)
            (w, h) = (roomX, roomX / r);
        if (h > roomY)
            (w, h) = (roomY * r, roomY);

        // A corner grows away from its anchor; an edge also grows the other way around the middle, pushed inside.
        var x = horizontal ? (dirX > 0 ? anchorX : anchorX - w) : Math.Clamp(start.X + start.Width / 2 - w / 2, 0, 1 - w);
        var y = vertical ? (dirY > 0 ? anchorY : anchorY - h) : Math.Clamp(start.Y + start.Height / 2 - h / 2, 0, 1 - h);
        return new CropRect { X = x, Y = y, Width = w, Height = h, Aspect = start.Aspect };
    }

    /// <summary><paramref name="start"/> moved by <paramref name="by"/> (fractions), stopping at the picture's edges.</summary>
    internal static CropRect Move(CropRect start, Vector by)
    {
        var moved = start.Clone();
        moved.X = Math.Clamp(start.X + by.X, 0, Math.Max(0, 1 - start.Width));
        moved.Y = Math.Clamp(start.Y + by.Y, 0, Math.Max(0, 1 - start.Height));
        return moved;
    }

    /// <summary>
    /// The ratio a drag keeps, as fractions of the picture: the fixed one, or with Shift the box's own (a square for a box
    /// with no size).
    /// </summary>
    private static double? FractionRatio(CropRect box, bool shift, Size image)
    {
        if (box.Aspect is { } aspect)
            return aspect * image.Height / image.Width;
        if (!shift)
            return null;
        return box.Width > 0 && box.Height > 0 ? box.Width / box.Height : image.Height / image.Width;
    }

    // ───────── Pointer ─────────

    /// <summary>What a press at <paramref name="p"/> takes. A box over the whole picture can't move: inside it, a drag draws a new one.</summary>
    private Grip GripAt(CropRect crop, Point p, Bitmap source)
    {
        var grip = HitTest(ToScreen(crop, ImageBounds(source)), p, Reach);
        return grip == Grip.Move && crop.Width >= .999 && crop.Height >= .999 ? Grip.None : grip;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Source is not { } source || Crop is not { } crop)
            return;
        var point = e.GetCurrentPoint(this);
        var p = point.Position;
        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && PanKeyDown))
        {
            _press = new Press(Grip.None, p, crop, _pan, Panning: true);
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && ToScreen(crop, ImageBounds(source)).Contains(p))
            {
                BoxDoubleClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            _press = new Press(GripAt(crop, p, source), p, crop, _pan, Panning: false);
        }
        else
        {
            return;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (Source is not { } source)
            return;
        if (_press is not { } press)
        {
            ShowCursor(PanKeyDown ? null : Crop is { } crop ? GripAt(crop, p, source) : Grip.None);
            return;
        }
        if (press.Panning)
        {
            _pan = press.Pan + (p - press.Start);
            InvalidateVisual();
            return;
        }
        if (_live is null && Point.Distance(p, press.Start) < DragThreshold)
            return;

        var image = ImageBounds(source);
        var to = ToFraction(p, image);
        var ratio = FractionRatio(press.Box, e.KeyModifiers.HasFlag(KeyModifiers.Shift), SizeOf(source));
        _live = press.Grip switch
        {
            Grip.Move => Move(press.Box, to - ToFraction(press.Start, image)),
            Grip.None => Resize(NewBox(ToFraction(press.Start, image), press.Box.Aspect), Grip.Right | Grip.Bottom, to, ratio),
            _ => Resize(press.Box, press.Grip, to, ratio),
        };
        InvalidateVisual();
    }

    /// <summary>A box of no size at <paramref name="at"/> (clamped onto the picture), to draw a new one from.</summary>
    private static CropRect NewBox(Point at, double? aspect) =>
        new() { X = Math.Clamp(at.X, 0, 1), Y = Math.Clamp(at.Y, 0, 1), Width = 0, Height = 0, Aspect = aspect };

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is null)
            return;
        var live = _live;
        EndPress();
        e.Pointer.Capture(null);
        if (live is not null)
            SetCurrentValue(CropProperty, live);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPress(); // lost mid-drag (another window took the mouse): the box stays as it was
    }

    private void EndPress()
    {
        _press = null;
        _live = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is not { } source)
            return;
        var zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), MinZoom, MaxZoom);
        _pan = ZoomAt(Bounds.Size, SizeOf(source), _zoom, _pan, e.GetPosition(this), zoom);
        _zoom = zoom;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>The pointer's shape for what it would take; null: the hand of Space + drag.</summary>
    private void ShowCursor(Grip? grip)
    {
        if (grip == _hover)
            return;
        _hover = grip;
        var shape = grip switch
        {
            null => StandardCursorType.Hand,
            Grip.Move => StandardCursorType.SizeAll,
            Grip.Left | Grip.Top => StandardCursorType.TopLeftCorner,
            Grip.Right | Grip.Top => StandardCursorType.TopRightCorner,
            Grip.Left | Grip.Bottom => StandardCursorType.BottomLeftCorner,
            Grip.Right | Grip.Bottom => StandardCursorType.BottomRightCorner,
            Grip.Left => StandardCursorType.LeftSide,
            Grip.Right => StandardCursorType.RightSide,
            Grip.Top => StandardCursorType.TopSide,
            Grip.Bottom => StandardCursorType.BottomSide,
            _ => StandardCursorType.Cross,
        };
        if (!Cursors.TryGetValue(shape, out var cursor))
            Cursors[shape] = cursor = new Cursor(shape);
        Cursor = cursor;
    }

    /// <summary>The pointer shapes in use, made once (the pointer changes shape at every edge it crosses).</summary>
    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    // ───────── Drawing ─────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // takes the pointer everywhere, also off the picture
        if (Source is not { } source)
            return;
        var image = ImageBounds(source);
        context.DrawImage(source, image);
        if ((_live ?? Crop) is not { } crop)
            return;

        var box = ToScreen(crop, image);
        // One shape with the box as a hole: four rectangles would leave seams where their soft edges meet.
        context.DrawGeometry(Shade, null, new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(image), new RectangleGeometry(box) },
        });

        if (_live is not null)
        {
            for (var i = 1; i <= 2; i++)
            {
                var x = box.Left + box.Width * i / 3;
                var y = box.Top + box.Height * i / 3;
                context.DrawLine(GridPen, new Point(x, box.Top), new Point(x, box.Bottom));
                context.DrawLine(GridPen, new Point(box.Left, y), new Point(box.Right, y));
            }
        }
        context.DrawRectangle(null, BoxPen, box);

        foreach (var x in new[] { box.Left, box.Center.X, box.Right })
        {
            foreach (var y in new[] { box.Top, box.Center.Y, box.Bottom })
            {
                if (x != box.Center.X || y != box.Center.Y)
                    context.DrawRectangle(Brushes.White, HandlePen, new Rect(x - 4, y - 4, 8, 8), 2, 2);
            }
        }
    }
}
