// The eyedropper of the picture view (the annotate and add background windows): while it is on, a magnifier follows the
// pointer over the picture and a press picks the colour under it instead of drawing. The colour is the picture as the
// window shows it, with the tool's edit drawn on it by the renderer that saves (the drawings, the backdrop): the pixels
// round the pointer are drawn again, a 13 × 13 square of them, at the result's own resolution (the saved picture's pixels,
// however big), so it is the same point at every zoom and screen scale (it goes by the pointer's place in the picture, not
// on the screen).

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Services;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using SkiaSharp;

namespace Filee.App.Controls;

public partial class PictureView
{
    /// <summary>Defines <see cref="Eyedropper"/>.</summary>
    public static readonly StyledProperty<bool> EyedropperProperty =
        AvaloniaProperty.Register<PictureView, bool>(nameof(Eyedropper));

    private static readonly PixelPoint LensMiddle = new(Magnifier.Cells / 2, Magnifier.Cells / 2);

    // Drawn by this view (Render), never laid out in it: it moves with the pointer, and has no place of its own.
    private readonly Magnifier _lens = new() { Pixel = LensMiddle };
    private ScreenShot? _around; // what the magnifier shows now: the pixels round the pointer
    private Point _pointer;

    /// <summary>A colour was picked off the picture (a left press while <see cref="Eyedropper"/> is on): the owner turns it off.</summary>
    public event Action<Color>? ColorPicked;

    /// <summary>The eyedropper is on: a magnifier follows the pointer, and a left press picks a colour instead of drawing.</summary>
    public bool Eyedropper
    {
        get => GetValue(EyedropperProperty);
        set => SetValue(EyedropperProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PictureProperty || change.Property == EditProperty)
            RefreshLens(); // another file or an undo: what is under the pointer is another, though the pointer did not move
        if (change.Property != EyedropperProperty)
            return;
        if (Eyedropper)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            RefreshLens();
        }
        else
        {
            Cursor = null;
            HideLens();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (Eyedropper)
            HideLens();
    }

    /// <summary>
    /// The pointer moved (or the wheel turned) at <paramref name="e"/>: the magnifier follows, and nothing else takes the move.
    /// <paramref name="remake"/> false when the picture moves too (a pan, a zoom) and <see cref="ViewChanged"/> will make the
    /// magnifier, once, for the picture where it is then.
    /// </summary>
    private void TrackEyedropper(PointerEventArgs e, bool remake = true)
    {
        _pointer = e.GetPosition(this);
        if (!Eyedropper)
            return;
        if (remake)
            UpdateLens();
        e.Handled = true;
    }

    /// <summary>
    /// What is under the pointer may have changed while it stayed still (another file, an undo, a zoom or a fit by the keys):
    /// the magnifier is made anew where the pointer last was, or is gone if the pointer is not over this view.
    /// </summary>
    protected void RefreshLens()
    {
        if (!Eyedropper)
            return;
        if (IsPointerOver)
            UpdateLens();
        else
            HideLens();
    }

    /// <summary>A press while the eyedropper is on: the colour under it is picked. The press is taken wherever it lands, so nothing is drawn or picked under it.</summary>
    private void PressEyedropper(PointerPressedEventArgs e, PointerPoint point)
    {
        if (!Eyedropper || !point.Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        Color? picked = null;
        using (var around = SampleAround(point.Position))
        {
            // A pixel see-through altogether has no colour (it reads as black, and the magnifier says "transparent"): nothing is
            // picked, the eyedropper stays on.
            if (around is not null && !around.IsClearAt(LensMiddle))
                picked = around.ColorAt(LensMiddle);
        }
        if (picked is { } color)
            ColorPicked?.Invoke(color);
    }

    private void UpdateLens()
    {
        var next = SampleAround(_pointer);
        _lens.Shot = next;
        _around?.Dispose();
        _around = next;
        InvalidateVisual();
    }

    private void HideLens()
    {
        _lens.Shot = null;
        _around?.Dispose();
        _around = null;
        InvalidateVisual();
    }

    private void DrawLens(DrawingContext context)
    {
        if (_around is null)
            return;
        var at = Magnifier.PlaceBeside(_pointer, Bounds.Size);
        using (context.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
            _lens.Render(context);
    }

    /// <summary>
    /// The picture as drawn, <see cref="Magnifier.Cells"/> pixels across, round the pixel of <paramref name="p"/> (a point of
    /// the control); null when <paramref name="p"/> is not on the picture. The middle pixel (<see cref="LensMiddle"/>) is the one under it.
    /// </summary>
    private ScreenShot? SampleAround(Point p)
    {
        var (content, shown) = (ContentSize, PictureRect);
        if (Picture is not { } picture || picture.Handle == IntPtr.Zero || content.Width < 1 || content.Height < 1 || !shown.Contains(p))
            return null;
        // The result at its own size, however big: only the 13 × 13 pixels round the pointer are drawn (the surface is that
        // big), so a big result costs no more, and a smaller grid would draw the picture shrunk and mix its pixels.
        var grid = new SKSizeI((int)Math.Round(content.Width), (int)Math.Round(content.Height));
        var at = ToFraction(p, shown);
        return Around(picture, EditToDraw ?? NoEdit, FullShortSide, grid,
            new SKPointI(Math.Clamp((int)(at.X * grid.Width), 0, grid.Width - 1), Math.Clamp((int)(at.Y * grid.Height), 0, grid.Height - 1)));
    }

    /// <summary>
    /// <paramref name="edit"/> drawn on <paramref name="picture"/> as the renderer draws a result <paramref name="grid"/> big, but
    /// only the <see cref="Magnifier.Cells"/> pixels round <paramref name="at"/> of it, which then are the middle of the shot.
    /// </summary>
    private static ScreenShot Around(SKImage picture, ImageEdit edit, int fullShortSide, SKSizeI grid, SKPointI at)
    {
        const int cells = Magnifier.Cells;
        using var surface = SKSurface.Create(new SKImageInfo(cells, cells));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(cells / 2 - at.X, cells / 2 - at.Y);
        ImageEditRenderer.Draw(canvas, picture, SKRect.Create(grid.Width, grid.Height), edit, fullShortSide);
        return new ScreenShot(surface.Snapshot(), new PixelRect(0, 0, cells, cells), 1);
    }
}
