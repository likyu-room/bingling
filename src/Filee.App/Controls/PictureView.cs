// The picture in the image tool window, the part every tool shares: fitted to the control, zoomed with the wheel around
// the pointer, panned with Space + drag or the middle button, Fit (Ctrl+0) and actual size (Ctrl+1). It draws the
// preview picture with the tool's edit through the renderer that saves it; a tool's canvas (the crop box, say) inherits
// it and adds what it drags. With the eyedropper on (PictureView.Eyedropper.cs) a press picks a colour instead.
//
// The geometry is in small static methods (ImageToolWindowTests).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.Core.Presets;
using SkiaSharp;

namespace Filee.App.Controls;

/// <summary>Shows a picture (and an edit on it) that can be zoomed and panned.</summary>
public partial class PictureView : Control
{
    /// <summary>Defines <see cref="Picture"/>.</summary>
    public static readonly StyledProperty<SKImage?> PictureProperty =
        AvaloniaProperty.Register<PictureView, SKImage?>(nameof(Picture));

    /// <summary>Defines <see cref="Edit"/>.</summary>
    public static readonly StyledProperty<ImageEdit?> EditProperty =
        AvaloniaProperty.Register<PictureView, ImageEdit?>(nameof(Edit));

    /// <summary>Defines <see cref="FullShortSide"/>.</summary>
    public static readonly StyledProperty<int> FullShortSideProperty =
        AvaloniaProperty.Register<PictureView, int>(nameof(FullShortSide));

    /// <summary>Room around the fitted picture, so handles on its edges are not cut off.</summary>
    internal const double Inset = 16;

    private const double MinZoom = .2;
    private const double MaxZoom = 40;

    private static readonly ImageEdit NoEdit = new();

    private double _zoom = 1;
    private Vector _pan;
    private (Point Start, Vector Pan)? _panning; // a pan drag in progress: where it began, and the pan then

    static PictureView()
    {
        AffectsRender<PictureView>(PictureProperty, EditProperty, FullShortSideProperty);
        FocusableProperty.OverrideDefaultValue<PictureView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PictureView>(true);
    }

    /// <summary>The preview picture the edit is drawn on (upright, maybe smaller than the file).</summary>
    public SKImage? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>What the tool does to the picture, drawn as it will be saved; null draws the picture alone.</summary>
    public ImageEdit? Edit
    {
        get => GetValue(EditProperty);
        set => SetValue(EditProperty, value);
    }

    /// <summary>The file's short side in pixels (the picture may be smaller), which the renderer sizes the edit by.</summary>
    public int FullShortSide
    {
        get => GetValue(FullShortSideProperty);
        set => SetValue(FullShortSideProperty, value);
    }

    /// <summary>Space is held: a left drag pans (set by the window, which sees the keys wherever the focus is).</summary>
    internal bool PanKeyDown { get; set; }

    /// <summary>The size of what is drawn, in its own pixels: the picture's (the crop canvas draws a bitmap instead).</summary>
    protected virtual Size ContentSize => Picture is { } picture ? new Size(picture.Width, picture.Height) : default;

    /// <summary>
    /// What the next frame draws on the picture: <see cref="Edit"/>, unless a tool's canvas shows more than the view model
    /// has (the box being dragged, which is only the view model's once it is let go of).
    /// </summary>
    protected virtual ImageEdit? EditToDraw => Edit;

    /// <summary>Where the picture is drawn on the control now, at this zoom and pan.</summary>
    protected Rect PictureRect => PictureRectIn(Bounds.Size);

    /// <summary>Where the picture is drawn on a control of <paramref name="size"/> (while it is arranged, before its bounds are).</summary>
    protected Rect PictureRectIn(Size size) => ImageRect(size, ContentSize, _zoom, _pan);

    /// <summary>The point Ctrl+1 centres (on the picture drawn at <paramref name="picture"/>); null: the picture's centre.</summary>
    protected virtual Point? ActualSizeCentre(Rect picture) => null;

