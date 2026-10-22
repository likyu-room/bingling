// The image tool window's shell, shared by its tools: which view model a tool gets, the preview it reads (one decode,
// as a bitmap and as a Skia picture), and the preview drawn with the renderer that saves the picture.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Tools;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using SkiaSharp;

namespace Filee.App.Tests;

public class ImageToolShellTests
{
    private static UserDataStore TestStore() =>
        new(Path.Combine(Path.GetTempPath(), "filee-app-tests", "shell-store-" + Guid.NewGuid().ToString("N")[..8]));

    private static ILocalizer Loc()
    {
        TestServices.EnsureInitialized("ko");
        return AppHost.Get<ILocalizer>();
    }

    [AvaloniaFact]
    public void Create_returns_the_crop_view_model_for_crop()
    {
        string[] files = [ImageToolWindowTests.TestImages.Png(400, 200)];
        using var crop = ImageToolViewModel.Create(ImageToolKind.Crop, files, TestStore(), Loc());
        Assert.IsType<CropToolViewModel>(crop);
        Assert.Equal(ImageToolKind.Crop, crop.Tool);
        Assert.Equal(files, crop.Files);

        using var background = ImageToolViewModel.Create(ImageToolKind.AddBackground, files, TestStore(), Loc());
        Assert.IsType<BackgroundToolViewModel>(background);
        using var redact = ImageToolViewModel.Create(ImageToolKind.Redact, files, TestStore(), Loc());
        Assert.IsType<RedactToolViewModel>(redact);
        using var annotate = ImageToolViewModel.Create(ImageToolKind.Annotate, files, TestStore(), Loc());
        Assert.IsType<AnnotateToolViewModel>(annotate);
    }

    [AvaloniaFact]
    public void The_preview_picture_comes_with_the_bitmap_and_goes_with_it()
    {
        using var vm = ImageToolViewModel.Create(ImageToolKind.Crop,
            [ImageToolWindowTests.TestImages.Png(3000, 1000), ImageToolWindowTests.TestImages.Png(300, 500)], TestStore(), Loc())!;
        Assert.Equal(1000, vm.FullShortSide); // of the file, not of its smaller preview
        WaitForPreview(vm);
        var first = vm.PreviewImage!;
        Assert.Equal((2048, 683), (first.Width, first.Height));
        Assert.Equal(new PixelSize(first.Width, first.Height), vm.Preview!.PixelSize);

        vm.NextCommand.Execute(null);
        Assert.Equal(IntPtr.Zero, first.Handle); // let go of with the other file's bitmap
        Assert.Equal(300, vm.FullShortSide);
        WaitForPreview(vm);
        var second = vm.PreviewImage!;
        Assert.Equal((300, 500), (second.Width, second.Height));
        using (var pixels = second.PeekPixels())
            Assert.Equal(new SKColor(0x46, 0x82, 0xB4), pixels.GetPixelColor(0, 0)); // the gradient's top: its own pixels, kept

        vm.Dispose();
        Assert.Null(vm.PreviewImage);
        Assert.Equal(IntPtr.Zero, second.Handle);
    }

