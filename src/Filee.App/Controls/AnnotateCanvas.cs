// The picture in the annotate window with what is drawn on it. A press draws with the tool: a pen or highlighter path, a
// line, an arrow, a box or an ellipse dragged out (Shift: 45° steps, a square, a circle), a number sticker per click, or a
// text typed into a box laid over the picture (Enter puts it there, Shift+Enter starts a new line, Esc drops it). The
// select tool picks the drawing under the pointer (the top one first, a line within 6 DIPs), drags it along, and a double
// click on a text types it anew; so does a click on one with the text tool. Everything is drawn by the renderer that saves
// the picture, the drawing being dragged too; the picked one gets a dashed outline on top. Zooming and panning are
// PictureView's.
//
// The drawings go in and out as one list (fractions of the picture), set back once per drawing, drag or text. When the
// list changes under a drag or a text being typed (an undo, a delete, another file), that drag or text is dropped: it
// would otherwise land on a drawing that is no longer the one it took.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Filee.Core.Presets;
using Filee.Engines.Magick;

namespace Filee.App.Controls;

/// <summary>Shows a picture and lets the user draw on it, and pick, move, retype and delete what is drawn.</summary>
public sealed class AnnotateCanvas : PictureView
{
    /// <summary>Defines <see cref="Annotations"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<Annotation>?> AnnotationsProperty =
        AvaloniaProperty.Register<AnnotateCanvas, IReadOnlyList<Annotation>?>(nameof(Annotations), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines <see cref="SelectedIndex"/>.</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<AnnotateCanvas, int>(nameof(SelectedIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines <see cref="Tool"/>.</summary>
    public static readonly StyledProperty<AnnotationKind?> ToolProperty =
        AvaloniaProperty.Register<AnnotateCanvas, AnnotationKind?>(nameof(Tool));

    /// <summary>Defines <see cref="Color"/>.</summary>
    public static readonly StyledProperty<string> ColorProperty =
        AvaloniaProperty.Register<AnnotateCanvas, string>(nameof(Color), ImageEditRenderer.AnnotationColors[0]);

    /// <summary>Defines <see cref="StrokeWidth"/>.</summary>
    public static readonly StyledProperty<StrokeLevel> StrokeWidthProperty =
        AvaloniaProperty.Register<AnnotateCanvas, StrokeLevel>(nameof(StrokeWidth), StrokeLevel.Normal);

    /// <summary>How far from a line (its edge, in DIPs) the pointer still takes it.</summary>
    internal const double Reach = 6;

    /// <summary>
    /// The least a drawing measures on the screen (DIPs): a pen stroke that never goes further from where it began, a line
    /// or an arrow shorter (an arrow's head is drawn full size however short it is), a box or an ellipse with a side
    /// shorter. Such a twitch of the mouse draws nothing.
    /// </summary>
    internal const double MinStroke = 4;

    /// <summary>Room between the text box's edge and the text in it: its border and padding.</summary>
    private const double EditorInset = 3;

    private static readonly IPen OutlinePen = new Pen(Brushes.White, 1.5, new DashStyle([4, 3], 0));
    private static readonly IPen OutlineShadowPen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(150, 0, 0, 0)), 3);

    /// <summary>A press in progress: where, the drawing it moves (-1: it draws <paramref name="Note"/>, a new one), and that drawing as it was.</summary>
    private sealed record Press(Point Start, int Index, Annotation Note);

    /// <summary>A text being typed: the one it changes (-1: a new one) and that text as it was, or a new one at its anchor.</summary>
    private sealed record Typing(int Index, Annotation Note);

    private readonly TextBox _editor;
    private Press? _press;
    private Annotation? _live; // the drawing as dragged now; the Annotations property gets it on release
    private Typing? _typing;
    private StandardCursorType? _cursor;

    static AnnotateCanvas() => AffectsRender<AnnotateCanvas>(AnnotationsProperty, SelectedIndexProperty);

    public AnnotateCanvas()
    {
        _editor = new TextBox
        {
            IsVisible = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 40,
            MinHeight = 0,
            Padding = new Thickness(EditorInset - 1, EditorInset - 1, 8, EditorInset - 1), // room for the caret after the text
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily(AnnotationFonts.FamilyName),
        };
        // Tunnelled: Enter puts the text there before the text box would take it as a new line (Shift+Enter is one).
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        // After the text box: an undo it does not take must not reach the window, which would undo a drawing under the text.
        _editor.AddHandler(KeyDownEvent, (_, e) => e.Handled |= e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Z or Key.Y);
        // A click elsewhere (on the picture, a chip, another text box) puts the text there.
        _editor.LostFocus += (_, _) => CommitText();
        VisualChildren.Add(_editor);
        LogicalChildren.Add(_editor);
    }

    /// <summary>What is drawn on the picture, in drawing order (fractions of the picture); set back once per drawing, drag or text.</summary>
    public IReadOnlyList<Annotation>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    /// <summary>The picked drawing, an index into the drawings; -1 for none.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>What a press draws; null for the select tool.</summary>
    public AnnotationKind? Tool
    {
        get => GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    /// <summary>The colour of new drawings, <c>#RRGGBB</c>.</summary>
    public string Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The thickness of new drawings (and the size of a text or a number).</summary>
    public StrokeLevel StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>The drawings by the renderer, with the one being dragged where it is now and the text being typed left out (its box shows it).</summary>
    protected override ImageEdit? EditToDraw => new() { Annotations = Shown().Notes };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // This canvas sets the drawings only once its drag or its text is over: a change now is someone else's.
        if (change.Property == AnnotationsProperty)
        {
            if (_press is not null)
                EndPress();
            if (_typing is not null)
                StopTyping();
        }
    }

    // ───────── Geometry ─────────

    private static Point ToScreen(EditPoint p, Rect image) => new(image.X + p.X * image.Width, image.Y + p.Y * image.Height);

    private static EditPoint ToEdit(Point fraction) => new() { X = fraction.X, Y = fraction.Y };

    private static EditPoint Clamped(Point fraction) => new() { X = Math.Clamp(fraction.X, 0, 1), Y = Math.Clamp(fraction.Y, 0, 1) };

    /// <summary>The short side of the picture as drawn: what the renderer sizes lines, texts and stickers by.</summary>
    private static double ShortSide(Rect image) => Math.Min(image.Width, image.Height);

    /// <summary>
    /// The drawing a press at <paramref name="p"/> takes: the top one (the last drawn) whose line passes within
    /// <see cref="Reach"/> of it (an outline, not the inside of a box), or whose text or sticker it is on; -1 for none.
    /// </summary>
    internal static int HitTest(IReadOnlyList<Annotation> notes, Rect image, Point p)
    {
        for (var i = notes.Count - 1; i >= 0; i--)
        {
            if (Hits(notes[i], image, p))
                return i;
        }
        return -1;
    }

    private static bool Hits(Annotation note, Rect image, Point p)
    {
        if (note.Points.Count == 0)
            return false;
        var points = note.Points.Select(q => ToScreen(q, image)).ToList();
        var half = HalfWidth(note, image);
        switch (note.Kind)
        {
            case AnnotationKind.Text or AnnotationKind.Number:
                return Extent(note, image).Contains(p);
            case AnnotationKind.Rectangle when points.Count > 1:
                var box = new Rect(points[0], points[1]);
                return Near([box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft, box.TopLeft], p, Reach + half);
            case AnnotationKind.Ellipse when points.Count > 1:
                var oval = new Rect(points[0], points[1]);
                return Near([.. Enumerable.Range(0, 49).Select(i => oval.Center + new Vector(
                    oval.Width / 2 * Math.Cos(i * Math.PI / 24), oval.Height / 2 * Math.Sin(i * Math.PI / 24)))], p, Reach + half);
            default:
                return Near(points, p, Reach + half);
        }
    }

    /// <summary>Half of how wide a drawing's line is drawn (a highlighter is three lines wide, an arrow's head as much).</summary>
    private static double HalfWidth(Annotation note, Rect image) =>
        ImageEditRenderer.Sizes(note.Width).Line * ShortSide(image) / 2 * (note.Kind is AnnotationKind.Highlighter or AnnotationKind.Arrow ? 3 : 1);

    /// <summary>Whether <paramref name="p"/> is within <paramref name="reach"/> of the line through <paramref name="line"/>.</summary>
    private static bool Near(IReadOnlyList<Point> line, Point p, double reach)
    {
        if (line.Count == 1)
            return Point.Distance(line[0], p) <= reach;
        for (var i = 1; i < line.Count; i++)
        {
            var (a, ab) = (line[i - 1], line[i] - line[i - 1]);
            var squared = ab.X * ab.X + ab.Y * ab.Y;
            var t = squared > 0 ? Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / squared, 0, 1) : 0;
            if (Point.Distance(p, a + ab * t) <= reach)
                return true;
        }
        return false;
    }

