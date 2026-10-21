// The add background window: what it opens with, the pixel values beside its sliders, one undo step per slider drag,
// the typed colour and the user's picture (large ones are prepared off the UI thread and previewed small), what it hands
// back and remembers, and the panel on the screen (the chips, the sliders, a preview in the shape of the result).

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
using Filee.Engines.Magick;
using ImageMagick;
using SkiaSharp;

namespace Filee.App.Tests;

public class BackgroundToolTests
{
    /// <summary>A settings store of its own, so one test's remembered backdrop doesn't reach the others.</summary>
    private static UserDataStore TestStore() =>
        new(Path.Combine(Path.GetTempPath(), "filee-app-tests", "background-store-" + Guid.NewGuid().ToString("N")[..8]));

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

    private static BackgroundToolViewModel Background(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc) =>
        Assert.IsType<BackgroundToolViewModel>(ImageToolViewModel.Create(ImageToolKind.AddBackground, files, store, loc));

    /// <summary>A solid-colour JPEG of this size in a folder of its own.</summary>
    private static string Jpeg(int width, int height, MagickColor color)
    {
        var folder = Path.Combine(Path.GetTempPath(), "filee-app-tests", "backdrop-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "backdrop.jpg");
        using var image = new MagickImage(color, (uint)width, (uint)height);
        image.Write(path, MagickFormat.Jpeg);
        return path;
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

    // ───────── The view model ─────────

    [AvaloniaFact]
    public void Opens_with_the_remembered_background()
    {
        var store = TestStore();
        store.Settings.ImageTools.Background = new BackgroundEdit
        {
            Kind = BackgroundKind.Solid,
            Value = "#336699",
            Padding = .12,
            Corner = .1,
            Shadow = 70,
            Aspect = "16:9",
        };
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));

        Assert.Equal("배경 넣기 · photo.png", vm.Title);
        Assert.Equal((12.0, 10.0, 70.0), (vm.Padding, vm.Corner, vm.Shadow));
        Assert.Equal("16:9", vm.Aspect.Value);
        Assert.Equal(["자동", "1:1", "16:9", "9:16", "4:5", "4:3"], vm.Aspects.Select(a => a.Label));
        Assert.Null(vm.SelectedSwatch); // no chip has this colour: it is the custom one
        Assert.True(vm.IsCustomColor);
        Assert.Equal("#336699", vm.CustomColorHex);
        Assert.Equal((BackgroundKind.Solid, "#336699"), (vm.PreviewEdit.Background!.Kind, vm.PreviewEdit.Background.Value));

        // The chips: the renderer's 8 gradients, then 6 colours, each named for people who cannot see it.
        Assert.Equal(8, vm.Swatches.Count(s => s.Kind == BackgroundKind.Gradient));
        Assert.Equal(["#FFFFFF", "#000000", "#8E8E93", "#FFD8BE", "#BEE3FF", "#BFF0DC"],
            vm.Swatches.Where(s => s.Kind == BackgroundKind.Solid).Select(s => s.Value));
        Assert.Equal(ImageEditRenderer.Gradients.Select(g => g.Id), vm.Swatches.Take(8).Select(s => s.Id));
        Assert.Equal("흰색", vm.Swatches.Single(s => s.Id == "solid-white").Label);
        Assert.Equal(vm.Swatches.Count, vm.Swatches.Select(s => s.Label).Distinct().Count());

        // Another start: a gradient is the chip with its id, and the settings alone are not changed by what happens here.
        store.Settings.ImageTools.Background = new BackgroundEdit { Value = "ocean" };
        var gradient = Background([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));
        Assert.Equal("ocean", gradient.SelectedSwatch!.Id);
        Assert.False(gradient.IsCustomColor);
        Assert.Equal((8.0, 3.0, 40.0), (gradient.Padding, gradient.Corner, gradient.Shadow)); // the spec's defaults
        gradient.Padding = 30;
        Assert.Equal(.08, store.Settings.ImageTools.Background.Padding); // only Save remembers
    }

