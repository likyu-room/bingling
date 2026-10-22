// The capture window (Ctrl+Alt+S): the screen the cursor is on, frozen and dimmed; a drag picks an area (its size in the
// screen's pixels), which is then edited, copied or saved. The screen is ColorPickTests' fake, whose every pixel has its
// own colour, so a test can tell which pixels were taken; the save folder is a fresh one of the test's own.

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Presets;
using ImageMagick;
using Microsoft.Extensions.Logging;
using static Filee.App.Tests.ColorPickTests;

namespace Filee.App.Tests;

public class CaptureWindowTests
{
    /// <summary>A save folder that does not exist yet.</summary>
    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "filee-app-tests", "captures-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>The screen tools on the fake screen, saving into a folder of their own; the annotate window answers <paramref name="edit"/>.</summary>
    private static (ScreenTools Tools, CaptureOutput Output, FakeEditWindows Windows) Start(string language, Func<string, ToolResult?>? edit = null)
    {
        TestServices.EnsureInitialized(language);
        var windows = new FakeEditWindows(edit ?? (_ => null));
        var output = new CaptureOutput(AppHost.Get<ConversionService>(), windows, AppHost.Get<ILocalizer>(), AppHost.Get<ILogger<CaptureOutput>>())
        {
            Folder = NewFolder(),
        };
        return (new ScreenTools(new FakeScreen(), new ToolsLog(), output), output, windows);
    }

    private static CaptureWindow Open(ScreenTools tools)
    {
        tools.StartCapture(new PixelPoint(100, 100));
        var window = Assert.IsType<CaptureWindow>(tools.Capture);
        Assert.True(window.IsVisible);
        return window;
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
    }

    /// <summary>Runs the UI thread until <paramref name="done"/>, for up to <paramref name="seconds"/> (a conversion takes a while).</summary>
    private static async Task Until(Func<bool> done, double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }

    private static async Task<Bitmap?> ClipboardPicture()
    {
        var top = new Window();
        top.Show();
        try
        {
            return await top.Clipboard!.TryGetBitmapAsync();
        }
        finally
        {
            top.Close();
        }
    }

    private static async Task<string?> ClipboardText()
    {
        var top = new Window();
        top.Show();
        try
        {
            return await top.Clipboard!.TryGetTextAsync();
        }
        finally
        {
            top.Close();
        }
    }

    /// <summary>The folders the capture made for its temporary files, and are still there.</summary>
    private static string[] TempFolders() =>
        Directory.Exists(CaptureOutput.TempRoot) ? Directory.GetDirectories(CaptureOutput.TempRoot) : [];

    [AvaloniaFact]
    public void A_drag_picks_an_area_and_says_its_size_in_the_screens_pixels()
    {
        var (tools, _, _) = Start("ko");
        var window = Open(tools);
        var vm = window.ViewModel;
        Assert.True(vm.ShowsHint);
        Assert.False(vm.ShowsActions);

        // Both pixels the drag went from and to are in the area.
        Drag(window, new Point(100, 200), new Point(1299, 874));

        Assert.Equal(new PixelRect(100, 200, 1200, 675), vm.Selection);
        Assert.Equal("1200 × 675", vm.SizeText);
        Assert.True(vm.ShowsActions);
        Assert.False(vm.ShowsHint);
        Assert.Equal((100, 200, 1200, 675), (Canvas.GetLeft(window.Frame), Canvas.GetTop(window.Frame), window.Frame.Width, window.Frame.Height));
        Assert.Equal((100d, 176d), (Canvas.GetLeft(window.SizeTag), Canvas.GetTop(window.SizeTag))); // above the area's corner
        Assert.Equal((100d, 883d), (Canvas.GetLeft(window.Actions), Canvas.GetTop(window.Actions))); // below it
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!)
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "capture-area-ko.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());