    [AvaloniaFact]
    public void The_window_says_while_the_picture_is_read_and_when_it_cannot_be()
    {
        var folder = Path.Combine(Path.GetTempPath(), "filee-app-tests", "shell-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var broken = Path.Combine(folder, "broken.png");
        File.WriteAllText(broken, "this is not a picture");
        using var vm = ImageToolViewModel.Create(ImageToolKind.Redact, [broken, ImageToolWindowTests.TestImages.Png(300, 500)], TestStore(), Loc())!;
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal("불러오는 중…", vm.PreviewStatus); // the read has not come back yet
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.PreviewStatus == "불러오는 중…" && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.Equal("미리 볼 수 없는 그림이에요", vm.PreviewStatus); // not a grey blank for ever
        Assert.Null(vm.Preview);
        Assert.Contains(nameof(vm.PreviewStatus), changes);

        vm.NextCommand.Execute(null); // another file is read: the mark of the one before goes
        Assert.Equal("불러오는 중…", vm.PreviewStatus);
        WaitForPreview(vm);
        Assert.Equal("", vm.PreviewStatus); // the picture is the answer
        Assert.Contains(nameof(vm.PreviewStatus), changes);

        vm.PreviousCommand.Execute(null);
        Assert.Equal("불러오는 중…", vm.PreviewStatus);
    }

    [AvaloniaFact]
    public void The_status_is_drawn_in_the_middle_of_the_window_until_there_is_a_picture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "filee-app-tests", "shell-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var broken = Path.Combine(folder, "broken.png");
        File.WriteAllText(broken, "this is not a picture");
        var vm = ImageToolViewModel.Create(ImageToolKind.Annotate, [broken], TestStore(), Loc())!;
        var window = new Filee.App.Views.ImageToolWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.PreviewStatus == "불러오는 중…" && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "미리 볼 수 없는 그림이에요");
        Assert.True(text.IsEffectivelyVisible);
        var picture = window.GetVisualDescendants().OfType<PictureView>().Single();
        var (centre, middle) = (text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), window)!.Value,
            picture.TranslatePoint(new Point(picture.Bounds.Width / 2, picture.Bounds.Height / 2), window)!.Value);
        Assert.True(Math.Abs(centre.X - middle.X) < 3 && Math.Abs(centre.Y - middle.Y) < 3, $"{centre} against {middle}");
        using (var frame = window.CaptureRenderedFrame()!)
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "tool-unreadable-ko.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Draw_operation_renders_the_edit_into_the_canvas()
    {
        Loc();
        using var picture = Solid(200, 100, SKColors.White);
        var view = new PictureView
        {
            Picture = picture,
            Edit = new ImageEdit { Redact = new RedactEdit { Boxes = [new EditRect { X = .25, Y = .25, Width = .5, Height = .5 }] } },
        };
        var window = new Window { Content = view, Width = 400 + 2 * PictureView.Inset, Height = 200 + 2 * PictureView.Inset, Background = Brushes.Gray };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame()!;
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "tool-shell-preview.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());
        var image = PictureView.ImageRect(view.Bounds.Size, new Size(200, 100), 1, default);
        Assert.Equal(new Rect(PictureView.Inset, PictureView.Inset, 400, 200), image); // twice the picture's size
        Point At(double x, double y) => view.TranslatePoint(new Point(image.X + x * image.Width, image.Y + y * image.Height), window)!.Value;

        Assert.Equal(Colors.Black, PixelAt(frame, At(.5, .5)));  // the box, hidden
        Assert.Equal(Colors.Black, PixelAt(frame, At(.27, .27)));
        Assert.Equal(Colors.White, PixelAt(frame, At(.1, .5)));  // the picture beside it
        Assert.Equal(Colors.White, PixelAt(frame, At(.5, .1)));
        Assert.Equal(Colors.Gray, PixelAt(frame, new Point(4, 4))); // the window around it
        window.Close();
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_file_shown_with_its_edit_on_the_clipboard_and_closes_without_saving()
    {
        string[] files = [ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(300, 500)];
        using var vm = ImageToolViewModel.Create(ImageToolKind.Crop, files, TestStore(), Loc())!;
        vm.CopiedHold = TimeSpan.Zero;
        var copies = new List<(string Path, ImageEdit Edit)>();
        vm.CopyPicture = (path, edit) =>
        {
            copies.Add((path, edit));
            return Task.FromResult(true);
        };
        var finished = 0;
        vm.Finished += (_, _) => finished++;
        Assert.Equal("복사", vm.CopyText);
        vm.NextCommand.Execute(null);

        await vm.CopyCommand.ExecuteAsync(null);

        var (path, edit) = Assert.Single(copies);
        Assert.Equal(files[1], path); // the file shown, not the first
        Assert.NotNull(edit.Crop);
        Assert.Equal(1, finished);
        Assert.Null(vm.Result); // nothing to run: no file is written
        Assert.Equal("복사됨", vm.CopyText);
    }

    [AvaloniaFact]
    public async Task Copy_leaves_the_window_open_when_that_is_turned_off_or_the_copy_failed()
    {
        var store = TestStore();
        using var vm = ImageToolViewModel.Create(ImageToolKind.Crop, [ImageToolWindowTests.TestImages.Png(400, 200)], store, Loc())!;
        vm.CopiedHold = TimeSpan.FromMinutes(1);
        var copied = false;
        vm.CopyPicture = (_, _) => Task.FromResult(copied);
        var finished = 0;
        vm.Finished += (_, _) => finished++;

        await vm.CopyCommand.ExecuteAsync(null); // the clipboard was busy
        Assert.Equal((0, "복사"), (finished, vm.CopyText));

        store.Settings.ImageTools.CloseAfterCopy = false;
        copied = true;
        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal((0, "복사됨"), (finished, vm.CopyText)); // "Copy" again a moment later
        vm.SaveCommand.Execute(null); // and a file can still be saved
        Assert.Equal(1, finished);
        Assert.NotNull(vm.Result);
    }

    [AvaloniaFact]
    public async Task Annotate_copies_the_drawings_of_the_file_shown_or_the_file_as_it_is()
    {
        string[] files = [ImageToolWindowTests.TestImages.Png(400, 200), ImageToolWindowTests.TestImages.Png(400, 200)];
        var store = TestStore();
        store.Settings.ImageTools.CloseAfterCopy = false; // both files are copied in turn
        using var vm = (AnnotateToolViewModel)ImageToolViewModel.Create(ImageToolKind.Annotate, files, store, Loc())!;
        vm.CopiedHold = TimeSpan.FromMinutes(1);
        var copies = new List<ImageEdit>();
        vm.CopyPicture = (_, edit) =>
        {
            copies.Add(edit);
            return Task.FromResult(true);
        };
        vm.Annotations = [new Annotation { Kind = AnnotationKind.Number, Number = 1, Points = [new EditPoint { X = .5, Y = .5 }] }];

        await vm.CopyCommand.ExecuteAsync(null);
        vm.NextCommand.Execute(null);
        await vm.CopyCommand.ExecuteAsync(null); // the second file has no drawings: it goes as the window shows it

        Assert.Equal(1, copies[0].Annotations!.Single().Number);
        Assert.False(copies[1].Draws);
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

    private static SKImage Solid(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>The frame's colour at <paramref name="p"/> (window DIPs; the headless screen is at 100 %).</summary>
    private static Color PixelAt(WriteableBitmap frame, Point p)
    {
        using var buffer = frame.Lock();
        var pixel = new byte[4];
        Marshal.Copy(buffer.Address + (int)p.Y * buffer.RowBytes + (int)p.X * 4, pixel, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(pixel[3], pixel[0], pixel[1], pixel[2])
            : Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
