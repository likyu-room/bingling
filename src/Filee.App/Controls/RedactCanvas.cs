// The picture in the redact window with its boxes: a drag on bare picture draws a box, a click picks one, a drag on it
// moves it, a drag on a handle or an edge of the picked one resizes it. What the boxes hide (black or mosaic) is drawn by
// the renderer that saves the picture; the picked box gets an outline and eight handles on top. Zooming and panning are
// PictureView's, and the box geometry is ImageCanvas's (Grip, HitTest, Resize, Move), used as it is on CropRects.
//
// The boxes go in and out as one RedactEdit (fractions of the picture), set back once per drag, when the button is let go.

using Avalonia;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Filee.Core.Presets;

namespace Filee.App.Controls;

/// <summary>Shows a picture and lets the user draw, pick, move and resize the boxes that hide parts of it.</summary>
public sealed class RedactCanvas : PictureView
{
    /// <summary>Defines <see cref="Redact"/>.</summary>
    public static readonly StyledProperty<RedactEdit?> RedactProperty =
        AvaloniaProperty.Register<RedactCanvas, RedactEdit?>(nameof(Redact), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines <see cref="SelectedIndex"/>.</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<RedactCanvas, int>(nameof(SelectedIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The least a box measures on each side, as a fraction of the picture: a twitch of the mouse is no box.</summary>
    internal const double MinBox = 0.005;

    private static readonly IPen OutlinePen = new Pen(Brushes.White, 1.5);
    private static readonly IPen OutlineShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1);

    /// <summary>A press in progress: the box it took (-1: it is drawing a new one), what of it, where, and the box it started from.</summary>
    private sealed record Press(int Index, ImageCanvas.Grip Grip, Point Start, CropRect Box);

    private Press? _press;
    private CropRect? _live; // the box while it is dragged; the Redact property gets it on release
    private ImageCanvas.Grip? _hover;

    static RedactCanvas() => AffectsRender<RedactCanvas>(RedactProperty, SelectedIndexProperty);

    /// <summary>The boxes and the way of hiding them, as fractions of the picture; set back once per drag.</summary>
    public RedactEdit? Redact
    {
        get => GetValue(RedactProperty);
        set => SetValue(RedactProperty, value);
    }

    /// <summary>The picked box, an index into the boxes; -1 for none.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The boxes drawn by the renderer, with the one being dragged where it is now.</summary>
    protected override ImageEdit? EditToDraw => new() { Redact = Shown().Redact };

    // ───────── Geometry ─────────

    private static CropRect AsCrop(EditRect box) => new() { X = box.X, Y = box.Y, Width = box.Width, Height = box.Height };

    private static EditRect AsBox(CropRect box) => new() { X = box.X, Y = box.Y, Width = box.Width, Height = box.Height };

    /// <summary>A box of no size at <paramref name="at"/> (clamped onto the picture), to draw a new one from.</summary>
    private static CropRect NewBox(Point at) => new() { X = Math.Clamp(at.X, 0, 1), Y = Math.Clamp(at.Y, 0, 1), Width = 0, Height = 0 };

    /// <summary>The boxes as they are on show, and which one is picked: while a drag goes on, it is the box being dragged.</summary>
    private (RedactEdit Redact, int Selected) Shown()
    {
        var redact = Redact?.Clone() ?? new RedactEdit();
        var selected = SelectedIndex;
        if (_press is { } press && _live is { } live)
        {
            if (press.Index >= 0 && press.Index < redact.Boxes.Count)
            {
                redact.Boxes[press.Index] = AsBox(live);
                selected = press.Index;
            }
            else
            {
                redact.Boxes.Add(AsBox(live));
                selected = redact.Boxes.Count - 1;
            }
        }
        return (redact, selected < redact.Boxes.Count ? selected : -1);
    }

    /// <summary>
    /// What a press at <paramref name="p"/> takes: the picked box first (its handles are the ones on show; a corner, an edge
    /// or the inside), then any other box under it, the last drawn on top, which can only be moved; else nothing, which
    /// draws a new box.
    /// </summary>
    private (int Index, ImageCanvas.Grip Grip) Take(Point p)
    {
        var boxes = Redact?.Boxes ?? [];
        var image = PictureRect;
        var selected = SelectedIndex;
        if (selected >= 0 && selected < boxes.Count)
        {
            var grip = ImageCanvas.HitTest(ImageCanvas.ToScreen(AsCrop(boxes[selected]), image), p, ImageCanvas.Reach);
            if (grip != ImageCanvas.Grip.None)
                return (selected, grip);
        }
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (i != selected && ImageCanvas.ToScreen(AsCrop(boxes[i]), image).Contains(p))
                return (i, ImageCanvas.Grip.Move);
        }
        return (-1, ImageCanvas.Grip.None);
    }

    // ───────── Pointer ─────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); // the focus; a pan
        if (e.Handled || Picture is null)
            return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        var p = point.Position;
        var (index, grip) = Take(p);
        // Picked at the press, so its handles show at once; a press on bare picture lets go of the box.
        SetCurrentValue(SelectedIndexProperty, index);
        _press = new Press(index, grip, p, index >= 0 ? AsCrop(Redact!.Boxes[index]) : NewBox(ToFraction(p, PictureRect)));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); // a pan
        if (e.Handled || Picture is null)
            return;
        var p = e.GetPosition(this);
        if (_press is not { } press)
        {
            ShowCursor(PanKeyDown ? null : Take(p).Grip);
            return;
        }
        if (_live is null && Point.Distance(p, press.Start) < ImageCanvas.DragThreshold)
            return;