    /// <summary>What a drawing covers on the control, for the picture drawn at <paramref name="image"/>: its outline goes round it.</summary>
    internal static Rect Extent(Annotation note, Rect image)
    {
        if (note.Points.Count == 0)
            return default;
        var points = note.Points.Select(q => ToScreen(q, image)).ToList();
        var sizes = ImageEditRenderer.Sizes(note.Width);
        switch (note.Kind)
        {
            case AnnotationKind.Text:
                // As the renderer lays it out: lines 1.25 sizes apart, from the anchor at the top left.
                var size = sizes.Text * ShortSide(image);
                var lines = (note.Text ?? "").Split('\n');
                using (var font = AnnotationFonts.Create((float)size))
                    return new Rect(points[0], new Size(lines.Max(l => font.MeasureText(l)), lines.Length * 1.25 * size));
            case AnnotationKind.Number:
                var radius = sizes.Sticker * ShortSide(image) / 2;
                return new Rect(points[0].X - radius, points[0].Y - radius, 2 * radius, 2 * radius);
            default:
                var corners = new Rect(new Point(points.Min(q => q.X), points.Min(q => q.Y)), new Point(points.Max(q => q.X), points.Max(q => q.Y)));
                return corners.Inflate(HalfWidth(note, image));
        }
    }

