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

        // A tool whose window is not built yet gets none, rather than the crop window under its name.
        Assert.Null(ImageToolViewModel.Create(ImageToolKind.Annotate, files, TestStore(), Loc()));

        using var background = ImageToolViewModel.Create(ImageToolKind.AddBackground, files, TestStore(), Loc());
        Assert.IsType<BackgroundToolViewModel>(background);
        using var redact = ImageToolViewModel.Create(ImageToolKind.Redact, files, TestStore(), Loc());
        Assert.IsType<RedactToolViewModel>(redact);
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