    /// <summary>
    /// How many pixels of the file <see cref="ContentSize"/>'s width stands for (<paramref name="fileWidth"/>: the file's
    /// own). A canvas that draws the picture on a backdrop shows more than the picture, so Ctrl+1 needs the result's width.
    /// </summary>
    protected virtual double ActualSizeWidth(int fileWidth) => fileWidth;

    /// <summary>Another file is about to be shown: a tool's canvas ends what it is in the middle of, on the file it was for.</summary>
    internal virtual void LeaveFile()
    {
    }

    /// <summary>
    /// A button is held on the picture: a pan, or a tool's box or drawing being dragged, which reaches the view model only
    /// when it is let go. Enter and Ctrl+C wait for that, or they would save or copy it where it was (ImageToolWindow).
    /// </summary>
    internal virtual bool Pressed => _panning is not null;

    /// <summary>Shows the whole picture again (Ctrl+0).</summary>
    public void Fit()
    {
        (_zoom, _pan) = (1, default);
        ViewChanged();
    }

    /// <summary>
    /// One pixel of the file on one pixel of the screen (Ctrl+1), around <see cref="ActualSizeCentre"/>.
    /// <paramref name="pixelWidth"/>: the file's width, as the picture may be smaller.
    /// </summary>
    public void ShowActualSize(int pixelWidth)
    {
        var content = ContentSize;
        if (content.Width <= 0 || pixelWidth <= 0)
            return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var fitted = ImageRect(Bounds.Size, content, 1, default);
        _zoom = Math.Clamp(ActualSizeWidth(pixelWidth) / scaling / fitted.Width, MinZoom, MaxZoom);
        var image = ImageRect(Bounds.Size, content, _zoom, default);
        _pan = ActualSizeCentre(image) is { } centre ? new Rect(Bounds.Size).Center - centre : default;
        ViewChanged();
    }

    /// <summary>The zoom or the pan changed: the picture is drawn anew, and what a canvas lays over it (a text box) is arranged anew.</summary>
    private void ViewChanged()
    {
        InvalidateVisual();
        InvalidateArrange();
        if (Eyedropper)
            UpdateLens(); // the pixel under the pointer is another now
    }

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

    /// <summary>The pan that keeps the picture point under <paramref name="at"/> in place when the zoom becomes <paramref name="newZoom"/>.</summary>
    internal static Vector ZoomAt(Size bounds, Size image, double zoom, Vector pan, Point at, double newZoom)
    {
        var u = ToFraction(at, ImageRect(bounds, image, zoom, pan));
        var unpanned = ImageRect(bounds, image, newZoom, default);
        return at - new Point(unpanned.X + u.X * unpanned.Width, unpanned.Y + u.Y * unpanned.Height);
    }

    // ───────── Pointer ─────────
    // A pan marks the event handled, so a tool's canvas (which calls these first) leaves it alone.

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (ContentSize.Width <= 0 || !(point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && PanKeyDown)))
        {
            PressEyedropper(e, point);
            return;
        }
        _panning = (point.Position, _pan);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        TrackEyedropper(e);
        if (_panning is not { } panning)
            return;
        _pan = panning.Pan + (e.GetPosition(this) - panning.Start);
        ViewChanged();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning is null)
            return;
        _panning = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panning = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var content = ContentSize;
        if (content.Width <= 0)
            return;
        TrackEyedropper(e);
        var zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), MinZoom, MaxZoom);
        _pan = ZoomAt(Bounds.Size, content, _zoom, _pan, e.GetPosition(this), zoom);
        _zoom = zoom;
        ViewChanged();
        e.Handled = true;
    }

    // ───────── Drawing ─────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // takes the pointer everywhere, also off the picture
        if (Picture is { } picture)
            context.Custom(new EditPreviewDrawOperation(PictureRect, picture, EditToDraw ?? NoEdit, FullShortSide));
        DrawLens(context);
    }
}
