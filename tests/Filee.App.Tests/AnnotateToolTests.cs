// The annotate window ("그리기"): each kind drawn with the mouse, Shift for 45° lines and squares, numbers counted per
// file, text typed over the picture, picking, moving and deleting, each file with its own drawings and undo steps, Save
// handing back only the files drawn on, Esc asking before drawings are thrown away, the tool keys, and the window on the
// screen with what was drawn (by the renderer that saves).

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Tools;
using Filee.App.Views;
using Filee.App.Views.Tools;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class AnnotateToolTests
{
    private static readonly Size Picture = new(400, 200);

    private static UserDataStore TestStore() =>
        new(Path.Combine(Path.GetTempPath(), "filee-app-tests", "annotate-store-" + Guid.NewGuid().ToString("N")[..8]));

    private static UserDataStore TestStoreAt(string directory)
    {
        var store = new UserDataStore(directory);
        store.Load();
        return store;
    }

    private static ILocalizer Loc()
    {
        TestServices.EnsureInitialized("ko");
        return AppHost.Get<ILocalizer>();
    }

    /// <summary>Runs the UI thread until the preview, read on another thread, has arrived.</summary>
    private static void WaitForPreview(ImageToolViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.PreviewImage is null && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.NotNull(vm.PreviewImage);
    }

    private static AnnotateToolViewModel Annotate(IReadOnlyList<string> files, UserDataStore store) =>
        Assert.IsType<AnnotateToolViewModel>(ImageToolViewModel.Create(ImageToolKind.Annotate, files, store, Loc()));

    private static (ImageToolWindow Window, AnnotateToolViewModel Vm) Open(UserDataStore store, params string[] files)
    {
        var vm = Annotate(files, store);
        var window = new ImageToolWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        WaitForPreview(vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (window, vm);
    }

    private static (ImageToolWindow Window, AnnotateToolViewModel Vm) Open(int files = 1) =>
        Open(TestStore(), [.. Enumerable.Range(0, files).Select(_ => ImageToolWindowTests.TestImages.Png(400, 200))]);

    private static AnnotateCanvas Canvas(Window window) => window.GetVisualDescendants().OfType<AnnotateCanvas>().Single();

    /// <summary>Where the picture (<paramref name="size"/> big) is on the window.</summary>
    private static Rect Shown(Window window, Size? size = null)
    {
        var canvas = Canvas(window);
        var image = PictureView.ImageRect(canvas.Bounds.Size, size ?? Picture, 1, default);
        return new Rect(canvas.TranslatePoint(image.TopLeft, window)!.Value, image.Size);
    }

    /// <summary>The window's point at (<paramref name="x"/>, <paramref name="y"/>) as fractions of the picture.</summary>
    private static Point At(Window window, double x, double y, Size? size = null)
    {
        var image = Shown(window, size);
        return new Point(image.X + x * image.Width, image.Y + y * image.Height);
    }

    /// <summary>A left-button drag in steps, as a mouse does it, with the keys held all along.</summary>
    private static void Drag(Window window, Point from, Point to, RawInputModifiers keys = RawInputModifiers.None)
    {
        window.MouseDown(from, MouseButton.Left, keys);
        for (var i = 1; i <= 4; i++)
            window.MouseMove(from + (to - from) * i / 4, keys);
        window.MouseUp(to, MouseButton.Left, keys);
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    /// <summary>The text box typed into on the picture; null while there is none.</summary>
    private static TextBox? Editor(Window window) =>
        Canvas(window).GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsVisible);

    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPressQwerty(key, modifiers);

    /// <summary>← or → to another file, and its preview read: until it is there, a press draws nothing.</summary>
    private static void Turn(Window window, ImageToolViewModel vm, PhysicalKey key)
    {
        Key(window, key);
        WaitForPreview(vm);
        Dispatcher.UIThread.RunJobs();
    }

    private static (double X, double Y) Xy(EditPoint p) => (p.X, p.Y);

    // ───────── Drawing ─────────

    [AvaloniaFact]
    public void Pen_drag_adds_one_pen_annotation()
    {
        var (window, vm) = Open();
        Assert.IsType<AnnotatePanel>(window.FindControl<ContentControl>("ToolPanel")!.Content);
        Assert.Equal("그리기 · photo.png", window.Title);
        Assert.Equal(AnnotationKind.Pen, vm.DrawingTool); // the default tool, with the default colour and thickness
        Assert.Empty(vm.Annotations);

        Drag(window, At(window, .1, .2), At(window, .6, .8));

        var pen = Assert.Single(vm.Annotations);
        Assert.Equal(AnnotationKind.Pen, pen.Kind);
        Assert.Equal("#FF3B30", pen.Color);
        Assert.Equal(StrokeLevel.Normal, pen.Width);
        Assert.True(pen.Points.Count >= 4, $"{pen.Points.Count} points"); // the path the mouse took, not two ends
        Assert.Equal((.1, .2), Xy(pen.Points[0]), new PointComparer());
        Assert.Equal((.6, .8), Xy(pen.Points[^1]), new PointComparer());

        // A click is no drawing, nor a wobble under 4 DIPs (3.5 is past the 3 that make a press a drag).
        Click(window, At(window, .3, .3));
        Drag(window, At(window, .7, .3), At(window, .7, .3) + new Point(3.5, 0));
        Assert.Single(vm.Annotations);

        vm.UndoCommand.Execute(null); // the stroke is one step
        Assert.Empty(vm.Annotations);
        vm.RedoCommand.Execute(null);
        Assert.Single(vm.Annotations);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Shift_snaps_a_line_to_45_degrees()
    {
        var (window, vm) = Open();
        var shown = Shown(window);
        Key(window, PhysicalKey.L);
        Assert.Equal(AnnotationKind.Line, vm.DrawingTool);

        // About 40° down to the right on the screen: Shift makes it 45° there (the picture is twice as wide as tall).
        var from = At(window, .2, .2);
        Drag(window, from, from + new Point(100, 84), RawInputModifiers.Shift);
        var line = Assert.Single(vm.Annotations);
        Assert.Equal(AnnotationKind.Line, line.Kind);
        Assert.Equal((.2, .2), Xy(line.Points[0]), new PointComparer());
        var (dx, dy) = ((line.Points[1].X - line.Points[0].X) * shown.Width, (line.Points[1].Y - line.Points[0].Y) * shown.Height);
        Assert.True(dx > 50 && Math.Abs(dx - dy) < .01, $"({dx}, {dy}) on the screen");

        // Nearly flat goes flat; without Shift the line goes where the mouse went.
        Drag(window, from, from + new Point(100, 12), RawInputModifiers.Shift);
        Assert.Equal(line.Points[0].Y, vm.Annotations[1].Points[1].Y, 6);
        Drag(window, from, from + new Point(100, 12));
        Assert.Equal(12 / shown.Height, vm.Annotations[2].Points[1].Y - vm.Annotations[2].Points[0].Y, 3);

        // An arrow too, and a stroke shorter than 4 DIPs makes none (its head would be drawn full size).
        Key(window, PhysicalKey.A);
        Drag(window, At(window, .5, .5), At(window, .5, .5) + new Point(-60, -50), RawInputModifiers.Shift);
        var arrow = vm.Annotations[3];
        Assert.Equal(AnnotationKind.Arrow, arrow.Kind);
        Assert.Equal((arrow.Points[1].X - arrow.Points[0].X) * shown.Width, (arrow.Points[1].Y - arrow.Points[0].Y) * shown.Height, 2);
        Drag(window, At(window, .8, .5), At(window, .8, .5) + new Point(3.5, 0));
        Key(window, PhysicalKey.L);
        Drag(window, At(window, .8, .5), At(window, .8, .5) + new Point(0, 3.5));
        Assert.Equal(4, vm.Annotations.Count);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Shift_makes_a_square()
    {
        var (window, vm) = Open();
        var shown = Shown(window);
        Key(window, PhysicalKey.R);
        var from = At(window, .1, .1);
        Drag(window, from, from + new Point(90, 40), RawInputModifiers.Shift);
        var box = Assert.Single(vm.Annotations);
        Assert.Equal(AnnotationKind.Rectangle, box.Kind);
        var (w, h) = ((box.Points[1].X - box.Points[0].X) * shown.Width, (box.Points[1].Y - box.Points[0].Y) * shown.Height);
        Assert.True(w > 80 && Math.Abs(w - h) < .01, $"{w} × {h} on the screen");

        // Up and to the left, as an ellipse: a circle.
        Key(window, PhysicalKey.E);
        from = At(window, .9, .9);
        Drag(window, from, from + new Point(-30, -70), RawInputModifiers.Shift);
        var circle = vm.Annotations[1];
        Assert.Equal(AnnotationKind.Ellipse, circle.Kind);
        (w, h) = ((circle.Points[1].X - circle.Points[0].X) * shown.Width, (circle.Points[1].Y - circle.Points[0].Y) * shown.Height);
        Assert.True(w < -60 && Math.Abs(w - h) < .01, $"{w} × {h} on the screen");

        // Without Shift, the box the mouse drew; one with a side under 4 DIPs is none.
        Key(window, PhysicalKey.R);
        Drag(window, At(window, .5, .5), At(window, .5, .5) + new Point(90, 40));
        Assert.Equal(40 / shown.Height, vm.Annotations[2].Points[1].Y - vm.Annotations[2].Points[0].Y, 3);
        Drag(window, At(window, .5, .2), At(window, .5, .2) + new Point(90, 3.5));
        Assert.Equal(3, vm.Annotations.Count);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Number_tool_counts_per_file()
    {
        var (window, vm) = Open(files: 2);
        Key(window, PhysicalKey.N);
        Click(window, At(window, .2, .5));
        Click(window, At(window, .5, .5));
        Assert.Equal([1, 2], vm.Annotations.Select(a => a.Number));
        Assert.All(vm.Annotations, a => Assert.Equal(AnnotationKind.Number, a.Kind));
        Assert.Equal((.5, .5), Xy(vm.Annotations[1].Points[0]), new PointComparer());

        Turn(window, vm, PhysicalKey.ArrowRight);
        Assert.Empty(vm.Annotations); // the next file has its own
        Click(window, At(window, .3, .3));
        Assert.Equal([1], vm.Annotations.Select(a => a.Number));

        // Back on the first: the biggest number there plus one, also after the 1 is gone.
        Turn(window, vm, PhysicalKey.ArrowLeft);
        Key(window, PhysicalKey.V);
        Click(window, At(window, .2, .5));
        Key(window, PhysicalKey.Delete);
        Key(window, PhysicalKey.N);
        Click(window, At(window, .8, .5));
        Assert.Equal([2, 3], vm.Annotations.Select(a => a.Number));

        // A click off the picture puts none there.
        Click(window, At(window, 1.02, .5));
        Assert.Equal(2, vm.Annotations.Count);
        window.Close();
        vm.Dispose();
    }

    // ───────── Per file ─────────

    [AvaloniaFact]
    public void Undo_only_touches_the_current_file()
    {
        var (window, vm) = Open(files: 2);
        Drag(window, At(window, .1, .2), At(window, .6, .8));
        Turn(window, vm, PhysicalKey.ArrowRight);
        Key(window, PhysicalKey.L);
        Drag(window, At(window, .2, .2), At(window, .8, .8));
        Drag(window, At(window, .2, .8), At(window, .8, .2));
        Assert.Equal(2, vm.Annotations.Count);

        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control); // one more than this file has: the other file's stroke stays
        Assert.Empty(vm.Annotations);

        Turn(window, vm, PhysicalKey.ArrowLeft);
        var pen = Assert.Single(vm.Annotations);
        Assert.Equal(AnnotationKind.Pen, pen.Kind);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Empty(vm.Annotations);
        Key(window, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Single(vm.Annotations);

        // The other file's redo steps waited for it.
        Turn(window, vm, PhysicalKey.ArrowRight);
        Assert.Empty(vm.Annotations);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Single(vm.Annotations);

        // Reset clears the file shown, as one step of its own.
        vm.ResetCommand.Execute(null);
        Assert.Empty(vm.Annotations);
        Turn(window, vm, PhysicalKey.ArrowLeft);
        Assert.Single(vm.Annotations);
        Turn(window, vm, PhysicalKey.ArrowRight);
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Annotations);
        window.Close();
        vm.Dispose();
    }

    // ───────── Text ─────────

    [AvaloniaFact]
    public void Text_tool_commits_on_enter_and_edits_on_double_click()
    {
        var (window, vm) = Open();
        Key(window, PhysicalKey.T);
        Click(window, At(window, .3, .4));
        var editor = Editor(window);
        Assert.NotNull(editor);
        Assert.True(editor.IsFocused);
        window.KeyTextInput("안녕");
        Key(window, PhysicalKey.Enter, RawInputModifiers.Shift); // a new line, not done yet
        window.KeyTextInput("하세요");
        Assert.Empty(vm.Annotations);
        Key(window, PhysicalKey.Enter);

        var text = Assert.Single(vm.Annotations);
        Assert.Equal(AnnotationKind.Text, text.Kind);
        Assert.Equal("안녕\n하세요", text.Text);
        Assert.Equal((.3, .4), Xy(Assert.Single(text.Points)), new PointComparer());
        Assert.Null(Editor(window));
        Assert.IsType<AnnotateCanvas>(window.FocusManager!.GetFocusedElement()); // the keys are the picture's again

        // Esc, or nothing typed, makes none; Esc does not close the window either.
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Click(window, At(window, .6, .7));
        window.KeyTextInput("버려요");
        Key(window, PhysicalKey.Escape);
        Click(window, At(window, .7, .2));
        Key(window, PhysicalKey.Enter);
        Assert.Single(vm.Annotations);
        Assert.False(closed);
        Assert.Null(vm.Result);

        // A double click on it with the select tool types it anew; it is still one text, changed in one step.
        Key(window, PhysicalKey.V);
        var inside = At(window, .3, .4) + new Point(6, 6);
        Click(window, inside);
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        editor = Editor(window);
        Assert.NotNull(editor);
        Assert.Equal("안녕\n하세요", editor.Text!.ReplaceLineEndings("\n"));
        editor.SelectAll();
        window.KeyTextInput("고침");
        Key(window, PhysicalKey.Enter);
        Assert.Equal("고침", Assert.Single(vm.Annotations).Text);
        vm.UndoCommand.Execute(null);
        Assert.Equal("안녕\n하세요", Assert.Single(vm.Annotations).Text);

        // With the text tool, a click on a text types it anew too; emptied, it is gone.
        Key(window, PhysicalKey.T);
        Click(window, inside + new Point(2, 0));
        Editor(window)!.SelectAll();
        Key(window, PhysicalKey.Delete);
        Key(window, PhysicalKey.Enter);
        Assert.Empty(vm.Annotations);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Tool_keys_are_ignored_while_typing_text()
    {
        var (window, vm) = Open(files: 2);
        Key(window, PhysicalKey.T);
        Click(window, At(window, .3, .4));
        foreach (var key in new[] { PhysicalKey.P, PhysicalKey.V, PhysicalKey.N, PhysicalKey.BracketRight, PhysicalKey.ArrowRight })
            Key(window, key);
        window.KeyTextInput("VPN]");
        Assert.Equal(AnnotationKind.Text, vm.DrawingTool);
        Assert.Equal(StrokeLevel.Normal, vm.StrokeWidth);
        Assert.Equal(0, vm.Index);
        Assert.Equal("VPN]", Editor(window)!.Text);
        Key(window, PhysicalKey.Enter);
        Assert.Equal("VPN]", Assert.Single(vm.Annotations).Text);

        // Done typing: the keys are the tools' again.
        Key(window, PhysicalKey.P);
        Assert.Equal(AnnotationKind.Pen, vm.DrawingTool);
        Key(window, PhysicalKey.BracketRight);
        Assert.Equal(StrokeLevel.Thick, vm.StrokeWidth);
        Key(window, PhysicalKey.BracketRight); // already the thickest
        Assert.Equal(StrokeLevel.Thick, vm.StrokeWidth);
        Key(window, PhysicalKey.BracketLeft);
        Key(window, PhysicalKey.BracketLeft);
        Assert.Equal(StrokeLevel.Thin, vm.StrokeWidth);
        foreach (var (key, kind) in new[]
        {
            (PhysicalKey.H, AnnotationKind.Highlighter), (PhysicalKey.A, AnnotationKind.Arrow), (PhysicalKey.L, AnnotationKind.Line),
            (PhysicalKey.R, AnnotationKind.Rectangle), (PhysicalKey.E, AnnotationKind.Ellipse), (PhysicalKey.T, AnnotationKind.Text),
            (PhysicalKey.N, AnnotationKind.Number), (PhysicalKey.P, AnnotationKind.Pen),
        })
        {
            Key(window, key);
            Assert.Equal(kind, vm.DrawingTool);
        }
        Key(window, PhysicalKey.V);
        Assert.Null(vm.DrawingTool); // the select tool
        Key(window, PhysicalKey.P, RawInputModifiers.Control); // not the key with Ctrl
        Assert.Null(vm.DrawingTool);
        window.Close();
        vm.Dispose();
    }

    // ───────── Picking ─────────

    [AvaloniaFact]
    public void Select_and_delete_removes_the_annotation()
    {
        var (window, vm) = Open();
        Key(window, PhysicalKey.L);
        Drag(window, At(window, .1, .5), At(window, .5, .5));
        Key(window, PhysicalKey.R);
        Drag(window, At(window, .6, .2), At(window, .9, .8));
        Drag(window, At(window, .6, .2), At(window, .9, .8)); // the same box again, on top
        Assert.Equal(3, vm.Annotations.Count);

        Key(window, PhysicalKey.V);
        Assert.Equal(-1, vm.SelectedIndex);
        Click(window, At(window, .3, .5) + new Point(0, 7)); // within 6 DIPs of the line's edge
        Assert.Equal(0, vm.SelectedIndex);
        Click(window, At(window, .3, .5) + new Point(0, 12)); // beyond: nothing, and the line is let go of
        Assert.Equal(-1, vm.SelectedIndex);
        Click(window, At(window, .75, .2)); // on the boxes' edge: the one on top
        Assert.Equal(2, vm.SelectedIndex);
        Click(window, At(window, .75, .5)); // inside an outline is not on it
        Assert.Equal(-1, vm.SelectedIndex);

        // Dragged, it moves; the others stay.
        Drag(window, At(window, .2, .5), At(window, .3, .6));
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal([(.2, .6), (.6, .6)], vm.Annotations[0].Points.Select(Xy), new PointComparer());
        Assert.Equal((.6, .2), Xy(vm.Annotations[1].Points[0]), new PointComparer());

        Key(window, PhysicalKey.Delete);
        Assert.Equal(2, vm.Annotations.Count);
        Assert.All(vm.Annotations, a => Assert.Equal(AnnotationKind.Rectangle, a.Kind));
        Assert.Equal(-1, vm.SelectedIndex);
        Key(window, PhysicalKey.Delete); // nothing picked: nothing goes
        Assert.Equal(2, vm.Annotations.Count);

        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(AnnotationKind.Line, vm.Annotations[0].Kind);
        Assert.Equal(0, vm.SelectedIndex); // picked again, as it was
        Key(window, PhysicalKey.Z, RawInputModifiers.Control); // the move
        Assert.Equal((.1, .5), Xy(vm.Annotations[0].Points[0]), new PointComparer());
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_drag_is_dropped_when_the_drawings_change_under_it()
    {
        var (window, vm) = Open();
        Key(window, PhysicalKey.L);
        Drag(window, At(window, .1, .3), At(window, .4, .3));
        Drag(window, At(window, .1, .7), At(window, .4, .7));
        var first = vm.Annotations[0].Points.Select(Xy).ToList();

        // The second line is being moved when Delete takes it away: letting go moves nothing and brings nothing back.
        Key(window, PhysicalKey.V);
        var from = At(window, .2, .7);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(20, -20));
        Assert.Equal(1, vm.SelectedIndex);
        Key(window, PhysicalKey.Delete);
        window.MouseMove(from + new Point(40, -40));
        window.MouseUp(from + new Point(40, -40), MouseButton.Left);
        var left = Assert.Single(vm.Annotations);
        Assert.Equal(first, left.Points.Select(Xy), new PointComparer());

        // A stroke being drawn when Ctrl+Z undoes the step before it: the stroke is dropped, the undo stands.
        Key(window, PhysicalKey.P);
        from = At(window, .6, .2);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(30, 10));
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(2, vm.Annotations.Count); // the delete undone
        window.MouseMove(from + new Point(60, 20));
        window.MouseUp(from + new Point(60, 20), MouseButton.Left);
        Assert.Equal([AnnotationKind.Line, AnnotationKind.Line], vm.Annotations.Select(a => a.Kind));
        window.Close();
        vm.Dispose();
    }

    // ───────── Saving and closing ─────────

    [AvaloniaFact]
    public void Save_returns_per_file_edits_for_marked_files_only()
    {
        var store = TestStore();
        string[] files = [ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(400, 200),
            ImageToolWindowTests.TestImages.Png(400, 200)];
        var (window, vm) = Open(store, files);
        var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.SaveCommand);
        Assert.False(save.IsEffectivelyEnabled); // nothing drawn yet
        Assert.Equal("", vm.FooterText);

        Key(window, PhysicalKey.BracketRight);
        vm.SelectedColor = vm.Colors.Single(c => c.Hex == "#007AFF");
        Key(window, PhysicalKey.A);
        Drag(window, At(window, .1, .2), At(window, .6, .8));
        Assert.True(save.IsEffectivelyEnabled);
        Assert.Equal("표시한 1개만 저장돼요", vm.FooterText);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "표시한 1개만 저장돼요");

        Turn(window, vm, PhysicalKey.ArrowRight);
        Turn(window, vm, PhysicalKey.ArrowRight);
        Key(window, PhysicalKey.N);
        Click(window, At(window, .5, .5));
        Assert.Equal("표시한 2개만 저장돼요", vm.FooterText);
        Assert.Equal("#FF3B30", store.Settings.ImageTools.PenColor); // only Save remembers

        Key(window, PhysicalKey.Enter);

        Assert.Null(vm.Result!.Shared);
        var perFile = vm.Result.PerFile!;
        Assert.Equal(new[] { files[0], files[2] }.Order(StringComparer.Ordinal), perFile.Keys.Order(StringComparer.Ordinal));
        var arrow = Assert.Single(perFile[files[0]].Annotations!);
        Assert.Equal((AnnotationKind.Arrow, "#007AFF", StrokeLevel.Thick), (arrow.Kind, arrow.Color, arrow.Width));
        Assert.Equal(1, Assert.Single(perFile[files[2]].Annotations!).Number);
        Assert.All(perFile.Values, e => Assert.True(e.Draws));

        // The tool, the colour and the thickness are remembered.
        foreach (var kept in new[] { store, TestStoreAt(store.Directory) })
        {
            var tools = kept.Settings.ImageTools;
            Assert.Equal(("#007AFF", StrokeLevel.Thick, AnnotationKind.Number), (tools.PenColor, tools.PenWidth, tools.LastTool));
        }
        var next = Annotate([ImageToolWindowTests.TestImages.Png(400, 200)], store);
        Assert.Equal(("#007AFF", StrokeLevel.Thick, (AnnotationKind?)AnnotationKind.Number), (next.Color, next.StrokeWidth, next.DrawingTool));
        Assert.Empty(next.Annotations); // the drawings are for these pictures only
        vm.Dispose();
        next.Dispose();
    }

    [AvaloniaFact]
    public void Esc_with_drawings_asks_once()
    {
        // Nothing drawn: Esc closes without a question.
        var (window, vm) = Open();
        var asked = new List<string>();
        vm.Ask = question =>
        {
            asked.Add(question);
            return Task.FromResult(true);
        };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Key(window, PhysicalKey.Escape);
        Assert.True(closed);
        Assert.Empty(asked);
        vm.Dispose();

        (window, vm) = Open();
        var answer = new TaskCompletionSource<bool>();
        vm.Ask = question =>
        {
            asked.Add(question);
            return answer.Task;
        };
        closed = false;
        window.Closed += (_, _) => closed = true;
        Drag(window, At(window, .1, .2), At(window, .6, .8));

        // One question, however many times Esc is pressed while it is asked; "no" keeps the window and the drawing.
        Key(window, PhysicalKey.Escape);
        Key(window, PhysicalKey.Escape);
        Assert.Equal(["그린 내용을 버리고 닫을까요?"], asked);
        answer.SetResult(false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(closed);
        Assert.Single(vm.Annotations);

        // Asked again on the next Esc; "yes" closes with nothing to run. The Cancel button asks the same.
        answer = new TaskCompletionSource<bool>();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.CancelCommand).Command!.Execute(null);
        Assert.Equal(2, asked.Count);
        answer.SetResult(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        Assert.Null(vm.Result);
        vm.Dispose();
    }

    // ───────── The panel and the screen ─────────

    [AvaloniaFact]
    public void Panel_clicks_hand_the_keys_back_to_the_picture()
    {
        var (window, vm) = Open(files: 2);
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        var panel = (AnnotatePanel)window.FindControl<ContentControl>("ToolPanel")!.Content!;

        foreach (var (name, index) in new[] { ("ToolCells", 4), ("ColorChips", 4), ("WidthChips", 2) })
        {
            window.FocusManager!.Focus(null);
            var cell = panel.FindControl<ListBox>(name)!.ContainerFromIndex(index)!;
            window.MouseDown(Centre(cell), MouseButton.Left);
            window.MouseUp(Centre(cell), MouseButton.Left);
            Assert.IsType<AnnotateCanvas>(window.FocusManager.GetFocusedElement());
        }
        Assert.Equal(AnnotationKind.Line, vm.DrawingTool); // the fifth tool cell: select, pen, highlighter, arrow, line
        Assert.Equal("#007AFF", vm.Color);
        Assert.Equal(StrokeLevel.Thick, vm.StrokeWidth);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // the files, not the chips
        Assert.Equal(1, vm.Index);
        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        Assert.Equal(AnnotationKind.Rectangle, vm.DrawingTool);

        // A colour typed in: a whole code becomes the colour.
        vm.CustomColorHex = "12ab3";
        Assert.Equal("#007AFF", vm.Color);
        vm.CustomColorHex = "12ab34";
        Assert.Equal("#12AB34", vm.Color);
        Assert.True(vm.IsCustomColor);
        Assert.Null(vm.SelectedColor);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Drawings_show_on_the_picture_and_leave_a_screenshot()
    {
        var size = new Size(1000, 700);
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(1000, 700));
        foreach (var text in new[] { "색", "굵기", "가늘게", "보통", "굵게", "사용자 색" })
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == text);
        Assert.Equal(9, window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ToolCells").ItemCount);

        // A wavy pen stroke.
        window.MouseDown(At(window, .06, .7, size), MouseButton.Left);
        for (var i = 1; i <= 24; i++)
            window.MouseMove(At(window, .06 + .3 * i / 24, .7 + .06 * Math.Sin(i / 3.0), size));
        window.MouseUp(At(window, .36, .7 + .06 * Math.Sin(8), size), MouseButton.Left);
        Key(window, PhysicalKey.A);
        Drag(window, At(window, .55, .2, size), At(window, .8, .45, size));
        Key(window, PhysicalKey.N);
        Click(window, At(window, .15, .2, size));
        Click(window, At(window, .3, .2, size));
        Key(window, PhysicalKey.T);
        Click(window, At(window, .45, .7, size));
        window.KeyTextInput("여기를 누르세요");
        Key(window, PhysicalKey.Enter);
        Key(window, PhysicalKey.V);
        Click(window, At(window, .8, .45, size)); // the arrow's tip: picked, with a dashed outline
        Assert.Equal(1, vm.SelectedIndex);
        Assert.Equal(
            [AnnotationKind.Pen, AnnotationKind.Arrow, AnnotationKind.Number, AnnotationKind.Number, AnnotationKind.Text],
            vm.Annotations.Select(a => a.Kind));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame()!;
        var red = PixelAt(frame, At(window, .3, .2, size) + new Point(-9, 0)); // the second sticker's disc, beside its digit
        Assert.True(red.R > 200 && red.G < 120 && red.B < 120, $"a red sticker, not {red}");
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "tool-annotate-ko.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());
        window.Close();
        vm.Dispose();
    }

    /// <summary>The frame's colour at <paramref name="p"/> (window DIPs; the headless screen is at 100 %).</summary>
    private static Avalonia.Media.Color PixelAt(WriteableBitmap frame, Point p)
    {
        using var buffer = frame.Lock();
        var pixel = new byte[4];
        Marshal.Copy(buffer.Address + (int)p.Y * buffer.RowBytes + (int)p.X * 4, pixel, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Avalonia.Media.Color.FromRgb(pixel[0], pixel[1], pixel[2])
            : Avalonia.Media.Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    /// <summary>Compares points to two decimals: a drag lands between pixels.</summary>
    private sealed class PointComparer : IEqualityComparer<(double X, double Y)>
    {
        public bool Equals((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) < .015 && Math.Abs(a.Y - b.Y) < .015;

        public int GetHashCode((double X, double Y) point) => 0;
    }
}
