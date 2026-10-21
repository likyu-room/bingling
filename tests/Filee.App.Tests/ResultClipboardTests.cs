// The toast card's Copy button: what ends up on the clipboard for a result (the picture, a list of files, nothing) and what
// the log says when there was nothing to copy. The window and the "Copied" label are in ToastWindowTests.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Filee.App.Services;
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
