// The toast card's Copy button: what ends up on the clipboard for a result (the picture, a list of files, nothing) and what
// the log says when there was nothing to copy; and the image tool window's Copy, which draws its edit on the picture. The
// window and the "Copied" label are in ToastWindowTests.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Filee.App.Services;
using Filee.Core.Presets;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Filee.App.Tests;

public class ResultClipboardTests
{
    /// <summary>A folder of its own for the files a test writes.</summary>
    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "filee-tests", "clipboard-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Picture(string folder, string name, uint width, uint height, MagickColor? color = null)
    {
        var path = Path.Combine(folder, name);
        using var image = new MagickImage(color ?? MagickColors.SteelBlue, width, height);
        image.Write(path);
        return path;
    }

    /// <summary>A window to borrow its clipboard and storage provider from, as the toast does.</summary>
    private static Window Top()
    {
        TestServices.EnsureInitialized("ko");
        var window = new Window();
        window.Show();
        return window;
    }

    private static async Task<IReadOnlyList<string>> FilesOnClipboard(IClipboard clipboard) =>
        [.. (await clipboard.TryGetFilesAsync() ?? []).Select(f => f.TryGetLocalPath()!)];

    [AvaloniaFact]
    public async Task One_picture_goes_on_the_clipboard_as_a_picture()
    {
        var top = Top();
        var path = Picture(Folder(), "photo.jpg", 120, 80);

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, [path], new ListLogger()));

        using var bitmap = await top.Clipboard!.TryGetBitmapAsync();
        Assert.NotNull(bitmap);
        Assert.Equal((120, 80), (bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        top.Close();
    }

    [AvaloniaFact]
    public async Task A_picture_a_phone_turned_goes_on_the_clipboard_the_way_up_it_is_shown()
    {
        // Stored 200 x 100 with EXIF orientation 6: every viewer shows it 100 x 200, and so must the clipboard's picture.
        var top = Top();
        var path = Path.Combine(Folder(), "phone.jpg");
        using (var turned = new MagickImage(MagickColors.SteelBlue, 200, 100) { Orientation = OrientationType.RightTop })
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            turned.SetProfile(exif);
            turned.Write(path, MagickFormat.Jpeg);
        }

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, [path], new ListLogger()));

        using var bitmap = await top.Clipboard!.TryGetBitmapAsync();
        Assert.Equal((100, 200), (bitmap!.PixelSize.Width, bitmap.PixelSize.Height));
        top.Close();
    }

    [AvaloniaFact]
    public async Task An_image_tool_copies_the_picture_the_way_its_window_shows_it()
    {
        // Shown 100 x 200 (EXIF orientation 6): the left half is kept, hidden under a box and set on a white backdrop
        // 10 % of its short side (5 px) wide all round, as Save would draw it.
        var top = Top();
        var path = Path.Combine(Folder(), "phone.jpg");
        using (var turned = new MagickImage(MagickColors.SteelBlue, 200, 100) { Orientation = OrientationType.RightTop })
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            turned.SetProfile(exif);
            turned.Write(path, MagickFormat.Jpeg);
        }
        var edit = new ImageEdit
        {
            Crop = new CropRect { Width = .5 },
            Redact = new RedactEdit { Boxes = [new EditRect { Width = 1, Height = 1 }] },
            Background = new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#FFFFFF", Padding = .1, Corner = 0, Shadow = 0 },
        };

        Assert.True(await ResultClipboard.CopyEditedAsync(top.Clipboard!, path, edit, new ListLogger()));

        using var bitmap = await top.Clipboard!.TryGetBitmapAsync();
        Assert.Equal((60, 210), (bitmap!.PixelSize.Width, bitmap.PixelSize.Height));
        Assert.Equal((0, 0, 0), Pixel(bitmap, 30, 105)); // the box
        Assert.Equal((255, 255, 255), Pixel(bitmap, 2, 2)); // the backdrop
        top.Close();
    }

    [AvaloniaFact]
    public async Task An_image_tool_copies_nothing_from_a_picture_that_is_gone()
    {
        var top = Top();
        var log = new ListLogger();

        Assert.False(await ResultClipboard.CopyEditedAsync(top.Clipboard!, Path.Combine(Folder(), "gone.png"), new ImageEdit(), log));
        Assert.NotEmpty(log.Lines);
        top.Close();
    }

    [AvaloniaFact]
    public async Task On_windows_the_picture_goes_on_as_an_opaque_dib_and_a_png_not_through_avalonia()
    {
        // Avalonia's bitmap on the Windows clipboard was one bit a pixel: pasted black. 4 x 2: the top row two see-through
        // pixels then two orange ones, the bottom row steel blue.
        var top = Top();
        var path = Path.Combine(Folder(), "logo.png");
        using (var picture = new MagickImage(MagickColors.SteelBlue, 4, 2))
        {
            picture.Alpha(AlphaOption.Set);
            using (var pixels = picture.GetPixels())
                for (var x = 0; x < 4; x++)
                    pixels.SetPixel(x, 0, x < 2 ? [255, 0, 0, 0] : [255, 122, 26, 255]);
            picture.Write(path, MagickFormat.Png);
        }
        var platform = new PictureClipboard();
        await top.Clipboard!.ClearAsync();

        Assert.True(await ResultClipboard.CopyEditedAsync(top.Clipboard!, path, new ImageEdit(), new ListLogger(), platform));

        var dib = platform.Dib!;
        Assert.Equal((40, 4, 2, (short)1, (short)32, 0), (BitConverter.ToInt32(dib, 0), BitConverter.ToInt32(dib, 4), BitConverter.ToInt32(dib, 8),
            BitConverter.ToInt16(dib, 12), BitConverter.ToInt16(dib, 14), BitConverter.ToInt32(dib, 16)));
        Assert.Equal(40 + 4 * 2 * 4, dib.Length);
        // Bottom-up, blue-green-red: the first row is the picture's bottom one; see-through turns white, as Paint shows it.
        Assert.Equal(new byte[] { 180, 130, 70, 255 }, dib[40..44]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, dib[56..60]);
        Assert.Equal(new byte[] { 26, 122, 255, 255 }, dib[68..72]);
        using (var png = SkiaSharp.SKBitmap.Decode(platform.Png!))
        {
            Assert.Equal((4, 2), (png.Width, png.Height));
            Assert.Equal(0, png.GetPixel(0, 0).Alpha); // the PNG keeps what is see-through
            Assert.Equal(new SkiaSharp.SKColor(255, 122, 26), png.GetPixel(3, 0));
        }
        Assert.Null(await top.Clipboard!.TryGetBitmapAsync());
        top.Close();
    }

    [AvaloniaFact]
    public async Task On_windows_a_capture_goes_on_as_an_opaque_dib_too()
    {
        var top = Top();
        using var shot = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(2, 1, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
        shot.SetPixel(0, 0, new SkiaSharp.SKColor(70, 130, 180));
        shot.SetPixel(1, 0, new SkiaSharp.SKColor(255, 122, 26));
        using var image = SkiaSharp.SKImage.FromBitmap(shot);
        var platform = new PictureClipboard();

        Assert.True(await ResultClipboard.CopyPictureAsync(top.Clipboard!, image, new ListLogger(), platform));

        Assert.Equal(new byte[] { 180, 130, 70, 255, 26, 122, 255, 255 }, platform.Dib![40..48]);
        top.Close();
    }

    /// <summary>The Windows clipboard as <see cref="ResultClipboard"/> sees it: it keeps what it is handed.</summary>
    private sealed class PictureClipboard : Filee.Core.Platform.IPlatformServices
    {
        public byte[]? Png { get; private set; }
        public byte[]? Dib { get; private set; }
        public bool SetsClipboardPictures => true;

        public bool SetClipboardPicture(byte[] png, byte[] dib)
        {
            (Png, Dib) = (png, dib);
            return true;
        }

        public bool IsFileManagerAt(int x, int y) => false;
        public string? ProcessNameAt(int x, int y) => null;
        public IReadOnlyList<string> GetFileManagerSelection() => [];
        public int SystemDragThreshold => 4;
        public bool PrefersReducedMotion => false;
        public void SetStartWithSystem(bool enabled, string executablePath) { }
        public void SetContextMenu(bool enabled, string executablePath, string label) { }
        public Filee.Core.Platform.ModernContextMenuState GetModernContextMenuState(string executablePath) => Filee.Core.Platform.ModernContextMenuState.Unsupported;
        public Task<Filee.Core.Platform.ModernContextMenuResult> SetModernContextMenuAsync(bool enabled, string executablePath) =>
            Task.FromResult(new Filee.Core.Platform.ModernContextMenuResult(false));
        public void RevealInFileManager(string path) { }
        public void RaiseTopmost(nint windowHandle) { }
    }

    /// <summary>The red, green and blue of one pixel of <paramref name="bitmap"/>.</summary>
    private static (int, int, int) Pixel(Avalonia.Media.Imaging.Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }
        return bitmap.Format == Avalonia.Platform.PixelFormats.Bgra8888 ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);
    }

    [AvaloniaFact]
    public async Task Several_results_go_on_the_clipboard_as_a_list_of_files()
    {
        var top = Top();
        var folder = Folder();
        string[] paths = [Picture(folder, "a.png", 50, 50), Picture(folder, "b.png", 60, 60)];

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, paths, new ListLogger()));

        Assert.Equal(paths, await FilesOnClipboard(top.Clipboard!));
        Assert.Null(await top.Clipboard!.TryGetBitmapAsync()); // not a picture of the first one
        top.Close();
    }

    [AvaloniaFact]
    public async Task An_animation_goes_on_as_the_file_so_it_keeps_moving()
    {
        var top = Top();
        var path = Path.Combine(Folder(), "anim.gif");
        using (var frames = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue })
                frames.Add(new MagickImage(color, 40, 30) { AnimationDelay = 10 });
            frames.Write(path, MagickFormat.Gif);
        }

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, [path], new ListLogger()));

        Assert.Equal([path], await FilesOnClipboard(top.Clipboard!));
        top.Close();
    }

    [AvaloniaFact]
    public async Task A_result_that_is_no_picture_goes_on_as_the_file()
    {
        var top = Top();
        var path = Path.Combine(Folder(), "note.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, [path], new ListLogger()));

        Assert.Equal([path], await FilesOnClipboard(top.Clipboard!));
        top.Close();
    }

    [AvaloniaFact]
    public async Task A_picture_that_cannot_be_read_goes_on_as_the_file()
    {
        // A file that says ".png" and is not one: no picture to make of it, but it is the result all the same.
        var top = Top();
        var path = Path.Combine(Folder(), "broken.png");
        File.WriteAllBytes(path, new byte[3000]);

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, [path], new ListLogger()));

        Assert.Equal([path], await FilesOnClipboard(top.Clipboard!));
        top.Close();
    }

    [AvaloniaFact]
    public async Task A_result_that_is_gone_copies_nothing_and_only_the_log_hears_of_it()
    {
        var top = Top();
        await top.Clipboard!.SetTextAsync("what was there before");
        var gone = Path.Combine(Folder(), "moved.png");
        var log = new ListLogger();

        Assert.False(await ResultClipboard.CopyAsync(top.Clipboard, top.StorageProvider, [gone], log));

        Assert.Equal("what was there before", await top.Clipboard.TryGetTextAsync()); // the clipboard is as it was
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("moved.png", line.Message);
        top.Close();
    }

    [AvaloniaFact]
    public async Task Results_that_are_gone_are_left_out_of_the_list_and_the_rest_is_copied()
    {
        var top = Top();
        var folder = Folder();
        var kept = Picture(folder, "kept.png", 50, 50);
        string[] paths = [Path.Combine(folder, "moved.png"), kept, Picture(folder, "also.png", 40, 40)];

        Assert.True(await ResultClipboard.CopyAsync(top.Clipboard!, top.StorageProvider, paths, new ListLogger()));

        Assert.Equal([kept, paths[2]], await FilesOnClipboard(top.Clipboard!));
        top.Close();
    }
}