    [AvaloniaFact]
    public void Padding_slider_shows_pixels_of_the_viewed_file()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(1000, 800), ImageToolWindowTests.TestImages.Png(500, 300)], TestStore(), Loc("ko"));
        Assert.Equal("64px", vm.PaddingText); // 8 % of the short side, 800
        Assert.Equal("24px", vm.CornerText);  // 3 % of 800
        Assert.Equal("40", vm.ShadowText);    // no pixels: a strength

        vm.Padding = 40;
        Assert.Equal("320px", vm.PaddingText);

        // Another file: the same percentage is another number of its pixels.
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.NextCommand.Execute(null);
        Assert.Equal("120px", vm.PaddingText);
        Assert.Equal("9px", vm.CornerText);
        Assert.Contains(nameof(BackgroundToolViewModel.PaddingText), changed);
        Assert.Contains(nameof(BackgroundToolViewModel.CornerText), changed);
    }

    [AvaloniaFact]
    public void The_result_size_in_pixels_follows_the_shape_the_padding_and_the_file_shown()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(300, 300)], TestStore(), Loc("ko"));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("결과 432 × 232px", vm.OutputText); // 8 % of the short side, 16 px, round the 400 × 200
        vm.Padding = 0;
        Assert.Equal("결과 400 × 200px", vm.OutputText);
        Assert.Contains(nameof(BackgroundToolViewModel.OutputText), changed);
        vm.Aspect = vm.Aspects.Single(a => a.Value == "16:9");
        Assert.Equal("결과 400 × 225px", vm.OutputText);
        vm.Padding = 40; // 80 px a side: 560 × 360 holds the picture, and 640 × 360 is the first 16:9 that does
        Assert.Equal("결과 640 × 360px", vm.OutputText);

        changed.Clear();
        vm.NextCommand.Execute(null); // 300 × 300 with 120 px of padding: 540 × 540, the 16:9 canvas that holds it
        Assert.Contains(nameof(BackgroundToolViewModel.OutputText), changed);
        Assert.Equal("결과 960 × 540px", vm.OutputText);
        vm.UndoCommand.Execute(null); // the padding back to 0, on this file: the 16:9 canvas that holds 300 × 300
        Assert.Equal("결과 544 × 306px", vm.OutputText);
    }

    [AvaloniaFact]
    public void The_colour_box_says_what_to_type_and_its_swatch_shows_only_for_a_whole_code()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        Assert.False(vm.HasCustomColor);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.CustomColorHex = "ff88";
        Assert.False(vm.HasCustomColor); // half a code: no empty pill that looks like a white swatch
        vm.CustomColorHex = "ff8800";
        Assert.True(vm.HasCustomColor);
        Assert.Contains(nameof(BackgroundToolViewModel.HasCustomColor), changed);
        vm.CustomColorHex = "";
        Assert.False(vm.HasCustomColor);
        vm.Dispose();

        var (window, shown) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(400, 200));
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "사용자 색");
        Assert.Equal("예: #FF7A1A", box.PlaceholderText);
        Assert.Equal("색 코드를 넣으면 바로 바뀌어요", ToolTip.GetTip(box));
        window.Close();
        shown.Dispose();
    }

    [AvaloniaFact]
    public void Save_returns_a_shared_background_edit_and_remembers_it_without_the_image_path()
    {
        var store = TestStore();
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(200, 400)], store, Loc("ko"));
        var backdrop = ImageToolWindowTests.TestImages.Png(300, 300, "backdrop.png");
        UseImage(vm, backdrop);
        vm.Padding = 20;
        vm.Corner = 10;
        vm.Shadow = 80;
        vm.Aspect = vm.Aspects.Single(a => a.Value == "4:5");
        vm.NextCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        // One edit for every file, with the picture itself (not the smaller copy the preview drew).
        Assert.Null(vm.Result!.PerFile);
        Assert.Null(vm.Result.Shared!.Crop);
        var edit = vm.Result.Shared.Background!;
        Assert.Equal((BackgroundKind.Image, backdrop, .2, .1, 80, "4:5"), (edit.Kind, edit.ImagePath, edit.Padding, edit.Corner, edit.Shadow, edit.Aspect));

        // Remembered, but not the path: a picture of the user's may be gone the next time. The last gradient stays.
        foreach (var kept in new[] { store.Settings.ImageTools.Background, TestStoreAt(store.Directory).Settings.ImageTools.Background })
        {
            Assert.Null(kept.ImagePath);
            Assert.Equal((BackgroundKind.Gradient, "peach", .2, .1, 80, "4:5"), (kept.Kind, kept.Value, kept.Padding, kept.Corner, kept.Shadow, kept.Aspect));
        }
        Assert.DoesNotContain("backdrop.png", File.ReadAllText(Path.Combine(store.Directory, "settings.json")));
        Assert.Equal(backdrop, edit.ImagePath); // the result was not changed by remembering it

        // A colour is what the next window starts with.
        var next = Background([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));
        next.CustomColorHex = "#ff8800";
        next.SaveCommand.Execute(null);
        Assert.Equal((BackgroundKind.Solid, "#FF8800"), (next.Result!.Shared!.Background!.Kind, next.Result.Shared.Background.Value));
        var again = Background([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));
        Assert.Equal("#FF8800", again.CustomColorHex);
        Assert.True(again.IsCustomColor);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Undo_restores_the_previous_padding()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        vm.Padding = 25;
        vm.Aspect = vm.Aspects.Single(a => a.Value == "1:1");
        vm.UndoCommand.Execute(null);
        Assert.Equal("auto", vm.Aspect.Value);
        Assert.Equal(25.0, vm.Padding);
        vm.UndoCommand.Execute(null);
        Assert.Equal(8.0, vm.Padding);
        Assert.Equal(.08, vm.PreviewEdit.Background!.Padding); // the preview follows
        vm.RedoCommand.Execute(null);
        Assert.Equal(25.0, vm.Padding);
        vm.CancelCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [AvaloniaFact]
    public void One_slider_drag_is_one_undo_step_and_keyboard_nudges_are_one_each()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));

        vm.BeginAdjust();
        vm.Padding = 10;
        vm.Padding = 14;
        vm.Padding = 21;
        vm.EndAdjust();
        vm.UndoCommand.Execute(null);
        Assert.Equal(8.0, vm.Padding); // all of the drag at once
        vm.UndoCommand.Execute(null);  // nothing before it
        Assert.Equal(8.0, vm.Padding);

        // Pressed and let go without moving: nothing to undo (the earlier step is not eaten by an empty one).
        vm.Corner = 5;
        vm.BeginAdjust();
        vm.EndAdjust();
        vm.UndoCommand.Execute(null);
        Assert.Equal(3.0, vm.Corner);

        // Arrow keys on a slider that nobody drags: a step each.
        vm.Shadow = 41;
        vm.Shadow = 42;
        vm.UndoCommand.Execute(null);
        Assert.Equal(41.0, vm.Shadow);
        vm.UndoCommand.Execute(null);
        Assert.Equal(40.0, vm.Shadow);
    }

    [AvaloniaFact]
    public void Reset_goes_back_to_the_defaults_as_one_undo_step()
    {
        var store = TestStore();
        store.Settings.ImageTools.Background = new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#000000", Padding = .3, Aspect = "9:16" };
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc("ko"));
        vm.ResetCommand.Execute(null);
        Assert.Equal("peach", vm.SelectedSwatch!.Id);
        Assert.Equal((8.0, 3.0, 40.0, "auto"), (vm.Padding, vm.Corner, vm.Shadow, vm.Aspect.Value));
        vm.UndoCommand.Execute(null);
        Assert.Equal((30.0, "9:16"), (vm.Padding, vm.Aspect.Value));
        Assert.Equal("solid-black", vm.SelectedSwatch!.Id); // #000000 is the black chip
    }

    [AvaloniaFact]
    public void A_typed_colour_is_taken_once_it_is_a_whole_hex_code()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        foreach (var partial in new[] { "#", "#12", "#1234", "#12345", "red", "#GG0000" })
        {
            vm.CustomColorHex = partial;
            Assert.Equal("peach", vm.SelectedSwatch!.Id); // typing half a code changes nothing
        }
        vm.CustomColorHex = "#ff8800";
        Assert.True(vm.IsCustomColor);
        Assert.Equal((BackgroundKind.Solid, "#FF8800"), (vm.PreviewEdit.Background!.Kind, vm.PreviewEdit.Background.Value));

        vm.CustomColorHex = "#FFFFFF"; // a chip's colour is that chip
        Assert.Equal("solid-white", vm.SelectedSwatch!.Id);
        Assert.False(vm.IsCustomColor);

        vm.UndoCommand.Execute(null);
        Assert.True(vm.IsCustomColor);
        vm.UndoCommand.Execute(null);
        Assert.Equal("peach", vm.SelectedSwatch!.Id);
    }

    [AvaloniaFact]
    public async Task A_large_backdrop_picture_is_prepared_off_the_UI_thread_and_previewed_small()
    {
        var big = Jpeg(6000, 4000, MagickColors.SteelBlue);
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));

        var task = vm.UseImageAsync(big);
        Assert.False(task.IsCompleted); // the picture is being read on another thread; this one is free
        Assert.Equal(BackgroundKind.Gradient, vm.PreviewEdit.Background!.Kind); // the backdrop shown stays until the new one is ready
        await task;

        var shown = vm.PreviewEdit.Background;
        Assert.Equal(BackgroundKind.Image, shown.Kind);
        Assert.True(vm.IsCustomImage);
        Assert.Equal("backdrop.jpg", vm.CustomImageName);
        Assert.NotEqual(big, shown.ImagePath); // the preview draws a smaller copy of it
        var info = new MagickImageInfo(shown.ImagePath!);
        Assert.Equal((2048u, 1365u), (info.Width, info.Height));

        // That copy is what the renderer draws behind the picture, here a see-through corner of the result.
        using var picture = SKImage.FromBitmap(new SKBitmap(100, 100));
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        ImageEditRenderer.Draw(surface.Canvas, picture, SKRect.Create(200, 200), vm.PreviewEdit, 0);
        using var snapshot = surface.Snapshot();
        using var pixels = snapshot.PeekPixels();
        var corner = pixels.GetPixelColor(2, 2);
        Assert.InRange(corner.Red, 0x46 - 4, 0x46 + 4); // steel blue, not the white of a backdrop that could not be read
        Assert.InRange(corner.Blue, 0xB4 - 4, 0xB4 + 4);

        // Saving gives the picture itself, full size.
        vm.SaveCommand.Execute(null);
        Assert.Equal(big, vm.Result!.Shared!.Background!.ImagePath);

        vm.Dispose();
        Assert.False(File.Exists(shown.ImagePath)); // the copy is cleared away with the window
        Assert.True(File.Exists(big));
    }

    [AvaloniaFact]
    public async Task A_small_backdrop_picture_is_used_as_it_is_and_a_file_that_is_not_a_picture_changes_nothing()
    {
        var vm = Background([ImageToolWindowTests.TestImages.Png(400, 200)], TestStore(), Loc("ko"));
        var small = ImageToolWindowTests.TestImages.Png(300, 200, "small.png");
        await vm.UseImageAsync(small);
        Assert.Equal(small, vm.PreviewEdit.Background!.ImagePath);

        var text = Path.Combine(Path.GetDirectoryName(small)!, "notes.png");
        File.WriteAllText(text, "this is not a picture");
        await vm.UseImageAsync(text);
        Assert.Equal(small, vm.PreviewEdit.Background.ImagePath);
        Assert.Equal("small.png", vm.CustomImageName);

        vm.UndoCommand.Execute(null); // the one step was the first picture
        Assert.False(vm.IsCustomImage);
        Assert.Equal("peach", vm.SelectedSwatch!.Id);
    }

    /// <summary>Picks a picture as the panel's button does, and waits for it to be prepared.</summary>
    private static void UseImage(BackgroundToolViewModel vm, string path)
    {
        var task = vm.UseImageAsync(path);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(task.IsCompletedSuccessfully);
    }

    // ───────── The window ─────────

    private static (ImageToolWindow Window, BackgroundToolViewModel Vm) Open(UserDataStore store, params string[] files)
    {
        var vm = Background(files, store, Loc("ko"));
        // Tall enough for the whole panel, so every control of it is on the screen to be clicked.
        var window = new ImageToolWindow { DataContext = vm, Width = 900, Height = 760 };
        window.Show();
        WaitForPreview(vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (window, vm);
    }

    [AvaloniaFact]
    public void The_window_shows_the_panel_and_leaves_a_screenshot()
    {
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(1000, 700));
        Assert.IsType<BackgroundPanel>(window.FindControl<ContentControl>("ToolPanel")!.Content);
        Assert.Equal("배경 넣기 · photo.png", window.Title);
        Assert.Single(window.GetVisualDescendants().OfType<PictureView>());

        var chips = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(vm.Swatches.Count + vm.Aspects.Count, chips.Count);
        Assert.Equal(3, window.GetVisualDescendants().OfType<Slider>().Count());
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "여백");

        using var frame = window.CaptureRenderedFrame()!;
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "tool-background-ko.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Clicking_a_chip_picks_it_and_dragging_a_slider_is_one_undo_step()
    {
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(400, 200));
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

        var ocean = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is BackgroundSwatch { Id: "ocean" });
        window.MouseDown(Centre(ocean), MouseButton.Left);
        window.MouseUp(Centre(ocean), MouseButton.Left);
        Assert.Equal("ocean", vm.SelectedSwatch!.Id);
        Assert.Equal("ocean", vm.PreviewEdit.Background!.Value);

        var ratio = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is Choice<string> { Value: "16:9" });
        window.MouseDown(Centre(ratio), MouseButton.Left);
        window.MouseUp(Centre(ratio), MouseButton.Left);
        Assert.Equal("16:9", vm.Aspect.Value);

        var slider = window.GetVisualDescendants().OfType<Slider>().First();
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var start = Centre(thumb);
        window.MouseDown(start, MouseButton.Left);
        for (var i = 1; i <= 6; i++)
            window.MouseMove(start + new Point(i * 10, 0));
        window.MouseUp(start + new Point(60, 0), MouseButton.Left);
        Assert.True(vm.Padding > 8, $"the thumb moved the padding to {vm.Padding}");

        vm.UndoCommand.Execute(null); // the whole drag
        Assert.Equal(8.0, vm.Padding);
        Assert.Equal("16:9", vm.Aspect.Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal("auto", vm.Aspect.Value);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void After_a_slider_is_used_with_the_mouse_the_arrow_keys_switch_files_again()
    {
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(200, 400));
        Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        var sliders = window.GetVisualDescendants().OfType<Slider>().ToList();

        // A drag on the thumb of the padding slider. The slider must not keep the keys: they are for the files (spec §3).
        var start = Centre(sliders[0].GetVisualDescendants().OfType<Thumb>().Single());
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(30, 0));
        window.MouseUp(start + new Point(30, 0), MouseButton.Left);
        var padding = vm.Padding;
        Assert.True(padding > 8);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(1, vm.Index);
        Assert.Equal(padding, vm.Padding); // not nudged

        // A click on the track of another slider, away from its thumb, and then a drag from where it went.
        var track = sliders[1].Bounds;
        var onTrack = sliders[1].TranslatePoint(new Point(track.Width * .7, track.Height / 2), window)!.Value;
        window.MouseDown(onTrack, MouseButton.Left);
        window.MouseUp(onTrack, MouseButton.Left);
        var corner = vm.Corner;
        Assert.True(corner > 3);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(0, vm.Index);
        Assert.Equal(corner, vm.Corner);

        // Someone who tabs to a slider does want the arrows there.
        Assert.True(sliders[2].Focus());
        var shadow = vm.Shadow;
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(0, vm.Index);
        Assert.NotEqual(shadow, vm.Shadow);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Pressing_on_the_track_and_dragging_is_one_undo_step()
    {
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(400, 200));
        var slider = window.GetVisualDescendants().OfType<Slider>().First();
        var width = slider.Bounds.Width;
        Point At(double x) => slider.TranslatePoint(new Point(x, slider.Bounds.Height / 2), window)!.Value;

        window.MouseDown(At(width * .5), MouseButton.Left); // away from the thumb: the value jumps there
        for (var i = 1; i <= 5; i++)
            window.MouseMove(At(width * .5 + i * 8));
        window.MouseUp(At(width * .5 + 40), MouseButton.Left);
        Assert.True(vm.Padding > 20, $"the padding went to {vm.Padding}");

        vm.UndoCommand.Execute(null);
        Assert.Equal(8.0, vm.Padding); // the jump and the drag after it together
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void A_lost_pointer_capture_ends_a_slider_drag()
    {
        var (window, vm) = Open(TestStore(), ImageToolWindowTests.TestImages.Png(400, 200));
        IPointer? pointer = null;
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        var thumb = window.GetVisualDescendants().OfType<Slider>().First().GetVisualDescendants().OfType<Thumb>().Single();
        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(20, 0));
        var dragged = vm.Padding;
        Assert.True(dragged > 8);
        pointer!.Capture(null); // taken away without a release: an Alt+Tab, a system dialog, a cancelled touch

        // The next change is a step of its own, as it is when no drag was ever begun.
        vm.Corner = 9;
        vm.UndoCommand.Execute(null);
        Assert.Equal(3.0, vm.Corner);
        Assert.Equal(dragged, vm.Padding); // only the corner went back: the drag's own step was not what this undid
        window.MouseUp(start + new Point(20, 0), MouseButton.Left);
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void The_preview_has_the_shape_of_the_result_and_actual_size_shows_it_pixel_for_pixel()
    {
        // A 400 × 200 picture with 8 % (16 px) around it, on a black square: 432 × 432 pixels once saved.
        var store = TestStore();
        store.Settings.ImageTools.Background = new BackgroundEdit
        {
            Kind = BackgroundKind.Solid,
            Value = "#000000",
            Aspect = "1:1",
            Corner = 0,
            Shadow = 0,
        };
        var (window, vm) = Open(store, ImageToolWindowTests.TestImages.Png(400, 200));
        var canvas = window.GetVisualDescendants().OfType<PictureView>().Single();

        var (fitted, _) = BlackBox(window, canvas);
        Assert.Equal(fitted.Width, fitted.Height, 3.0); // square, not the picture's 2 : 1
        Assert.True(fitted.Width > 300, $"a square {fitted.Width} wide");

        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control); // Ctrl+1
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var (actual, _) = BlackBox(window, canvas);
        Assert.Equal(432.0, actual.Width, 3.0); // one pixel of the result on one pixel of the screen
        Assert.Equal(432.0, actual.Height, 3.0);
        window.Close();
        vm.Dispose();
    }

    /// <summary>The bounding box (window pixels) of the pure black pixels inside <paramref name="canvas"/>.</summary>
    private static (Rect Box, int Count) BlackBox(Window window, Control canvas)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var origin = canvas.TranslatePoint(default, window)!.Value;
        var (left, top, right, bottom, count) = (int.MaxValue, int.MaxValue, -1, -1, 0);
        var pixel = new byte[4];
        for (var y = (int)origin.Y; y < (int)(origin.Y + canvas.Bounds.Height); y++)
        {
            for (var x = (int)origin.X; x < (int)(origin.X + canvas.Bounds.Width); x++)
            {
                Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, pixel, 0, 4);
                if (pixel[0] != 0 || pixel[1] != 0 || pixel[2] != 0)
                    continue;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                count++;
            }
        }
        return (new Rect(left, top, right - left + 1, bottom - top + 1), count);
    }
}
