// The redact window: drawing, selecting, moving, resizing and deleting boxes with the mouse and keys, one undo step per
// drag, the boxes kept inside the picture, the B / M / U mode keys, Save off while there is nothing to hide, what Save hands
// back and remembers, the panel and the preview on the screen (black, mosaic and blur drawn by the renderer that saves), and
// "find what to hide": what the platform read listed and outlined, and the chosen spots hidden as one undo step.

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
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Privacy;
using Filee.Core.Settings;
using ImageMagick;

namespace Filee.App.Tests;

public class RedactToolTests
{
    private static UserDataStore TestStore() =>
        new(Path.Combine(Path.GetTempPath(), "filee-app-tests", "redact-store-" + Guid.NewGuid().ToString("N")[..8]));

    private static UserDataStore TestStoreAt(string directory)
    {
        var store = new UserDataStore(directory);
        store.Load();
        return store;
    }

    private static ILocalizer Loc(string language)
    {
        TestServices.EnsureInitialized(language);
        return AppHost.Get<ILocalizer>();
    }

    private static RedactToolViewModel Redact(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc) =>
        Assert.IsType<RedactToolViewModel>(ImageToolViewModel.Create(ImageToolKind.Redact, files, store, loc));

    private static EditRect Box(double x, double y, double width, double height) => new() { X = x, Y = y, Width = width, Height = height };

    private static RedactEdit Boxes(RedactMode mode, params EditRect[] boxes) => new() { Mode = mode, Boxes = [.. boxes] };

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

    // ───────── The view model ─────────