        var image = PictureRect;
        var to = ToFraction(p, image);
        _live = press.Grip switch
        {
            ImageCanvas.Grip.Move => ImageCanvas.Move(press.Box, to - ToFraction(press.Start, image)),
            // A new box is drawn from where the press began, dragged by its bottom right corner.
            ImageCanvas.Grip.None => ImageCanvas.Resize(press.Box, ImageCanvas.Grip.Right | ImageCanvas.Grip.Bottom, to, null),
            _ => ImageCanvas.Resize(press.Box, press.Grip, to, null),
        };
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); // a pan
        if (e.Handled || _press is not { } press)
            return;
        var live = _live;
        EndPress();
        e.Pointer.Capture(null);
        if (live is not null)
            Commit(press.Index, live);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPress(); // lost mid-drag (another window took the mouse): the boxes stay as they were
    }

    private void EndPress()
    {
        _press = null;
        _live = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Puts the dragged box in the boxes (a new one at the end) and picks it. A box with a side under
    /// <see cref="MinBox"/> is not kept, so a drag that is no more than a twitch makes none and a resize cannot take one
    /// down to nothing: it stays as it was.
    /// </summary>
    private void Commit(int index, CropRect live)
    {
        if (live.Width < MinBox || live.Height < MinBox)
            return;
        var next = Redact?.Clone() ?? new RedactEdit();
        if (index >= 0 && index < next.Boxes.Count)
        {
            next.Boxes[index] = AsBox(live);
        }
        else
        {
            next.Boxes.Add(AsBox(live));
            index = next.Boxes.Count - 1;
        }
        SetCurrentValue(RedactProperty, next);
        SetCurrentValue(SelectedIndexProperty, index);
    }

    /// <summary>The pointer's shape for what it would take; null: the hand of Space + drag.</summary>
    private void ShowCursor(ImageCanvas.Grip? grip)
    {
        if (grip == _hover)
            return;
        _hover = grip;
        Cursor = ImageCanvas.CursorFor(grip);
    }

    // ───────── Drawing ─────────

    public override void Render(DrawingContext context)
    {
        base.Render(context); // the picture, with its boxes hiding what they hide
        if (Picture is null)
            return;
        var (redact, selected) = Shown();
        if (selected < 0)
            return;
        var box = ImageCanvas.ToScreen(AsCrop(redact.Boxes[selected]), PictureRect);
        // White over a dark line: the outline shows on a black box and on a bright picture alike.
        context.DrawRectangle(null, OutlineShadowPen, box.Inflate(1));
        context.DrawRectangle(null, OutlinePen, box);
        ImageCanvas.DrawHandles(context, box);
    }
}