        // Too small (under 8 pixels across): a click, not an area.
        Drag(window, new Point(500, 500), new Point(505, 700));
        Assert.Null(vm.Selection);
        Assert.False(vm.ShowsActions);
        window.Close();
    }

    [AvaloniaFact]
    public void The_buttons_wait_for_the_release_and_only_a_move_with_the_button_down_drags()
    {
        var (tools, _, _) = Start("en");
        var window = Open(tools);
        var vm = window.ViewModel;

        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseMove(new Point(300, 200), RawInputModifiers.LeftMouseButton);
        Assert.Equal(new PixelRect(100, 100, 201, 101), vm.Selection);
        Assert.False(window.Actions.IsVisible); // not while dragging
        window.MouseUp(new Point(300, 200), MouseButton.Left);
        Assert.True(window.Actions.IsVisible);

        window.MouseMove(new Point(500, 400)); // no button down: no drag
        Assert.Equal(new PixelRect(100, 100, 201, 101), vm.Selection);

        // A release that never came (it went to another window): the next move without the button ends the drag where it was.
        window.MouseDown(new Point(600, 600), MouseButton.Left);
        window.MouseMove(new Point(700, 650), RawInputModifiers.LeftMouseButton);
        Assert.False(window.Actions.IsVisible);
        window.MouseMove(new Point(900, 900));
        Assert.Equal(new PixelRect(600, 600, 101, 51), vm.Selection);
        Assert.True(window.Actions.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_buttons_go_below_the_area_above_it_where_the_screen_ends_and_inside_it_when_it_fills_the_screen()
    {
        var screen = new Size(1920, 1080);
        var buttons = new Size(200, 36);

        Assert.Equal(new Point(100, 308), CaptureWindow.PlaceActions(new Rect(100, 100, 300, 200), buttons, screen));
        Assert.Equal(new Point(100, 856), CaptureWindow.PlaceActions(new Rect(100, 900, 300, 170), buttons, screen));
        Assert.Equal(new Point(0, 1036), CaptureWindow.PlaceActions(new Rect(0, 0, 1920, 1080), buttons, screen));
        Assert.Equal(new Point(1720, 208), CaptureWindow.PlaceActions(new Rect(1800, 100, 100, 100), buttons, screen)); // on the screen
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_area_on_the_clipboard_says_so_and_closes()
    {
        var (tools, output, _) = Start("ko");
        var window = Open(tools);
        var vm = window.ViewModel;
        Drag(window, new Point(30, 40), new Point(129, 89));

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        await Until(() => vm.SaysCopied, 2);
        Assert.True(window.IsVisible); // "Copied" for a moment
        Assert.False(vm.ShowsActions);
        await Until(() => !window.IsVisible, 2);

        Assert.Null(tools.Capture);
        using var picture = await ClipboardPicture();
        Assert.NotNull(picture);
        Assert.Equal(new PixelSize(100, 50), picture.PixelSize);
        Assert.Equal(ScreenColor(30, 40), Pixel(picture, new Point(0, 0)));
        Assert.Equal(ScreenColor(129, 89), Pixel(picture, new Point(99, 49)));
        Assert.False(Directory.Exists(output.Folder)); // nothing saved
    }

    [AvaloniaFact]
    public async Task Save_writes_the_area_as_a_png_in_the_capture_folder_and_shows_its_card()
    {
        var (tools, output, _) = Start("ko");
        var conversions = AppHost.Get<ConversionService>();
        var window = Open(tools);
        Drag(window, new Point(30, 40), new Point(129, 89));
        Assert.False(Directory.Exists(output.Folder));
        var jobs = conversions.ToastJobs.Count;

        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);

        Assert.False(window.IsVisible); // closed at once; the job saves it, once the picture is a file
        await Until(() => conversions.ToastJobs.Count > jobs, 10);
        var job = conversions.ToastJobs[0];
        var temp = job.Job.Sources[0];
        Assert.StartsWith(CaptureOutput.TempRoot, temp);
        await Until(() => !job.IsRunning && !File.Exists(temp), 30);

        var saved = Assert.Single(Directory.GetFiles(output.Folder)); // the folder is made for it
        Assert.Matches(@"^캡처 \d{4}-\d\d-\d\d \d{6}\.png$", Path.GetFileName(saved));
        Assert.Equal(Path.GetFileName(temp), Path.GetFileName(saved));
        Assert.Equal([saved], job.Job.Outputs);
        Assert.True(job.HasPictureResult); // the card shows the picture, to copy or open
        using (var image = new MagickImage(saved))
            Assert.Equal((100u, 50u), (image.Width, image.Height));
        Assert.False(Directory.Exists(Path.GetDirectoryName(temp)));
        job.DismissCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task A_save_that_fails_keeps_the_capture_for_its_retry()
    {
        var (_, output, _) = Start("en");
        var conversions = AppHost.Get<ConversionService>();
        // A file where the save folder would be: the job cannot make the folder, and fails.
        Directory.CreateDirectory(Path.GetDirectoryName(output.Folder)!);
        File.WriteAllText(output.Folder, "in the way");
        using var shot = ScreenGrab.Grab(new FakeScreen(), new PixelRect(0, 0, 100, 50), 1)!;

        await output.SaveAsync(shot.Crop(new PixelRect(0, 0, 100, 50)));

        var job = conversions.ToastJobs.First(j => j.Job.Sources[0].StartsWith(CaptureOutput.TempRoot));
        Assert.NotEqual(JobState.Completed, job.Job.State);
        var temp = job.Job.Sources[0];
        Assert.True(File.Exists(temp)); // the only copy of the capture
        Assert.True(job.CanRetry);

        File.Delete(output.Folder);
        job.RetryCommand.Execute(null);
        var again = conversions.ToastJobs[0];
        Assert.Equal([temp], again.Job.Sources);
        await conversions.FinishedAsync(again);
        Assert.Equal(JobState.Completed, again.Job.State);
        Assert.Equal([Assert.Single(Directory.GetFiles(output.Folder))], again.Job.Outputs); // the capture, saved after all
        again.DismissCommand.Execute(null);
        Directory.Delete(Path.GetDirectoryName(temp)!, recursive: true);
    }

    [AvaloniaFact]
    public async Task A_capture_that_cannot_be_handed_on_says_so()
    {
        var (_, output, windows) = Start("ko", _ => throw new IOException("disk full"));
        using var shot = ScreenGrab.Grab(new FakeScreen(), new PixelRect(0, 0, 100, 50), 1)!;

        await output.EditAsync(shot.Crop(new PixelRect(0, 0, 100, 50)), new PixelPoint(50, 25));

        Assert.Equal(["캡처를 저장하지 못했어요: disk full"], windows.Messages);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Assert.Single(windows.Opened).Files[0]))); // nothing to retry: it goes
    }

    [AvaloniaFact]
    public void Only_temporary_captures_over_a_day_old_are_cleared_away()
    {
        var root = NewFolder();
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        foreach (var (name, age) in new[] { ("old", TimeSpan.FromHours(25)), ("recent", TimeSpan.FromHours(23)) })
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            File.WriteAllText(Path.Combine(folder, "Capture.png"), "");
            Directory.SetLastWriteTimeUtc(folder, now - age);
        }

        CaptureOutput.DeleteOld(root, now);

        Assert.Equal(["recent"], Directory.GetDirectories(root).Select(Path.GetFileName));
        CaptureOutput.DeleteOld(Path.Combine(root, "none"), now); // no folder: nothing to do, and no error
        Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact]
    public async Task Edit_opens_the_annotate_window_on_the_area_and_saves_what_it_draws_in_the_capture_folder()
    {
        var conversions = AppHost.Get<ConversionService>();
        (int Width, int Height)? opened = null;
        var (tools, output, windows) = Start("ko", temp =>
        {
            using (var image = new MagickImage(temp))
                opened = ((int)image.Width, (int)image.Height);
            return new ToolResult(null, new Dictionary<string, ImageEdit>
            {
                [temp] = new() { Annotations = [new Annotation { Kind = AnnotationKind.Arrow, Points = [new() { X = .1, Y = .1 }, new() { X = .9, Y = .9 }] }] },
            });
        });
        var window = Open(tools);
        Drag(window, new Point(30, 40), new Point(129, 89));

        await window.ViewModel.EditCommand.ExecuteAsync(null);

        Assert.False(window.IsVisible);
        var edit = Assert.Single(windows.Opened);
        Assert.Equal(ImageToolKind.Annotate, edit.Tool);
        var temp = Assert.Single(edit.Files);
        Assert.Matches(@"^캡처 \d{4}-\d\d-\d\d \d{6}\.png$", Path.GetFileName(temp));
        Assert.Equal((100, 50), opened);
        Assert.Equal(new PixelPoint(80, 65), edit.Cursor); // the area's middle
        Assert.Equal("사진\\Bingling 폴더에 저장해요", edit.Footer);
        var job = conversions.ToastJobs[0];
        Assert.Equal([temp], job.Job.Sources);
        Assert.NotNull(job.Job.EditsByFile![temp].Annotations);
        await Until(() => !job.IsRunning && !File.Exists(temp), 30);

        var saved = Assert.Single(Directory.GetFiles(output.Folder));
        Assert.Equal(Path.GetFileName(temp), Path.GetFileName(saved));
        Assert.Equal([saved], job.Job.Outputs);
        job.DismissCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task An_edit_closed_without_saving_saves_nothing_and_leaves_no_temporary_file()
    {
        var (tools, output, windows) = Start("en");
        var window = Open(tools);
        Drag(window, new Point(30, 40), new Point(129, 89));

        await window.ViewModel.EditCommand.ExecuteAsync(null);

        var temp = Assert.Single(Assert.Single(windows.Opened).Files);
        Assert.Matches(@"^Capture \d{4}-\d\d-\d\d \d{6}\.png$", Path.GetFileName(temp));
        Assert.False(File.Exists(temp));
        Assert.False(Directory.Exists(Path.GetDirectoryName(temp)));
        Assert.False(Directory.Exists(output.Folder));
    }

    [AvaloniaFact]
    public async Task Esc_or_a_right_click_closes_it_and_leaves_nothing_behind()
    {
        var (tools, output, windows) = Start("ko");
        var temps = TempFolders().Length;
        var window = Open(tools);
        await window.Clipboard!.SetTextAsync("what was there before");
        var vm = window.ViewModel;
        Drag(window, new Point(30, 40), new Point(129, 89));

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
        Assert.Null(tools.Capture);
        Assert.Null(vm.Shot.ColorAt(new PixelPoint(0, 0))); // the screen's picture is let go of
        Assert.Equal("what was there before", await ClipboardText());
        Assert.False(Directory.Exists(output.Folder));
        Assert.Empty(windows.Opened);
        Assert.Equal(temps, TempFolders().Length);

        window = Open(tools);
        window.MouseDown(new Point(30, 40), MouseButton.Right);
        Assert.True(window.IsVisible); // closed on the release, which would otherwise reach the app below
        window.MouseUp(new Point(30, 40), MouseButton.Right);
        Assert.False(window.IsVisible);
        Assert.Null(tools.Capture);
        Assert.Null(tools.Card);
    }

    [AvaloniaFact]
    public async Task At_200_percent_the_area_is_in_the_screens_pixels()
    {
        var (_, output, _) = Start("en");
        // A 400 × 300 px screen right of the main one, at 200 %: the window over it is 200 × 150 DIPs.
        using var shot = ScreenGrab.Grab(new FakeScreen(), new PixelRect(1920, 0, 400, 300), 2)!;
        var window = new CaptureWindow();
        window.SetRenderScaling(2);
        var vm = new CaptureViewModel(shot, output)
        {
            CopyPicture = picture => ResultClipboard.CopyPictureAsync(window.Clipboard!, picture, null),
        };
        window.Open(vm, new PixelPoint(1920 + 100, 100));
        Assert.Equal((200, 150), ((int)window.Width, (int)window.Height));

        Drag(window, new Point(10, 20), new Point(60, 45));

        Assert.Equal(new PixelRect(20, 40, 101, 51), vm.Selection);
        Assert.Equal("101 × 51", vm.SizeText);
        Assert.Equal((10, 20, 50.5, 25.5), (Canvas.GetLeft(window.Frame), Canvas.GetTop(window.Frame), window.Frame.Width, window.Frame.Height));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Until(() => !window.IsVisible, 2);
        using var picture = await ClipboardPicture();
        Assert.Equal(new PixelSize(101, 51), picture!.PixelSize);
        Assert.Equal(ScreenColor(1920 + 20, 40), Pixel(picture, new Point(0, 0)));
    }

    [AvaloniaFact]
    public async Task Full_screen_takes_all_of_it_and_the_eyedropper_copies_a_colour_and_closes()
    {
        var (tools, _, _) = Start("ko");
        var window = Open(tools);
        var vm = window.ViewModel;

        vm.ModeIndex = (int)CaptureMode.Screen;
        Assert.Equal(new PixelRect(0, 0, 1920, 1280), vm.Selection); // the headless platform's only screen
        Assert.Equal("1920 × 1280", vm.SizeText);
        Assert.True(vm.ShowsActions);

        vm.ModeIndex = (int)CaptureMode.Color;
        Assert.Null(vm.Selection);
        Assert.False(vm.ShowsActions);
        Assert.True(window.Lens.IsVisible);
        window.MouseMove(new Point(30, 40));
        Assert.Equal(new PixelPoint(30, 40), window.Lens.Pixel);
        window.MouseDown(new Point(30, 40), MouseButton.Left);

        Assert.False(window.IsVisible);
        Assert.Null(tools.Capture);
        var card = Assert.IsType<ColorCardWindow>(tools.Card);
        var colors = Assert.IsType<ColorCardViewModel>(card.DataContext);
        await Until(() => colors.SaysHexCopied, 2);
        Assert.Equal("#1E287A", colors.Hex);
        Assert.Equal("#1E287A", await ClipboardText());
        card.Close();
    }

    [AvaloniaFact]
    public void One_screen_tool_at_a_time()
    {
        var (tools, _, _) = Start("en");
        var window = Open(tools);

        tools.StartCapture(new PixelPoint(100, 100));
        tools.StartColorPick(new PixelPoint(100, 100));

        Assert.Same(window, tools.Capture);
        Assert.Null(tools.Picker);
        window.Close();
        Assert.Null(tools.Capture);
    }

    [AvaloniaFact]
    public void A_capture_says_where_the_annotate_window_saves()
    {
        TestServices.EnsureInitialized("ko");
        var png = Path.Combine(Path.GetTempPath(), "filee-app-tests", "capture-note-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        using (var image = new MagickImage(MagickColors.SteelBlue, 8, 8))
            image.Write(png);
        using var vm = ImageToolViewModel.Create(ImageToolKind.Annotate, [png], AppHost.Get<Filee.Core.Settings.UserDataStore>(), AppHost.Get<ILocalizer>())!;
        Assert.Equal("원본은 그대로 두고 옆에 새 파일로 저장해요", vm.FooterText);

        vm.FooterNote = "사진\\Bingling 폴더에 저장해요";

        Assert.Equal("사진\\Bingling 폴더에 저장해요", vm.FooterText);
    }

    /// <summary>A WindowService whose annotate window "opens" at once and answers what the test hands it for the file.</summary>
    private sealed class FakeEditWindows(Func<string, ToolResult?> answer)
        : WindowService(AppHost.Get<ILocalizer>(), AppHost.Get<IServiceProvider>(), AppHost.Get<MemoryTrimmer>())
    {
        public List<(ImageToolKind Tool, string[] Files, PixelPoint Cursor, string? Footer)> Opened { get; } = [];
        public List<string> Messages { get; } = [];

        public override Task MessageAsync(Window? owner, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public override Task<ToolResult?> EditImageAsync(ImageToolKind tool, IReadOnlyList<string> files, PixelPoint cursor, string? footer = null)
        {
            Opened.Add((tool, [.. files], cursor, footer));
            return Task.FromResult(answer(files[0]));
        }
    }
}