    [AvaloniaFact]
    public void Opens_with_no_boxes_and_the_remembered_mode()
    {
        var store = TestStore();
        store.Settings.ImageTools.RedactMode = RedactMode.Mosaic;
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));

        Assert.Equal("가리기 · photo.png", vm.Title);
        Assert.Empty(vm.Redact.Boxes);
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        Assert.Equal(["검은 상자", "모자이크", "흐림"], vm.Modes.Select(m => m.Label));
        Assert.Equal("상자 0개", vm.BoxCountText);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Save_is_off_without_boxes()
    {
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        var finished = false;
        vm.Finished += (_, _) => finished = true;
        var changes = 0;
        vm.SaveCommand.CanExecuteChanged += (_, _) => changes++;

        Assert.False(vm.SaveCommand.CanExecute(null));
        // Enter runs the command without asking it (CommunityToolkit's Execute does not check): nothing is handed back.
        vm.SaveCommand.Execute(null);
        Assert.False(finished);
        Assert.Null(vm.Result);

        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .3, .3));
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.True(changes > 0);

        vm.UndoCommand.Execute(null); // the box is gone again
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.RedoCommand.Execute(null);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.ResetCommand.Execute(null);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Save_returns_shared_redact_edit_and_remembers_mode()
    {
        var store = TestStore();
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(200, 400)], store, Loc("ko"));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .2, .3, .4), Box(.5, .5, .25, .25));
        vm.Mode = vm.Modes.Single(m => m.Value == RedactMode.Mosaic);
        vm.NextCommand.Execute(null); // the same boxes belong to every file
        Assert.Equal("상자 2개", vm.BoxCountText);
        Assert.Equal(RedactMode.Black, store.Settings.ImageTools.RedactMode); // only Save remembers

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result!.PerFile);
        Assert.Null(vm.Result.Shared!.Crop);
        Assert.Null(vm.Result.Shared.Annotations);
        var redact = vm.Result.Shared.Redact!;
        Assert.Equal(RedactMode.Mosaic, redact.Mode);
        Assert.Equal([(.1, .2, .3, .4), (.5, .5, .25, .25)], redact.Boxes.Select(b => (b.X, b.Y, b.Width, b.Height)));
        Assert.True(vm.Result.Shared.Draws);

        foreach (var kept in new[] { store, TestStoreAt(store.Directory) })
            Assert.Equal(RedactMode.Mosaic, kept.Settings.ImageTools.RedactMode);
        var next = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));
        Assert.Equal(RedactMode.Mosaic, next.Mode.Value);
        Assert.Empty(next.Redact.Boxes); // the boxes are for these pictures only

        // The result is the edit as saved: later changes in the window do not reach it.
        vm.Redact = Boxes(RedactMode.Black);
        Assert.Equal(2, redact.Boxes.Count);
        vm.Dispose();
        next.Dispose();
    }

    [AvaloniaFact]
    public void Undo_and_reset_put_back_boxes_and_mode_one_step_at_a_time()
    {
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .2, .2));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .2, .2), Box(.5, .5, .2, .2));
        vm.SelectedIndex = 1;
        vm.Mode = vm.Modes.Single(m => m.Value == RedactMode.Mosaic);

        vm.ResetCommand.Execute(null);
        Assert.Empty(vm.Redact.Boxes);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value); // the way of hiding is a choice, not work to lose
        Assert.Equal(-1, vm.SelectedIndex);

        vm.UndoCommand.Execute(null); // the reset
        Assert.Equal(2, vm.Redact.Boxes.Count);
        Assert.Equal(1, vm.SelectedIndex);
        vm.UndoCommand.Execute(null); // the mode
        Assert.Equal(RedactMode.Black, vm.Mode.Value);
        Assert.Equal(2, vm.Redact.Boxes.Count);
        vm.UndoCommand.Execute(null); // the second box
        Assert.Single(vm.Redact.Boxes);
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.Redact.Boxes.Count);

        // Setting what is already there is no step.
        vm.Redact = vm.Redact.Clone();
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Redact.Boxes);
        vm.Dispose();
    }

    // ───────── The window ─────────

    private static (ImageToolWindow Window, RedactToolViewModel Vm) Open(UserDataStore store, int width = 900, params string[] files)
    {
        var vm = Redact(files, store, Loc("ko"));
        var window = new ImageToolWindow { DataContext = vm, Width = width, Height = 600 };
        window.Show();
        WaitForPreview(vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (window, vm);
    }

    /// <summary>The window's point at (<paramref name="x"/>, <paramref name="y"/>) as fractions of the picture, which is <paramref name="size"/> big.</summary>
    private static Point At(Window window, Size size, double x, double y)
    {
        var canvas = window.GetVisualDescendants().OfType<RedactCanvas>().Single();
        var image = PictureView.ImageRect(canvas.Bounds.Size, size, 1, default);
        return canvas.TranslatePoint(new Point(image.X + x * image.Width, image.Y + y * image.Height), window)!.Value;
    }

    /// <summary>A left-button drag in steps, as a mouse does it.</summary>
    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 4; i++)
            window.MouseMove(from + (to - from) * i / 4);
        window.MouseUp(to, MouseButton.Left);
    }

    private static readonly Size Picture = new(400, 200);

    [AvaloniaFact]
    public void Dragging_on_the_picture_adds_a_box()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        Assert.IsType<RedactPanel>(window.FindControl<ContentControl>("ToolPanel")!.Content);

        Drag(window, At(window, Picture, .1, .2), At(window, Picture, .6, .8));

        var box = Assert.Single(vm.Redact.Boxes);
        Assert.Equal((.1, .2, .5, .6), (box.X, box.Y, box.Width, box.Height), new BoxComparer());
        Assert.Equal(0, vm.SelectedIndex); // the new box is the one picked
        Assert.Equal("상자 1개", vm.BoxCountText);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "상자 1개");

        // Dragged any way round: from the bottom right to the top left of it.
        Drag(window, At(window, Picture, .95, .95), At(window, Picture, .8, .85));
        Assert.Equal(2, vm.Redact.Boxes.Count);
        var second = vm.Redact.Boxes[1];
        Assert.Equal((.8, .85, .15, .1), (second.X, second.Y, second.Width, second.Height), new BoxComparer());

        // A click on the bare picture draws nothing and is no undo step; it only lets go of the box.
        var click = At(window, Picture, .7, .1);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Assert.Equal(2, vm.Redact.Boxes.Count);
        Assert.Equal(-1, vm.SelectedIndex);

        vm.UndoCommand.Execute(null); // each drag is one step
        Assert.Single(vm.Redact.Boxes);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Redact.Boxes);
        vm.RedoCommand.Execute(null);
        Assert.Single(vm.Redact.Boxes);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Clicking_a_box_picks_it_dragging_moves_it_and_a_handle_resizes_it()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .2, .3), Box(.5, .5, .3, .3));
        Assert.Equal(-1, vm.SelectedIndex);

        var inside = At(window, Picture, .65, .65);
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        Assert.Equal(1, vm.SelectedIndex);
        Assert.Equal(2, vm.Redact.Boxes.Count);

        // Moving: the whole box goes along, the other stays.
        Drag(window, inside, At(window, Picture, .75, .60));
        Assert.Equal((.6, .45, .3, .3), Tuple(vm.Redact.Boxes[1]), new BoxComparer());
        Assert.Equal((.1, .1, .2, .3), Tuple(vm.Redact.Boxes[0]), new BoxComparer());

        // Its bottom right handle.
        Drag(window, At(window, Picture, .9, .75), At(window, Picture, .95, .95));
        Assert.Equal((.6, .45, .35, .5), Tuple(vm.Redact.Boxes[1]), new BoxComparer());

        // The other box, picked by a press on it, moves from there.
        Drag(window, At(window, Picture, .2, .25), At(window, Picture, .3, .25));
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal((.2, .1, .2, .3), Tuple(vm.Redact.Boxes[0]), new BoxComparer());

        vm.UndoCommand.Execute(null); // each drag one step
        Assert.Equal(.1, vm.Redact.Boxes[0].X, 2);
        vm.UndoCommand.Execute(null);
        Assert.Equal(.3, vm.Redact.Boxes[1].Width, 2);
        vm.UndoCommand.Execute(null);
        Assert.Equal(.5, vm.Redact.Boxes[1].X, 2);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Delete_removes_the_selected_box()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .2, .3), Box(.5, .5, .3, .3), Box(.8, .1, .1, .1));

        // Nothing picked: Delete takes nothing away.
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Assert.Equal(3, vm.Redact.Boxes.Count);

        var inside = At(window, Picture, .65, .65);
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Assert.Equal([(.1, .1, .2, .3), (.8, .1, .1, .1)], vm.Redact.Boxes.Select(Tuple), new BoxComparer());
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Equal("상자 2개", vm.BoxCountText);

        window.MouseDown(At(window, Picture, .2, .2), MouseButton.Left);
        window.MouseUp(At(window, Picture, .2, .2), MouseButton.Left);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal([(.8, .1, .1, .1)], vm.Redact.Boxes.Select(Tuple), new BoxComparer());

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); // one step each
        Assert.Equal(2, vm.Redact.Boxes.Count);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(3, vm.Redact.Boxes.Count);
        Assert.Equal(1, vm.SelectedIndex); // the box that was picked is picked again
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_drag_is_dropped_when_the_boxes_change_under_it()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .2, .3), Box(.5, .5, .3, .3));

        // The first box is being moved when Delete takes it away: letting go must not put it over the second one, which
        // would show what that one hides.
        var from = At(window, Picture, .2, .25);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(20, 20));
        Assert.Equal(0, vm.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        window.MouseMove(from + new Point(40, 40));
        window.MouseUp(from + new Point(40, 40), MouseButton.Left);
        Assert.Equal([(.5, .5, .3, .3)], vm.Redact.Boxes.Select(Tuple), new BoxComparer());

        // The box left, now the first, is being moved when Ctrl+Z brings the deleted one back: the undo stands.
        from = At(window, Picture, .65, .65);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(-20, -20));
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        window.MouseMove(from + new Point(-40, -40));
        window.MouseUp(from + new Point(-40, -40), MouseButton.Left);
        Assert.Equal([(.1, .1, .2, .3), (.5, .5, .3, .3)], vm.Redact.Boxes.Select(Tuple), new BoxComparer());
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Box_is_clamped_inside_the_picture()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));

        // Drawn out past the right and bottom edges, and past the top and left ones.
        Drag(window, At(window, Picture, .7, .6), At(window, Picture, 1.02, 1.5));
        Drag(window, At(window, Picture, .2, .2), At(window, Picture, -.02, -.3));
        Assert.Equal((.7, .6, .3, .4), Tuple(vm.Redact.Boxes[0]), new BoxComparer());
        Assert.Equal((0, 0, .2, .2), Tuple(vm.Redact.Boxes[1]), new BoxComparer());

        // Moved out: it stops at the edge, with its size. The second box first goes inside, then out each way.
        Drag(window, At(window, Picture, .1, .1), At(window, Picture, .4, .5));
        Assert.Equal((.3, .4, .2, .2), Tuple(vm.Redact.Boxes[1]), new BoxComparer());
        Drag(window, At(window, Picture, .4, .5), At(window, Picture, 1.02, 1.5));
        Assert.Equal((.8, .8, .2, .2), Tuple(vm.Redact.Boxes[1]), new BoxComparer());
        Drag(window, At(window, Picture, .9, .9), At(window, Picture, -.02, -.3));
        Assert.Equal((0, 0, .2, .2), Tuple(vm.Redact.Boxes[1]), new BoxComparer());
        Assert.Equal((.7, .6, .3, .4), Tuple(vm.Redact.Boxes[0]), new BoxComparer());

        foreach (var box in vm.Redact.Boxes)
        {
            Assert.InRange(box.X, 0, 1);
            Assert.InRange(box.Y, 0, 1);
            Assert.InRange(box.X + box.Width, 0, 1 + 1e-9);
            Assert.InRange(box.Y + box.Height, 0, 1 + 1e-9);
        }
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_box_under_half_a_percent_is_not_made_and_a_resize_to_that_is_refused()
    {
        // A wide window, so half a percent of the picture is more than the 3 DIPs that make a press a drag.
        var (window, vm) = Open(TestStore(), 1500, ImageToolWindowTests.TestImages.Png(400, 200));
        var canvas = window.GetVisualDescendants().OfType<RedactCanvas>().Single();
        var shown = PictureView.ImageRect(canvas.Bounds.Size, Picture, 1, default);
        Assert.True(shown.Width * .005 > 3.5 && shown.Height * .005 > 2.2, $"the picture is {shown.Width} × {shown.Height} DIPs");
        var start = At(window, Picture, .3, .3);
        Drag(window, start, start + new Point(3.5, 100)); // narrower than 0.5 % of the width, yet a drag (3 DIPs or more)
        Assert.Empty(vm.Redact.Boxes);
        Drag(window, start, start + new Point(100, 2.0)); // lower than 0.5 % of the height
        Assert.Empty(vm.Redact.Boxes);

        Drag(window, start, start + new Point(30, 30));
        var box = Assert.Single(vm.Redact.Boxes);
        Assert.True(box.Width >= .005 && box.Height >= .005);

        // Pulling its right edge back to the left edge leaves it as it was.
        var before = Tuple(box);
        var right = At(window, Picture, box.X + box.Width, box.Y + box.Height / 2);
        var left = At(window, Picture, box.X + 0.002, box.Y + box.Height / 2);
        Drag(window, right, left);
        Assert.Equal(before, Tuple(vm.Redact.Boxes[0]), new BoxComparer());
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Mode_key_M_switches_to_mosaic()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        Assert.Equal(RedactMode.Black, vm.Mode.Value);

        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        Assert.Equal(RedactMode.Mosaic, vm.Redact.Mode);
        var chips = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.True(chips.Single(c => c.DataContext is Choice<RedactMode> { Value: RedactMode.Mosaic }).IsSelected);

        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(RedactMode.Black, vm.Mode.Value);
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control); // Ctrl+M is not the key
        Assert.Equal(RedactMode.Black, vm.Mode.Value);

        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); // the mode is one undo step
        Assert.Equal(RedactMode.Black, vm.Mode.Value);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Mode_key_U_switches_to_blur_and_Save_remembers_it()
    {
        var store = TestStore();
        var (window, vm) = Open(store, 900, ImageToolWindowTests.TestImages.Png(400, 200));
        Drag(window, At(window, Picture, .1, .2), At(window, Picture, .6, .8));

        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.None);
        Assert.Equal(RedactMode.Blur, vm.Mode.Value);
        Assert.Equal(RedactMode.Blur, vm.Redact.Mode);
        var chips = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(3, chips.Count(c => c.DataContext is Choice<RedactMode>));
        Assert.True(chips.Single(c => c.DataContext is Choice<RedactMode> { Value: RedactMode.Blur }).IsSelected);

        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.Control); // Ctrl+U is not the key
        Assert.Equal(RedactMode.Blur, vm.Mode.Value);
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); // the mode is one undo step
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(RedactMode.Blur, vm.Result!.Shared!.Redact!.Mode);
        Assert.Equal(RedactMode.Blur, store.Settings.ImageTools.RedactMode); // the next window starts on it
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Enter_saves_only_with_a_box_and_the_save_button_follows()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.SaveCommand);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Assert.False(save.IsEffectivelyEnabled); // the command keeps it off

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(closed);
        Assert.Null(vm.Result);

        Drag(window, At(window, Picture, .1, .2), At(window, Picture, .6, .8));
        Assert.True(save.IsEffectivelyEnabled);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(closed);
        Assert.Single(vm.Result!.Shared!.Redact!.Boxes);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void After_a_chip_is_used_with_the_mouse_the_keys_still_reach_the_picture()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(200, 400));
        vm.Redact = Boxes(RedactMode.Black, Box(.1, .1, .3, .3));
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

        // The focus is off the picture (as after another window had it): the chip, which takes none itself, must bring it back.
        window.FocusManager!.Focus(null);
        Assert.Null(window.FocusManager.GetFocusedElement());
        var mosaic = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is Choice<RedactMode> { Value: RedactMode.Mosaic });
        window.MouseDown(Centre(mosaic), MouseButton.Left);
        window.MouseUp(Centre(mosaic), MouseButton.Left);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        Assert.IsType<RedactCanvas>(window.FocusManager!.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); // the files, not the chips
        Assert.Equal(1, vm.Index);
        Assert.Single(vm.Redact.Boxes); // the same boxes on the next file
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(RedactMode.Black, vm.Mode.Value);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void The_delete_button_follows_the_picked_box_and_hands_the_keys_back()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(400, 200));
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.DeleteSelectionCommand);
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        Assert.False(vm.HasSelection);
        Assert.False(button.IsEffectivelyEnabled); // no box picked: nothing to delete

        Drag(window, At(window, Picture, .1, .2), At(window, Picture, .4, .5));
        Drag(window, At(window, Picture, .5, .5), At(window, Picture, .9, .9));
        Assert.Equal(2, vm.Redact.Boxes.Count);
        Assert.True(vm.HasSelection); // a box just drawn is the picked one
        Assert.True(button.IsEffectivelyEnabled);

        window.FocusManager!.Focus(null);
        window.MouseDown(Centre(button), MouseButton.Left);
        window.MouseUp(Centre(button), MouseButton.Left);
        var left = Assert.Single(vm.Redact.Boxes);
        Assert.Equal(.1, left.X, 2); // the second box went, the picked one
        Assert.False(vm.HasSelection);
        Assert.False(button.IsEffectivelyEnabled);
        Assert.IsType<RedactCanvas>(window.FocusManager.GetFocusedElement()); // the keys are the picture's again

        vm.UndoCommand.Execute(null); // one undo step, with the box picked again
        Assert.Equal(2, vm.Redact.Boxes.Count);
        Assert.True(vm.HasSelection);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_box_that_is_not_picked_still_shows_a_thin_outline_where_a_mosaic_changes_nothing()
    {
        // A flat picture: a mosaic on it looks like no box at all, which is what the outline is for.
        var flat = Avalonia.Media.Color.FromRgb(70, 130, 180);
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Flat(1000, 700, MagickColors.SteelBlue));
        var size = new Size(1000, 700);
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Drag(window, At(window, size, .2, .3), At(window, size, .6, .7));
        var corner = At(window, size, .95, .95);
        window.MouseDown(corner, MouseButton.Left); // a click on the bare picture lets go of the box
        window.MouseUp(corner, MouseButton.Left);
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Equal(RedactMode.Mosaic, vm.Mode.Value);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame()!;
        Screenshot(window, "tool-redact-unpicked-ko.png");
        bool Differs(Avalonia.Media.Color c) => Math.Abs(c.R - flat.R) + Math.Abs(c.G - flat.G) + Math.Abs(c.B - flat.B) > 60;
        var top = At(window, size, .2, .3);
        var edge = from i in Enumerable.Range(0, 60) from dy in Enumerable.Range(-2, 5) select PixelAt(frame, new Point(top.X + 20 + i, top.Y + dy));
        Assert.Contains(edge, Differs); // a dash of the outline along the box's top edge

        // The picked box is drawn as before, with handles; the outline is the preview's only, never part of the saved edit.
        Assert.False(Differs(PixelAt(frame, At(window, size, .4, .5)))); // inside: still the picture's own colour
        Assert.False(Differs(PixelAt(frame, At(window, size, .1, .5))));
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void The_window_shows_the_panel_draws_the_hidden_box_and_leaves_a_screenshot()
    {
        var (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(1000, 700));
        var size = new Size(1000, 700);
        Assert.Equal("가리기 · photo.png", window.Title);
        Assert.Single(window.GetVisualDescendants().OfType<PictureView>());
        Assert.Equal(3, window.GetVisualDescendants().OfType<ListBoxItem>().Count());
        foreach (var text in new[] { "방식", "검은 상자", "모자이크", "흐림", "상자 0개", "그림 위를 끌어서 가릴 곳을 고르세요", "결과에서 사진 정보(EXIF)도 지워져요" })
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == text);

        // Nothing to hide yet: Save is off.
        Screenshot(window, "tool-redact-empty-ko.png");

        Drag(window, At(window, size, .2, .3), At(window, size, .6, .7));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var centre = At(window, size, .4, .5);

        using var frame = window.CaptureRenderedFrame()!;
        Assert.Equal(Avalonia.Media.Colors.Black, PixelAt(frame, centre)); // hidden, drawn by the renderer
        Assert.NotEqual(Avalonia.Media.Colors.Black, PixelAt(frame, At(window, size, .1, .5))); // the picture beside it
        var corner = PixelAt(frame, At(window, size, .6, .7)); // the picked box's outline and handle: light on dark
        Assert.True(corner.R > 200 && corner.G > 200 && corner.B > 200, $"a white handle at the corner, not {corner}");
        Screenshot(window, "tool-redact-ko.png");

        // Mosaic: the box is blocks of the picture's colours, no longer black.
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var mosaic = window.CaptureRenderedFrame()!;
        Assert.NotEqual(Avalonia.Media.Colors.Black, PixelAt(mosaic, centre));
        Screenshot(window, "tool-redact-mosaic-ko.png");

        // Blur: the same box, the blocks softened (the renderer that saves draws it here too). Not the mosaic left as it was:
        // down a line through the box the blur has smoothed the steps between the cells, so the screen differs from the mosaic's.
        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var blur = window.CaptureRenderedFrame()!;
        var softened = Enumerable.Range(-60, 121).Count(dy => PixelAt(blur, centre + new Point(0, dy)) != PixelAt(mosaic, centre + new Point(0, dy)));
        Assert.True(softened > 10, $"only {softened} of 121 pixels down the box differ from the mosaic's");
        Assert.NotEqual(Avalonia.Media.Colors.Black, PixelAt(blur, centre));
        Screenshot(window, "tool-redact-blur-ko.png");
        window.Close();
        vm.Dispose();
    }

    /// <summary>
    /// The window in the other looks and modes (the light classic look is the screenshots above): the mode chips, the delete
    /// button (on, with a box picked), the count and the note on dark and on peach glass, with two boxes drawn.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ThemeStyle.Classic, ThemeMode.Dark)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Light)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Dark)]
    public void The_window_looks_right_in_every_look_and_mode(ThemeStyle style, ThemeMode mode)
    {
        Loc("ko");
        var store = AppHost.Get<UserDataStore>();
        var themes = AppHost.Get<ThemeService>();
        var before = store.Settings.Theme.Clone();
        ImageToolWindow? window = null;
        RedactToolViewModel? vm = null;
        try
        {
            // In the store: every Loc() of the tests applies the stored look again.
            store.Settings.Theme.Style = style;
            store.Settings.Theme.Mode = mode;
            themes.Apply(store.Settings.Theme, "ko");
            (window, vm) = Open(TestStore(), 900, ImageToolWindowTests.TestImages.Png(1000, 700));
            var size = new Size(1000, 700);
            Drag(window, At(window, size, .1, .15), At(window, size, .4, .5));
            Drag(window, At(window, size, .5, .4), At(window, size, .85, .8)); // the picked one
            Assert.Equal(2, vm.Redact.Boxes.Count);
            Assert.True(vm.DeleteSelectionCommand.CanExecute(null));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            using (var frame = window.CaptureRenderedFrame()!)
                Assert.Equal(Avalonia.Media.Colors.Black, PixelAt(frame, At(window, size, .1, .15) + new Point(8, 8))); // black in every look
            Screenshot(window, $"tool-redact-{style}-{mode}.png".ToLowerInvariant());
        }
        finally
        {
            window?.Close();
            vm?.Dispose();
            store.Settings.Theme.Style = before.Style;
            store.Settings.Theme.Mode = before.Mode;
            themes.Apply(store.Settings.Theme, "ko");
        }
    }

    // ───────── Find what to hide ─────────

    /// <summary>A platform that reads <paramref name="read"/> on any picture, and keeps what picture it was given.</summary>
    private sealed class FakeRecognizer(RecognitionResult read) : IPlatformServices
    {
        /// <summary>The picture's path, its size and format, as it was when it came.</summary>
        public (string Path, int Width, int Height, MagickFormat Format)? Given { get; private set; }

        public Task<RecognitionResult> RecognizeAsync(string imagePath, CancellationToken ct)
        {
            var info = new MagickImageInfo(imagePath);
            Given = (imagePath, (int)info.Width, (int)info.Height, info.Format);
            return Task.FromResult(read);
        }

        public bool IsFileManagerAt(int x, int y) => false;
        public string? ProcessNameAt(int x, int y) => null;
        public IReadOnlyList<string> GetFileManagerSelection() => [];
        public int SystemDragThreshold => 4;
        public bool PrefersReducedMotion => false;
        public void SetStartWithSystem(bool enabled, string executablePath) { }
        public void SetContextMenu(bool enabled, string executablePath, string label) { }
        public ModernContextMenuState GetModernContextMenuState(string executablePath) => ModernContextMenuState.Unsupported;
        public Task<ModernContextMenuResult> SetModernContextMenuAsync(bool enabled, string executablePath) => Task.FromResult(new ModernContextMenuResult(false));
        public void RevealInFileManager(string path) { }
        public void RaiseTopmost(nint windowHandle) { }
    }

    /// <summary>A line of words, each (text, x, y, width, height) in pixels.</summary>
    private static RecognizedLine Line(params (string Text, double X, double Y, double W, double H)[] words) =>
        new(string.Join(' ', words.Select(w => w.Text)), [.. words.Select(w => new RecognizedWord(w.Text, w.X, w.Y, w.W, w.H))]);

    /// <summary>On a 400 × 200 picture: "Tel 010-1234-5678" at the top, an e-mail address below it, and a face on the right.</summary>
    private static readonly RecognitionResult Read = new(
        [Line(("Tel", 20, 20, 40, 20), ("010-1234-5678", 70, 20, 150, 20)), Line(("kim@example.com", 20, 80, 160, 20))],
        [new PixelBox(250, 60, 80, 100)], TextAvailable: true);

    /// <summary>Runs the UI thread until the find that was started is over.</summary>
    private static void WaitForFind(RedactToolViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.IsFinding && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.False(vm.IsFinding);
    }

    [AvaloniaFact]
    public void Find_lists_what_was_read_and_hiding_the_chosen_adds_their_boxes_as_one_step()
    {
        var platform = new FakeRecognizer(Read);
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        vm.Recognize = platform.RecognizeAsync;
        vm.Mode = vm.Modes.Single(m => m.Value == RedactMode.Mosaic);
        vm.Redact = Boxes(RedactMode.Mosaic, Box(.8, .8, .1, .1)); // a box drawn by hand before
        Assert.True(vm.FindCommand.CanExecute(null));

        vm.FindCommand.Execute(null);
        WaitForFind(vm);

        // A PNG copy of the picture went to the platform, and is gone again.
        Assert.Equal((400, 200, MagickFormat.Png), (platform.Given!.Value.Width, platform.Given.Value.Height, platform.Given.Value.Format));
        Assert.False(File.Exists(platform.Given.Value.Path));
        Assert.Equal(["전화번호", "이메일", "얼굴"], vm.Spots.Select(s => s.KindText));
        Assert.Equal(["010-1234-5678", "kim@example.com", ""], vm.Spots.Select(s => s.Text));
        Assert.All(vm.Spots, s => Assert.True(s.IsChosen)); // all chosen at first
        Assert.Equal(3, vm.ChosenSpots.Count);
        Assert.Equal("", vm.FindStatus);
        Assert.Single(vm.Redact.Boxes); // found is not hidden yet

        vm.Spots[1].IsChosen = false; // the e-mail address may stay
        Assert.Equal([SensitiveKind.Phone, SensitiveKind.Face], vm.ChosenSpots.Select(s => s.Kind));
        vm.HideChosenCommand.Execute(null);

        Assert.Equal(RedactMode.Mosaic, vm.Redact.Mode); // in the way of hiding chosen
        Assert.Equal(3, vm.Redact.Boxes.Count);
        // The number's word, 3 px larger on every side (15 % of its height), as fractions of the picture.
        Assert.Equal((67 / 400.0, 17 / 200.0, 156 / 400.0, 26 / 200.0), Tuple(vm.Redact.Boxes[1]), new BoxComparer());
        Assert.Equal((235 / 400.0, 45 / 200.0, 110 / 400.0, 130 / 200.0), Tuple(vm.Redact.Boxes[2]), new BoxComparer());
        Assert.Empty(vm.Spots); // offered once: another press would hide them twice
        Assert.Empty(vm.ChosenSpots);
        Assert.False(vm.HideChosenCommand.CanExecute(null));

        vm.UndoCommand.Execute(null); // one step
        Assert.Equal(Tuple(Box(.8, .8, .1, .1)), Tuple(Assert.Single(vm.Redact.Boxes)));
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_turned_photo_is_read_upright_as_the_window_shows_it()
    {
        var platform = new FakeRecognizer(RecognitionResult.Empty);
        var vm = Redact([ImageToolWindowTests.TestImages.TurnedJpeg(300, 200)], TestStore(), Loc("ko"));
        vm.Recognize = platform.RecognizeAsync;

        vm.FindCommand.Execute(null);
        WaitForFind(vm);

        // Stored 300 × 200 with EXIF orientation 6: shown, and read, 200 wide and 300 tall, so the boxes land where they show.
        Assert.Equal((200, 300, MagickFormat.Png), (platform.Given!.Value.Width, platform.Given.Value.Height, platform.Given.Value.Format));
        vm.Dispose();
    }

    public static TheoryData<RecognitionResult, string[], string> Outcomes => new()
    {
        { new RecognitionResult([], [new PixelBox(250, 60, 80, 100)], TextAvailable: false), ["얼굴"], "이 PC에는 글자 인식 언어가 없어요" },
        { RecognitionResult.Empty, [], "이 PC에는 글자 인식 언어가 없어요\n가릴 곳을 찾지 못했어요" },
        { new RecognitionResult([Line(("Hello", 20, 20, 60, 20))], [], TextAvailable: true), [], "가릴 곳을 찾지 못했어요" },
        { RecognitionResult.Empty with { Failed = true }, [], "찾다가 문제가 생겼어요. 다시 해 보세요" },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Outcomes))]
    public void The_line_under_the_button_says_what_came_of_it(RecognitionResult read, string[] kinds, string status)
    {
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        vm.Recognize = new FakeRecognizer(read).RecognizeAsync;
        vm.FindCommand.Execute(null);
        WaitForFind(vm);
        Assert.Equal(kinds, vm.Spots.Select(s => s.KindText));
        Assert.Equal(status, vm.FindStatus);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void While_it_looks_it_says_so_and_a_failure_is_one_line()
    {
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        var answer = new TaskCompletionSource<RecognitionResult>();
        var asked = false;
        vm.Recognize = (_, _) =>
        {
            asked = true;
            return answer.Task;
        };
        vm.FindCommand.Execute(null);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!asked && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(asked);
        Assert.True(vm.IsFinding);
        Assert.Equal("찾는 중…", vm.FindStatus);
        Assert.False(vm.FindCommand.CanExecute(null)); // one at a time
        answer.SetException(new IOException("the disk is gone"));
        WaitForFind(vm);
        Assert.Equal("찾다가 문제가 생겼어요. 다시 해 보세요", vm.FindStatus);
        Assert.True(vm.FindCommand.CanExecute(null));

        // A picture that can't be read is a failure too.
        var broken = Path.Combine(Path.GetTempPath(), "filee-app-tests", "broken-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(broken)!);
        File.WriteAllText(broken, "not a picture");
        var other = Redact([broken], TestStore(), Loc("ko"));
        other.Recognize = new FakeRecognizer(Read).RecognizeAsync;
        other.FindCommand.Execute(null);
        WaitForFind(other);
        Assert.Equal("찾다가 문제가 생겼어요. 다시 해 보세요", other.FindStatus);
        Assert.Empty(other.Spots);
        vm.Dispose();
        other.Dispose();
    }

    [AvaloniaFact]
    public void Every_kind_has_a_name_on_its_chip()
    {
        var read = new RecognitionResult(
            [Line(("010-1234-5678", 0, 0, 10, 10)), Line(("a@b.co", 0, 20, 10, 10)), Line(("900101-1234567", 0, 40, 10, 10)),
                Line(("1234-5678-9012-3456", 0, 60, 10, 10)), Line(("서울시", 0, 80, 10, 10), ("강남구", 20, 80, 10, 10), ("테헤란로", 40, 80, 10, 10), ("1", 60, 80, 10, 10))],
            [new PixelBox(100, 100, 50, 50)], TextAvailable: true);
        var vm = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        vm.Recognize = new FakeRecognizer(read).RecognizeAsync;
        vm.FindCommand.Execute(null);
        WaitForFind(vm);
        Assert.Equal(Enum.GetValues<SensitiveKind>(), vm.Spots.Select(s => s.Spot.Kind));
        Assert.Equal(["전화번호", "이메일", "주민등록번호", "카드번호", "주소", "얼굴"], vm.Spots.Select(s => s.KindText));
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Find_is_for_one_file_only_and_its_tip_says_why()
    {
        var one = Redact([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        var two = Redact([ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(200, 400)], TestStore(), Loc("ko"));
        Assert.True(one.FindCommand.CanExecute(null));
        Assert.Equal("전화번호, 이메일, 주민등록번호, 카드번호, 주소, 얼굴을 이 PC 안에서 찾아요", one.FindTip);
        Assert.False(two.FindCommand.CanExecute(null)); // the boxes go on every file, and a spot is one picture's
        Assert.Equal("가리기 상자는 모든 파일에 같이 쓰여서, 파일이 하나일 때만 찾을 수 있어요", two.FindTip);
        one.Dispose();
        two.Dispose();
    }

    [AvaloniaFact]
    public void The_panel_lists_the_spots_and_the_picture_outlines_them_but_the_result_only_has_the_hidden_ones()
    {
        var store = TestStore();
        var vm = Redact([ImageToolWindowTests.TestImages.Flat(400, 200, MagickColors.White)], store, Loc("ko"));
        vm.Recognize = new FakeRecognizer(Read).RecognizeAsync;
        var window = new ImageToolWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        WaitForPreview(vm);
        var size = new Size(400, 200);
        var find = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.FindCommand);
        Assert.True(ToolTip.GetShowOnDisabled(find)); // with several files, the tip says why it is off

        vm.FindCommand.Execute(null);
        WaitForFind(vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        foreach (var text in new[] { "가릴 곳 찾기", "전화번호", "010-1234-5678", "이메일", "kim@example.com", "얼굴", "고른 곳 가리기" })
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == text);
        Assert.Equal(3, window.GetVisualDescendants().OfType<CheckBox>().Count(c => c.IsChecked == true));

        // The phone number's outline is orange on the white picture; the result holds no box yet (Save is off).
        var phone = vm.Spots[0].Spot.Box;
        using (var frame = window.CaptureRenderedFrame()!)
        {
            var top = At(window, size, phone.X + phone.Width / 2, phone.Y);
            var edge = from dy in Enumerable.Range(-2, 5) select PixelAt(frame, top + new Point(0, dy));
            Assert.Contains(edge, c => c.R > 140 && c.G is > 60 and < 130 && c.B < 60);
        }
        Assert.False(vm.SaveCommand.CanExecute(null));
        Screenshot(window, "tool-redact-find-ko.png");

        window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.HideChosenCommand).Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var hidden = window.CaptureRenderedFrame()!)
            Assert.Equal(Avalonia.Media.Colors.Black, PixelAt(hidden, At(window, size, phone.X + phone.Width / 2, phone.Y + phone.Height / 2)));
        Assert.Empty(window.GetVisualDescendants().OfType<CheckBox>());

        vm.SaveCommand.Execute(null);
        Assert.Equal(3, vm.Result!.Shared!.Redact!.Boxes.Count); // the boxes, and nothing of the outlines
        Assert.Null(vm.Result.Shared.Annotations);
        window.Close();
        vm.Dispose();
    }

    private static void Screenshot(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new PngBitmapEncoderOptions());
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

    private static (double X, double Y, double Width, double Height) Tuple(EditRect box) => (box.X, box.Y, box.Width, box.Height);

    /// <summary>Compares boxes to two decimals: a drag lands between pixels.</summary>
    private sealed class BoxComparer : IEqualityComparer<(double X, double Y, double Width, double Height)>
    {
        public bool Equals((double X, double Y, double Width, double Height) a, (double X, double Y, double Width, double Height) b) =>
            Math.Abs(a.X - b.X) < .015 && Math.Abs(a.Y - b.Y) < .015 && Math.Abs(a.Width - b.Width) < .015 && Math.Abs(a.Height - b.Height) < .015;

        public int GetHashCode((double X, double Y, double Width, double Height) box) => 0;
    }
}
