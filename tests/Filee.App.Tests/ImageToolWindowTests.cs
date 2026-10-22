// The crop window: the box it opens with, one box over several files, the size boxes, undo, what it hands back, the
// keys and a drag on the picture, where it opens, and the geometry of the canvas (fitting, zooming, handles, resizing).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Tools;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;

namespace Filee.App.Tests;

public class ImageToolWindowTests
{
    /// <summary>Real pictures in a fresh temp folder.</summary>
    internal static class TestImages
    {
        private static string Folder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "filee-app-tests", "crop-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string Png(int width, int height, string name = "photo.png")
        {
            var path = Path.Combine(Folder(), name);
            using var image = new MagickImage("gradient:#4682B4-#F2A65A", (uint)width, (uint)height);
            image.Write(path, MagickFormat.Png);
            return path;
        }

        /// <summary>A picture of one colour: a mosaic on it changes nothing, so anything else at a box is the box's outline.</summary>
        public static string Flat(int width, int height, MagickColor color)
        {
            var path = Path.Combine(Folder(), "flat.png");
            using var image = new MagickImage(color, (uint)width, (uint)height);
            image.Write(path, MagickFormat.Png);
            return path;
        }

        /// <summary>Vertical bands side by side, one colour each and equally wide: a pick tells which band it landed on.</summary>
        public static string Bands(int width, int height, params string[] colors)
        {
            var path = Path.Combine(Folder(), "bands.png");
            using var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height);
            for (var i = 0; i < colors.Length; i++)
            {
                using var band = new MagickImage(new MagickColor(colors[i]), (uint)(width / colors.Length), (uint)height);
                image.Composite(band, i * (width / colors.Length), 0, CompositeOperator.Over);
            }
            image.Write(path, MagickFormat.Png);
            return path;
        }

        /// <summary>The left half of the picture see-through altogether and the right half red at half opacity: a pick on each.</summary>
        public static string HalfClear(int width, int height)
        {
            var path = Path.Combine(Folder(), "half-clear.png");
            using var image = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
            using var half = new MagickImage(new MagickColor("#FF000080"), (uint)(width / 2), (uint)height);
            image.Composite(half, width / 2, 0, CompositeOperator.Over);
            image.Write(path, MagickFormat.Png);
            return path;
        }

        /// <summary>Grey with a single red pixel at (<paramref name="x"/>, <paramref name="y"/>): a pick lands on it only if it is as sharp as a pixel.</summary>
        public static string Dot(int width, int height, int x, int y)
        {
            var path = Path.Combine(Folder(), "dot.png");
            using var image = new MagickImage(new MagickColor("#808080"), (uint)width, (uint)height);
            using var dot = new MagickImage(MagickColors.Red, 1, 1);
            image.Composite(dot, x, y, CompositeOperator.Over);
            image.Write(path, MagickFormat.Png);
            return path;
        }