    /// <summary>Whether a drawing is more than a twitch of the mouse on the screen (<see cref="MinStroke"/>).</summary>
    internal static bool LongEnough(Annotation note, Rect image)
    {
        var points = note.Points.Select(q => ToScreen(q, image)).ToList();
        if (note.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
            return points.Count > 1 && Math.Abs(points[1].X - points[0].X) >= MinStroke && Math.Abs(points[1].Y - points[0].Y) >= MinStroke;
        return points.Any(q => Point.Distance(q, points[0]) >= MinStroke);
    }

    /// <summary>
    /// The second point of a line, an arrow, a box or an ellipse dragged from <paramref name="start"/> to <paramref name="p"/>
    /// (on the control). With Shift, a line or an arrow goes at a multiple of 45° on the screen and a box or an ellipse is
    /// square there, as long as the drag; it stops where it would leave the picture, keeping that angle.
    /// </summary>
    private static EditPoint SecondPoint(AnnotationKind kind, Point start, Point p, bool shift, Rect image)
    {
        if (!shift)
            return Clamped(ToFraction(p, image));
        var v = (Vector)(p - start);
        if (kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
        {
            var side = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            v = new Vector(v.X < 0 ? -side : side, v.Y < 0 ? -side : side);
        }
        else
        {
            var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
            v = new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length;
        }
        var (a, b) = (ToFraction(start, image), ToFraction(start + v, image));
        // The largest part of the way from a (on the picture) to b that stays on it.
        var t = 1.0;
        if (b.X > 1)
            t = Math.Min(t, (1 - a.X) / (b.X - a.X));
        if (b.X < 0)
            t = Math.Min(t, a.X / (a.X - b.X));
        if (b.Y > 1)
            t = Math.Min(t, (1 - a.Y) / (b.Y - a.Y));
        if (b.Y < 0)
            t = Math.Min(t, a.Y / (a.Y - b.Y));
        return ToEdit(a + (b - a) * t);
    }

    /// <summary><paramref name="note"/> moved by <paramref name="by"/> (fractions), stopping at the picture's edges.</summary>
    private static Annotation Moved(Annotation note, Vector by)
    {
        var (dx, dy) = (Math.Max(-note.Points.Min(p => p.X), Math.Min(1 - note.Points.Max(p => p.X), by.X)),
            Math.Max(-note.Points.Min(p => p.Y), Math.Min(1 - note.Points.Max(p => p.Y), by.Y)));
        var moved = note.Clone();
        foreach (var point in moved.Points)
            (point.X, point.Y) = (point.X + dx, point.Y + dy);
        return moved;
    }

    /// <summary>The drawings as they are on show, and which one is picked: a drag shows where it is now, a text being typed is left out.</summary>
    private (List<Annotation> Notes, int Selected) Shown()
    {
        var notes = (Annotations ?? []).ToList();
        var selected = SelectedIndex;
        if (_press is { } press && _live is { } live)
        {
            if (press.Index < 0)
                notes.Add(live);
            else if (press.Index < notes.Count)
                notes[press.Index] = live;
        }
        if (_typing is { Index: >= 0 } typing && typing.Index < notes.Count)
        {
            notes.RemoveAt(typing.Index);
            selected = -1;
        }
        return (notes, selected < notes.Count ? selected : -1);
    }

    // ───────── Pointer ─────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // A press in the text box is the text box's (the caret, a selection).
        if (e.Source is Visual source && (source == _editor || _editor.IsVisualAncestorOf(source)))
            return;
        var typing = _typing is not null;
        base.OnPointerPressed(e); // the focus, which puts a text being typed there (see the editor's LostFocus); a pan
        if (typing)
        {
            e.Handled = true; // the press only ended the typing
            return;
        }
        if (e.Handled || Picture is null)
            return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        var p = point.Position;
        var image = PictureRect;
        var notes = Annotations ?? [];
        if (Tool is not { } kind)
        {
            var hit = HitTest(notes, image, p);
            if (e.ClickCount == 2 && hit >= 0 && notes[hit].Kind == AnnotationKind.Text)
            {
                StartTyping(hit, notes[hit].Clone());
                return;
            }
            // Picked at the press, so its outline shows at once; a press on nothing lets go of it.
            SetCurrentValue(SelectedIndexProperty, hit);
            if (hit >= 0)
            {
                _press = new Press(p, hit, notes[hit]);
                e.Pointer.Capture(this);
            }
            return;
        }
        if (!image.Contains(p))
            return; // drawing starts on the picture
        var at = ToEdit(ToFraction(p, image));
        switch (kind)
        {
            case AnnotationKind.Text:
                var text = HitTest(notes, image, p) is var i && i >= 0 && notes[i].Kind == AnnotationKind.Text ? i : -1;
                StartTyping(text, text >= 0 ? notes[text].Clone() : new Annotation { Kind = kind, Points = [at] });
                break;
            case AnnotationKind.Number:
                // The biggest number on this file plus one: a sticker taken away leaves a gap rather than a second one of a number.
                var number = notes.Where(n => n.Kind == AnnotationKind.Number).Select(n => n.Number).DefaultIfEmpty().Max() + 1;
                Commit(-1, new Annotation { Kind = kind, Color = Color, Width = StrokeWidth, Points = [at], Number = number });
                break;
            default:
                _press = new Press(p, -1, new Annotation { Kind = kind, Color = Color, Width = StrokeWidth, Points = [at] });
                e.Pointer.Capture(this);
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); // a pan
        if (e.Handled || Picture is null)
            return;
        var p = e.GetPosition(this);
        if (_press is not { } press)
        {
            ShowCursor(p);
            return;
        }
        if (_live is null && Point.Distance(p, press.Start) < ImageCanvas.DragThreshold)
            return;

        var image = PictureRect;
        var note = press.Note;
        if (press.Index >= 0)
        {
            _live = Moved(note, ToFraction(p, image) - ToFraction(press.Start, image));
        }
        else if (note.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
        {
            // This canvas's own copy: a frame being drawn has a copy of its own (EditPreviewDrawOperation).
            (_live ??= note.Clone()).Points.Add(Clamped(ToFraction(p, image)));
        }
        else
        {
            _live = note.Clone();
            _live.Points = [note.Points[0], SecondPoint(note.Kind, press.Start, p, e.KeyModifiers.HasFlag(KeyModifiers.Shift), image)];
        }
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
        if (live is null)
            return; // a click
        if (press.Index >= 0)
        {
            // Pushed against an edge it may not have moved at all: no undo step for that.
            if (live.Points[0].X != press.Note.Points[0].X || live.Points[0].Y != press.Note.Points[0].Y)
                Commit(press.Index, live);
        }
        else if (LongEnough(live, PictureRect))
        {
            Commit(-1, live);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPress(); // lost mid-drag (another window took the mouse): the drawings stay as they were
    }

    private void EndPress()
    {
        _press = null;
        _live = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Puts <paramref name="note"/> in the drawings: a new one at the end (<paramref name="index"/> -1) or in the place of
    /// the one at <paramref name="index"/>; null takes that one out.
    /// </summary>
    private void Commit(int index, Annotation? note)
    {
        var next = (Annotations ?? []).ToList();
        if (index >= next.Count)
            return;
        if (index < 0)
            next.Add(note!);
        else if (note is null)
            next.RemoveAt(index);
        else
            next[index] = note;
        SetCurrentValue(AnnotationsProperty, next);
    }

    /// <summary>The pointer's shape: a hand for Space + drag, a move over a drawing the select tool would take, a text caret, or a cross to draw.</summary>
    private void ShowCursor(Point p)
    {
        var shape = PanKeyDown ? StandardCursorType.Hand : Tool switch
        {
            null => HitTest(Annotations ?? [], PictureRect, p) >= 0 ? StandardCursorType.SizeAll : StandardCursorType.Arrow,
            AnnotationKind.Text => StandardCursorType.Ibeam,
            _ => StandardCursorType.Cross,
        };
        if (shape == _cursor)
            return;
        _cursor = shape;
        Cursor = new Cursor(shape);
    }

    // ───────── Text ─────────

    /// <summary>Opens the text box on <paramref name="note"/>'s anchor, with its text, and gives it the keys.</summary>
    private void StartTyping(int index, Annotation note)
    {
        _typing = new Typing(index, note);
        var color = index < 0 ? Color : note.Color; // a new text gets the colour of new drawings, an old one keeps its own
        _editor.Foreground = Avalonia.Media.Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : null;
        _editor.Text = note.Text ?? "";
        _editor.CaretIndex = _editor.Text.Length;
        _editor.IsVisible = true;
        InvalidateMeasure();
        InvalidateVisual(); // without the text it retypes
        _editor.Focus();
    }

    /// <summary>
    /// Ends the typing and puts the text in the drawings: a new one at the end, in the colour and thickness of now; a changed
    /// one in its place. Nothing typed makes none, and an old text emptied is taken out.
    /// </summary>
    private void CommitText()
    {
        if (_typing is not { } typing)
            return;
        var text = (_editor.Text ?? "").ReplaceLineEndings("\n").TrimEnd();
        StopTyping();
        var note = typing.Note;
        if (typing.Index < 0)
        {
            if (text.Length == 0)
                return;
            (note.Text, note.Color, note.Width) = (text, Color, StrokeWidth);
            Commit(-1, note);
        }
        else if (text.Length == 0)
        {
            Commit(typing.Index, null);
        }
        else if (text != note.Text)
        {
            note.Text = text;
            Commit(typing.Index, note);
        }
    }

    /// <summary>Closes the text box without a word of it going in; the keys go back to the picture if the box had them.</summary>
    private void StopTyping()
    {
        _typing = null; // first: hiding the box takes its focus, and that must not put the text in
        var hadKeys = _editor.IsKeyboardFocusWithin;
        _editor.IsVisible = false;
        if (hadKeys)
            Focus();
        InvalidateVisual();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                CommitText();
                e.Handled = true; // not the window's Enter, which saves
                break;
            case Key.Escape:
                StopTyping();
                e.Handled = true; // not the window's Esc, which closes it
                break;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _editor.Measure(Size.Infinity); // as wide as its longest line: it does not wrap
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_typing is { } typing)
        {
            // The text box's text where the text will be drawn, as large as it is drawn at this zoom.
            var image = PictureRectIn(finalSize);
            var width = typing.Index < 0 ? StrokeWidth : typing.Note.Width;
            _editor.FontSize = Math.Max(ImageEditRenderer.Sizes(width).Text * ShortSide(image), 1);
            _editor.LineHeight = 1.25 * _editor.FontSize;
            var at = ToScreen(typing.Note.Points[0], image) - new Point(EditorInset, EditorInset);
            _editor.Arrange(new Rect(at, _editor.DesiredSize));
        }
        return finalSize;
    }

    // ───────── Drawing ─────────

    public override void Render(DrawingContext context)
    {
        base.Render(context); // the picture with its drawings
        if (Picture is null)
            return;
        var (notes, selected) = Shown();
        if (selected < 0)
            return;
        var box = Extent(notes[selected], PictureRect).Inflate(4);
        // White dashes over a dark line: the outline shows on a bright picture and on a dark one alike.
        context.DrawRectangle(null, OutlineShadowPen, box);
        context.DrawRectangle(null, OutlinePen, box);
    }
}