        /// <summary>A JPEG stored as <paramref name="width"/> × <paramref name="height"/> that a phone turned: EXIF orientation 6.</summary>
        public static string TurnedJpeg(int width, int height)
        {
            var path = Path.Combine(Folder(), "phone.jpg");
            using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height) { Orientation = OrientationType.RightTop };
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Write(path, MagickFormat.Jpeg);
            return path;
        }
    }

    /// <summary>A settings store of its own, so a test's remembered ratio doesn't reach the others.</summary>
    private static UserDataStore TestStore() =>
        new(Path.Combine(Path.GetTempPath(), "filee-app-tests", "crop-store-" + Guid.NewGuid().ToString("N")[..8]));

    private static ILocalizer Loc(string language)
    {
        TestServices.EnsureInitialized(language);
        return AppHost.Get<ILocalizer>();
    }

    private static CropToolViewModel Crop(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc) =>
        Assert.IsType<CropToolViewModel>(ImageToolViewModel.Create(ImageToolKind.Crop, files, store, loc));

    /// <summary>Runs the UI thread until the preview, read on another thread, has arrived.</summary>
    private static void WaitForPreview(ImageToolViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.Preview is null && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.NotNull(vm.Preview);
    }

    // ───────── The view model ─────────

    [AvaloniaFact]
    public void Opens_with_the_last_aspect_as_the_largest_centred_box()
    {
        var store = TestStore();
        store.Settings.ImageTools.CropAspect = "1:1";
        var vm = Crop([TestImages.Png(400, 200)], store, Loc("ko"));
        Assert.Equal("1:1", vm.Aspect.Value);
        Assert.Equal((200, 200), (vm.PixelWidth, vm.PixelHeight));
        Assert.Equal(.25, vm.Crop.X, 3);
        Assert.Equal("자르기 · photo.png", vm.Title);
        Assert.Equal("원본은 그대로 두고 옆에 새 파일로 저장해요", vm.FooterText); // one file: the original stays
        Assert.Equal(CropMath.AspectIds, vm.Aspects.Select(a => a.Value));
        Assert.Equal("자유", vm.Aspects[0].Label);
    }

    [AvaloniaFact]
    public void Next_file_shows_the_same_box_refitted_and_save_returns_one_shared_edit()
    {
        var vm = Crop([TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("ko"));
        Assert.Equal("2개 중 1", vm.PositionText);
        Assert.Equal("파일 2개에 똑같이 적용돼요", vm.FooterText);
        vm.Aspect = vm.Aspects.Single(a => a.Value == "1:1");
        vm.NextCommand.Execute(null);
        Assert.Equal("2개 중 2", vm.PositionText);
        Assert.Equal(vm.PixelWidth, vm.PixelHeight);
        Assert.Equal((100, 100), (vm.PixelWidth, vm.PixelHeight)); // half the width of the portrait picture
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Equal(1.0, vm.Result!.Shared!.Crop!.Aspect);
        Assert.Equal(.5, vm.Result.Shared!.Crop.Width, 6);
        Assert.Null(vm.Result.PerFile); // one edit for every file
    }

    [AvaloniaFact]
    public void Save_on_several_files_returns_the_box_as_set_not_as_refitted_on_the_file_shown()
    {
        // The engine refits the one box on every file (CropMath.Refit), as the preview did: so Save hands back the box
        // itself. A 1:1 box on the 400 × 200 file is half its width and all of its height; on the 200 × 400 file it shows
        // as all of the width and a quarter of the height.
        var vm = Crop([TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("ko"));
        vm.Aspect = vm.Aspects.Single(a => a.Value == "1:1");
        vm.NextCommand.Execute(null);
        Assert.Equal(.25, vm.Crop.Height, 6); // the one shown
        vm.SaveCommand.Execute(null);
        var saved = vm.Result!.Shared!.Crop!;
        Assert.Equal((.25, 0.0, .5, 1.0, (double?)1.0), (saved.X, saved.Y, saved.Width, saved.Height, saved.Aspect));

        // "원본" (each file's own shape) on several files: saved without a ratio, so every file keeps its own.
        vm = Crop([TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("ko"));
        Assert.Equal("원본", vm.Aspect.Label);
        vm.PixelWidth = 200;
        vm.NextCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Null(vm.Result!.Shared!.Crop!.Aspect);
        Assert.Equal((.5, .5), (vm.Result.Shared!.Crop.Width, vm.Result.Shared!.Crop.Height));
    }

    [AvaloniaFact]
    public void Save_finishes_even_when_the_ratio_cannot_be_remembered()
    {
        // The settings folder is a file: saving the settings throws, as on a full disk or a locked file.
        var blocked = Path.Combine(Path.GetTempPath(), "filee-app-tests", "crop-blocked-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "");
        var store = new UserDataStore(blocked);
        Assert.ThrowsAny<IOException>(store.SaveSettings);

        var vm = Crop([TestImages.Png(400, 200)], store, Loc("ko"));
        var finished = false;
        vm.Finished += (_, _) => finished = true;
        vm.SaveCommand.Execute(null);

        Assert.True(finished);
        Assert.NotNull(vm.Result);
        Assert.Equal(vm.Aspect.Value, store.Settings.ImageTools.CropAspect); // remembered for this run all the same
    }

    [AvaloniaFact]
    public void Typing_a_pixel_width_keeps_a_fixed_ratio_and_save_remembers_the_aspect()
    {
        var store = TestStore();
        var vm = Crop([TestImages.Png(400, 200)], store, Loc("ko"));
        vm.Aspect = vm.Aspects.Single(a => a.Value == "16:9");
        vm.PixelWidth = 160;
        Assert.Equal(90, vm.PixelHeight);
        vm.PixelHeight = 45; // a fixed ratio follows: the width goes with it
        Assert.Equal(80, vm.PixelWidth);
        vm.SaveCommand.Execute(null);
        Assert.Equal("16:9", store.Settings.ImageTools.CropAspect);
        Assert.Equal("16:9", TestStoreAt(store.Directory).Settings.ImageTools.CropAspect); // saved, not just set
    }

    private static UserDataStore TestStoreAt(string directory)
    {
        var store = new UserDataStore(directory);
        store.Load();
        return store;
    }

    [AvaloniaFact]
    public void Undo_restores_the_previous_box_and_cancel_returns_nothing()
    {
        var vm = Crop([TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        var before = vm.Crop.Clone();
        vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
        vm.Crop = new CropRect { X = .1, Y = .1, Width = .2, Height = .2 };
        vm.UndoCommand.Execute(null);
        Assert.NotEqual(.2, vm.Crop.Width);
        Assert.Equal((before.X, before.Width), (vm.Crop.X, vm.Crop.Width));
        Assert.Equal("free", vm.Aspect.Value);
        vm.UndoCommand.Execute(null); // the ratio was a step of its own
        Assert.Equal("original", vm.Aspect.Value);
        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Equal(.2, vm.Crop.Width, 6);
        vm.CancelCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [AvaloniaFact]
    public void Original_keeps_each_files_own_shape()
    {
        // The box of "original" is the same fraction of either side: on another file it has that file's shape.
        var vm = Crop([TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("en"));
        Assert.Equal("original", vm.Aspect.Value);
        vm.PixelWidth = 200;
        Assert.Equal(100, vm.PixelHeight);
        vm.NextCommand.Execute(null);
        Assert.Equal((100, 200), (vm.PixelWidth, vm.PixelHeight));
        vm.SaveCommand.Execute(null);
        var crop = vm.Result!.Shared!.Crop!;
        Assert.Null(crop.Aspect); // so the engine keeps the fractions on every file
        Assert.Equal((100, 50), (CropMath.ToPixels(crop, 200, 100).Width, CropMath.ToPixels(crop, 200, 100).Height));
    }

    [AvaloniaFact]
    public void A_turned_phone_photo_is_measured_and_shown_the_way_it_stands()
    {
        var vm = Crop([TestImages.TurnedJpeg(400, 200)], TestStore(), Loc("en"));
        Assert.Equal((200, 400), (vm.ImageWidth, vm.ImageHeight));
        WaitForPreview(vm);
        Assert.Equal(new PixelSize(200, 400), vm.Preview!.PixelSize);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void The_preview_is_no_larger_than_2048_pixels()
    {
        var vm = Crop([TestImages.Png(3000, 1000)], TestStore(), Loc("en"));
        Assert.Equal((3000, 1000), (vm.ImageWidth, vm.ImageHeight));
        WaitForPreview(vm);
        Assert.Equal(2048, vm.Preview!.PixelSize.Width);
        vm.Dispose();
    }

    // ───────── The window ─────────

    private static (ImageToolWindow Window, CropToolViewModel Vm) Open(params string[] files)
    {
        var vm = Crop(files, TestStore(), Loc("ko"));
        var window = new ImageToolWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        WaitForPreview(vm);
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void Keys_move_between_files_pick_ratios_undo_and_enter_saves()
    {
        var (window, vm) = Open(TestImages.Png(400, 200), TestImages.Png(200, 400));
        Assert.IsType<Filee.App.Views.Tools.CropPanel>(window.FindControl<ContentControl>("ToolPanel")!.Content);
        Assert.Equal("자르기 · photo.png", window.Title);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(1, vm.Index);
        window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
        Assert.Equal("1:1", vm.Aspect.Value);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("original", vm.Aspect.Value);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("1:1", vm.Aspect.Value);

        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(closed);
        Assert.Equal(1.0, vm.Result!.Shared!.Crop!.Aspect);
    }

    [AvaloniaFact]
    public void Ctrl_C_copies_unless_a_box_is_being_typed_in_and_the_window_closes()
    {
        var (window, vm) = Open(TestImages.Png(400, 200));
        vm.CopiedHold = TimeSpan.Zero;
        var copies = 0;
        vm.CopyPicture = (_, _) =>
        {
            copies++;
            return Task.FromResult(true);
        };
        var copy = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.CopyCommand);
        Assert.Equal("복사", copy.Content);
        Assert.Equal("Ctrl+C", ToolTip.GetTip(copy));
        var closed = false;
        window.Closed += (_, _) => closed = true;

        // A size being typed keeps Ctrl+C for its text.
        Assert.True(window.GetVisualDescendants().OfType<TextBox>().First().Focus());
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Assert.Equal(0, copies);

        Assert.True(window.GetVisualDescendants().OfType<PictureView>().Single().Focus());
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, copies);
        Assert.True(closed);
        Assert.Null(vm.Result); // no file is written
    }

    [AvaloniaFact]
    public void Enter_and_Ctrl_C_wait_for_a_box_being_dragged()
    {
        // The box reaches the view model when it is let go: saved or copied before, it would be where it was.
        var (window, vm) = Open(TestImages.Png(400, 200));
        vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
        vm.CopiedHold = TimeSpan.Zero;
        var copied = new List<CropRect>();
        vm.CopyPicture = (_, edit) =>
        {
            copied.Add(edit.Crop!);
            return Task.FromResult(true);
        };
        var canvas = window.GetVisualDescendants().OfType<ImageCanvas>().Single();
        var image = ImageCanvas.ImageRect(canvas.Bounds.Size, new Size(400, 200), 1, default);
        Point At(double x, double y) => canvas.TranslatePoint(new Point(image.X + x * image.Width, image.Y + y * image.Height), window)!.Value;

        window.MouseDown(At(.1, .2), MouseButton.Left);
        window.MouseMove(At(.6, .8));
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Empty(copied);
        Assert.Null(vm.Result);

        window.MouseUp(At(.6, .8), MouseButton.Left);
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(.5, Assert.Single(copied).Width, 2);
    }

    [AvaloniaFact]
    public void The_window_takes_no_clicks_or_keys_while_it_copies()
    {
        // Nothing edited meanwhile would be in the copy, and closing after it would throw that away.
        var (window, vm) = Open(TestImages.Png(400, 200), TestImages.Png(200, 400));
        vm.CopiedHold = TimeSpan.Zero;
        var copies = 0;
        var clickable = true;
        vm.CopyPicture = (_, _) =>
        {
            copies++;
            clickable = ((Control)window.Content!).IsHitTestVisible;
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control); // no second copy
            return Task.FromResult(true);
        };

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, copies);
        Assert.False(clickable);
        Assert.Equal((0, "original"), (vm.Index, vm.Aspect.Value));
    }

    [AvaloniaFact]
    public void A_typed_size_is_taken_on_enter_and_the_next_enter_saves()
    {
        var (window, vm) = Open(TestImages.Png(400, 200));
        vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
        var width = window.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(width.Focus());
        width.SelectAll();
        window.KeyTextInput("100");
        window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None); // typing digits picks no ratio
        Assert.Equal("free", vm.Aspect.Value);
        Assert.Equal(400, vm.PixelWidth); // taken when the box is left, not at every key

        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(100, vm.PixelWidth);
        Assert.False(closed);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(closed);
        Assert.Equal(.25, vm.Result!.Shared!.Crop!.Width, 6);
    }

    private static Button ButtonOf(Window window, System.Windows.Input.ICommand command) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == command);

    [AvaloniaFact]
    public void The_top_bar_has_undo_and_redo_buttons_that_follow_the_steps_even_for_one_file()
    {
        var (window, vm) = Open(TestImages.Png(400, 200)); // one file: no ‹ ›, but undo and redo are there all the same
        var (undo, redo) = (ButtonOf(window, vm.UndoCommand), ButtonOf(window, vm.RedoCommand));
        Assert.True(undo.IsEffectivelyVisible && redo.IsEffectivelyVisible);
        Assert.False(undo.IsEffectivelyEnabled); // nothing done yet
        Assert.False(redo.IsEffectivelyEnabled);
        Assert.Equal("되돌리기 (Ctrl+Z)", ToolTip.GetTip(undo));
        Assert.Equal("다시 하기 (Ctrl+Y)", ToolTip.GetTip(redo));
        Assert.False(undo.Focusable); // the keys stay with the picture

        vm.Aspect = vm.Aspects.Single(a => a.Value == "1:1");
        Assert.True(undo.IsEffectivelyEnabled);
        Assert.False(redo.IsEffectivelyEnabled);

        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        window.FocusManager!.Focus(null); // as after another window had it: the press brings the keys back to the picture
        window.MouseDown(Centre(undo), MouseButton.Left);
        window.MouseUp(Centre(undo), MouseButton.Left);
        Assert.IsType<ImageCanvas>(window.FocusManager.GetFocusedElement());
        Assert.Equal("original", vm.Aspect.Value);
        Assert.False(undo.IsEffectivelyEnabled);
        Assert.True(redo.IsEffectivelyEnabled);

        window.MouseDown(Centre(redo), MouseButton.Left);
        window.MouseUp(Centre(redo), MouseButton.Left);
        Assert.Equal("1:1", vm.Aspect.Value);
        Assert.True(undo.IsEffectivelyEnabled);
        Assert.False(redo.IsEffectivelyEnabled);

        // A new step after an undo clears what could be redone; Reset is a step too.
        vm.UndoCommand.Execute(null);
        vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
        Assert.False(redo.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void Buttons_and_the_ratio_chips_tell_their_keys_in_tooltips()
    {
        var (window, vm) = Open(TestImages.Png(400, 200), TestImages.Png(200, 400));
        Assert.Equal("Enter", ToolTip.GetTip(ButtonOf(window, vm.SaveCommand)));
        Assert.Equal("Esc", ToolTip.GetTip(ButtonOf(window, vm.CancelCommand)));
        Assert.Equal("이전 파일 (←)", ToolTip.GetTip(ButtonOf(window, vm.PreviousCommand)));
        Assert.Equal("다음 파일 (→)", ToolTip.GetTip(ButtonOf(window, vm.NextCommand)));
        Assert.Equal("숫자 키 1~7로도 골라요", ToolTip.GetTip(window.GetVisualDescendants().OfType<ListBox>().Single()));
        window.Close();
    }

    [AvaloniaFact]
    public void One_file_says_the_original_stays_and_several_say_the_edit_goes_on_all()
    {
        var vm = Crop([TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        Assert.Equal("원본은 그대로 두고 옆에 새 파일로 저장해요", vm.FooterText);
        vm.Dispose();
        vm = Crop([TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("ko"));
        Assert.Equal("파일 2개에 똑같이 적용돼요", vm.FooterText);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Escape_closes_without_an_edit()
    {
        var (window, vm) = Open(TestImages.Png(400, 200));
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(closed);
        Assert.Null(vm.Result);
    }

    [AvaloniaFact]
    public void Dragging_on_the_picture_draws_a_new_box_and_a_double_click_in_it_saves()
    {
        var (window, vm) = Open(TestImages.Png(400, 200));
        vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
        var canvas = window.GetVisualDescendants().OfType<ImageCanvas>().Single();
        var image = ImageCanvas.ImageRect(canvas.Bounds.Size, new Size(400, 200), 1, default);
        Point At(double x, double y) => canvas.TranslatePoint(new Point(image.X + x * image.Width, image.Y + y * image.Height), window)!.Value;

        window.MouseDown(At(.1, .2), MouseButton.Left);
        window.MouseMove(At(.3, .5));
        window.MouseMove(At(.6, .8));
        window.MouseUp(At(.6, .8), MouseButton.Left);
        Assert.Equal(.1, vm.Crop.X, 2);
        Assert.Equal(.2, vm.Crop.Y, 2);
        Assert.Equal(.5, vm.Crop.Width, 2);
        Assert.Equal(.6, vm.Crop.Height, 2);

        // Moving it: the whole box goes along.
        window.MouseDown(At(.35, .5), MouseButton.Left);
        window.MouseMove(At(.45, .5));
        window.MouseUp(At(.45, .5), MouseButton.Left);
        Assert.Equal(.2, vm.Crop.X, 2);
        Assert.Equal(.5, vm.Crop.Width, 2);

        vm.UndoCommand.Execute(null); // each drag is one step
        Assert.Equal(.1, vm.Crop.X, 2);

        Assert.Null(vm.Result);
        window.MouseDown(At(.3, .5), MouseButton.Left); // a click changes nothing
        window.MouseUp(At(.3, .5), MouseButton.Left);
        Assert.Equal(.1, vm.Crop.X, 2);
        Assert.Null(vm.Result);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.MouseDown(At(.3, .5), MouseButton.Left); // the second one makes it a double click: saved and closed
        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal(.1, vm.Result.Shared!.Crop!.X, 2);
    }

    [AvaloniaFact]
    public void Opens_near_the_cursor_within_80_percent_of_the_screen_it_is_on()
    {
        // A big photo on a 150 % screen to the right of the main one, the cursor in its top-left corner.
        var area = new PixelRect(1920, 0, 2560, 1400);
        var (size, smallest, position) = ImageToolWindow.Placement(area, 1.5, new PixelPoint(1950, 30), new PixelSize(6000, 4000));
        Assert.Equal(ImageToolWindow.Smallest, smallest); // room enough for the usual smallest window
        Assert.InRange(size.Width, 0, 2560 * .8 / 1.5 + .01);
        Assert.InRange(size.Height, 0, 1400 * .8 / 1.5 + .01);
        Assert.Equal(new PixelPoint(1920, 0), position); // pushed onto the screen
        var picture = new Size(size.Width - ImageToolWindow.Chrome.Width, size.Height - ImageToolWindow.Chrome.Height);
        Assert.Equal(1.5, picture.Width / picture.Height, 2); // sized to the photo

        // A small picture: the smallest window, centred on the cursor.
        (size, _, position) = ImageToolWindow.Placement(new PixelRect(0, 0, 1920, 1040), 1, new PixelPoint(960, 500), new PixelSize(64, 64));
        Assert.Equal(ImageToolWindow.Smallest, size);
        Assert.Equal(new PixelPoint(960 - (int)(size.Width / 2), 500 - (int)(size.Height / 2)), position);
    }

    [AvaloniaFact]
    public void On_a_small_screen_the_window_fits_and_the_bottom_bar_stays_on_it()
    {
        // 1920 × 1040 px at 250 %: 768 × 416 DIPs of working area, lower than the usual smallest window (480).
        var area = new PixelRect(0, 0, 1920, 1040);
        var (size, smallest, position) = ImageToolWindow.Placement(area, 2.5, new PixelPoint(960, 900), new PixelSize(4000, 3000));
        Assert.InRange(size.Width, 0, 768 * .8 + .01);
        Assert.InRange(size.Height, 0, 416 * .8 + .01);
        Assert.True(smallest.Width <= size.Width && smallest.Height <= size.Height, $"{smallest} > {size}");
        Assert.InRange(position.Y, area.Y, area.Bottom - (int)(size.Height * 2.5)); // the whole window, bottom bar too
        Assert.InRange(position.X, area.X, area.Right - (int)(size.Width * 2.5));

        // At that size the picture gives way, not the bar with Save.
        var vm = Crop([TestImages.Png(400, 300)], TestStore(), Loc("ko"));
        var window = new ImageToolWindow { DataContext = vm, MinWidth = smallest.Width, MinHeight = smallest.Height, Width = size.Width, Height = size.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.SaveCommand);
        var bottom = save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y;
        Assert.Equal(size.Height, window.ClientSize.Height, 1);
        Assert.InRange(bottom, 0, window.ClientSize.Height);
        Assert.True(window.GetVisualDescendants().OfType<ImageCanvas>().Single().Bounds.Height > 0);
        window.Close();
        vm.Dispose();
    }

    // ───────── Canvas geometry ─────────

    [AvaloniaFact]
    public void The_picture_is_fitted_and_centred_and_points_map_to_fractions()
    {
        var bounds = new Size(400 + 2 * ImageCanvas.Inset, 300 + 2 * ImageCanvas.Inset);
        var image = ImageCanvas.ImageRect(bounds, new Size(200, 100), 1, default);
        Assert.Equal(new Rect(ImageCanvas.Inset, ImageCanvas.Inset + 50, 400, 200), image);
        Assert.Equal(new Point(.5, .5), ImageCanvas.ToFraction(new Point(bounds.Width / 2, bounds.Height / 2), image));
        var box = ImageCanvas.ToScreen(new CropRect { X = .25, Y = .5, Width = .5, Height = .5 }, image);
        Assert.Equal(new Rect(image.X + 100, image.Y + 100, 200, 100), box);
    }

    [AvaloniaFact]
    public void Zooming_keeps_the_point_under_the_cursor()
    {
        var bounds = new Size(800, 600);
        var picture = new Size(300, 200);
        var at = new Point(250, 180);
        var before = ImageCanvas.ImageRect(bounds, picture, 1, new Vector(10, -20));
        var pan = ImageCanvas.ZoomAt(bounds, picture, 1, new Vector(10, -20), at, 2.5);
        var after = ImageCanvas.ImageRect(bounds, picture, 2.5, pan);
        Assert.Equal(before.Width * 2.5, after.Width, 6);
        var (u, v) = (ImageCanvas.ToFraction(at, before), ImageCanvas.ToFraction(at, after));
        Assert.Equal(u.X, v.X, 6);
        Assert.Equal(u.Y, v.Y, 6);
    }

    [AvaloniaFact]
    public void Handles_and_edges_are_hit_before_the_inside()
    {
        var box = new Rect(100, 100, 200, 100);
        Assert.Equal(ImageCanvas.Grip.Left | ImageCanvas.Grip.Top, ImageCanvas.HitTest(box, new Point(103, 98), 8));
        Assert.Equal(ImageCanvas.Grip.Right | ImageCanvas.Grip.Bottom, ImageCanvas.HitTest(box, new Point(300, 200), 8));
        Assert.Equal(ImageCanvas.Grip.Right, ImageCanvas.HitTest(box, new Point(305, 150), 8));
        Assert.Equal(ImageCanvas.Grip.Bottom, ImageCanvas.HitTest(box, new Point(200, 196), 8));
        Assert.Equal(ImageCanvas.Grip.Move, ImageCanvas.HitTest(box, new Point(200, 150), 8));
        Assert.Equal(ImageCanvas.Grip.None, ImageCanvas.HitTest(box, new Point(50, 50), 8));
        Assert.Equal(ImageCanvas.Grip.None, ImageCanvas.HitTest(box, new Point(320, 150), 8));
    }

    [AvaloniaFact]
    public void A_corner_keeps_a_fixed_ratio_and_the_box_stays_on_the_picture()
    {
        var start = new CropRect { X = .25, Y = .25, Width = .5, Height = .5, Aspect = 2 };
        var grown = ImageCanvas.Resize(start, ImageCanvas.Grip.Right | ImageCanvas.Grip.Bottom, new Point(1.2, .8), ratio: 1);
        Assert.Equal((.25, .25, .75, .75), (grown.X, grown.Y, grown.Width, grown.Height)); // stopped by the right edge
        Assert.Equal(2, grown.Aspect); // the ratio it keeps on other files goes along

        // Pulled up a lot and left a little: the bigger pull sets the size, held by the left edge, from the fixed corner.
        var up = ImageCanvas.Resize(start, ImageCanvas.Grip.Left | ImageCanvas.Grip.Top, new Point(.6, 0), ratio: 2);
        Assert.Equal((0, .375, .75, .375), (up.X, up.Y, up.Width, up.Height));
    }

    [AvaloniaFact]
    public void An_edge_with_a_ratio_grows_the_other_side_around_the_centre()
    {
        var start = new CropRect { X = .4, Y = .4, Width = .2, Height = .2 };
        var wider = ImageCanvas.Resize(start, ImageCanvas.Grip.Right, new Point(.8, .9), ratio: 1);
        Assert.Equal(.4, wider.X, 6);
        Assert.Equal(.4, wider.Width, 6);
        Assert.Equal(.4, wider.Height, 6);
        Assert.Equal(.3, wider.Y, 6); // around the same middle

        var free = ImageCanvas.Resize(start, ImageCanvas.Grip.Right, new Point(.8, .9), ratio: null);
        Assert.Equal((.4, .4, .4, .2), (free.X, free.Y, Math.Round(free.Width, 6), free.Height)); // only that edge moved
    }

    [AvaloniaFact]
    public void A_new_box_is_drawn_from_where_the_drag_began_in_any_direction()
    {
        var anchor = new CropRect { X = .3, Y = .3, Width = 0, Height = 0 };
        var box = ImageCanvas.Resize(anchor, ImageCanvas.Grip.Right | ImageCanvas.Grip.Bottom, new Point(.1, .6), ratio: null);
        Assert.Equal(.1, box.X, 6);
        Assert.Equal(.3, box.Y, 6);
        Assert.Equal(.2, box.Width, 6);
        Assert.Equal(.3, box.Height, 6);
    }

    [AvaloniaFact]
    public void Moving_the_box_stops_at_the_edges()
    {
        var moved = ImageCanvas.Move(new CropRect { X = .5, Y = .5, Width = .4, Height = .4 }, new Vector(.3, -.9));
        Assert.Equal((.6, 0.0, .4, .4), (Math.Round(moved.X, 6), moved.Y, moved.Width, moved.Height));
    }
}
