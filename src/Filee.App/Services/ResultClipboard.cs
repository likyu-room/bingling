// Puts a finished job's results on the clipboard for the toast card's Copy button: one picture as a picture (to paste in a
// chat or a document), anything else as files (to paste in a folder or an attachment). The image tool window's Copy puts
// its edited picture there the same way, without writing a file, and so does the capture window with the area it took.
// On Windows a picture goes on as a DIB and a PNG through the platform: Avalonia's bitmap there pasted black.

using System.Buffers.Binary;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Filee.Core.Formats;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.Services;

/// <summary>What the toast card's Copy button puts on the clipboard, and how.</summary>
public static class ResultClipboard
{
    /// <summary>
    /// Copies <paramref name="outputs"/> to <paramref name="clipboard"/>: a single result that is a still picture goes as a
    /// picture, upright as the card's thumbnail shows it; everything else goes as a list of files. Results that are gone
    /// are left out. An animation goes as its file: as a picture it would stop moving. Never throws.
    /// </summary>
    /// <returns>True when something was copied; false (and a line in <paramref name="log"/>) when there was nothing to copy.</returns>
    public static async Task<bool> CopyAsync(IClipboard clipboard, IStorageProvider storage, IReadOnlyList<string> outputs, ILogger? log,
        IPlatformServices? platform = null)
    {
        try
        {
            var present = outputs.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
            if (present.Count == 0)
            {
                log?.LogWarning("Nothing to copy: the result is gone ({Outputs})", string.Join(", ", outputs));
                return false;
            }

            // On a worker: a camera's photo takes a while to turn into pixels.
            if (present is [var one] && await Task.Run(() => ReadPicture(one, null, log)) is { } picture)
            {
                using (picture)
                    await PutAsync(clipboard, platform, picture.PeekPixels());
                return true;
            }

            var items = new List<IStorageItem>();
            foreach (var path in present)
            {
                IStorageItem? item = Directory.Exists(path) ? await storage.TryGetFolderFromPathAsync(path) : await storage.TryGetFileFromPathAsync(path);
                if (item is not null)
                    items.Add(item);
            }
            if (items.Count == 0)
            {
                log?.LogWarning("Nothing to copy: the system could not open the results as files ({Outputs})", string.Join(", ", present));
                return false;
            }
            await clipboard.SetFilesAsync(items);
            return true;
        }
        catch (Exception ex)
        {
            // Another program holds the clipboard, or the file went away meanwhile: the card says nothing, the log does.
            log?.LogWarning(ex, "Could not copy the result to the clipboard ({Outputs})", string.Join(", ", outputs));
            return false;
        }
    }

    /// <summary>
    /// The image tool window's Copy: the picture at <paramref name="path"/> with <paramref name="edit"/> drawn on it, upright,
    /// as the window shows it and Save would write it, goes on the clipboard as a picture. Never throws.
    /// </summary>
    /// <returns>True when it was copied; false (and a line in <paramref name="log"/>) when the picture can't be read or the
    /// clipboard is busy.</returns>
    public static async Task<bool> CopyEditedAsync(IClipboard clipboard, string path, ImageEdit edit, ILogger? log, IPlatformServices? platform = null)
    {
        try
        {
            if (await Task.Run(() => ReadPicture(path, edit, log)) is not { } picture)
            {
                log?.LogWarning("Nothing to copy: {Path} can't be read as a picture", path);
                return false;
            }
            using (picture)
                await PutAsync(clipboard, platform, picture.PeekPixels());
            return true;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Could not copy the edited picture to the clipboard ({Path})", path);
            return false;
        }
    }

    /// <summary>The capture window's Copy: a picture in memory (<paramref name="picture"/>) goes on the clipboard. Never throws.</summary>
    /// <returns>True when it was copied; false (and a line in <paramref name="log"/>) when the clipboard is busy.</returns>
    public static async Task<bool> CopyPictureAsync(IClipboard clipboard, SKImage picture, ILogger? log, IPlatformServices? platform = null)
    {
        try
        {
            using var pixels = picture.PeekPixels() ?? throw new ArgumentException("The picture is not in memory", nameof(picture));
            await PutAsync(clipboard, platform, pixels);
            return true;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Could not copy the {Width} × {Height} picture to the clipboard", picture.Width, picture.Height);
            return false;
        }
    }

    /// <summary>
    /// Puts <paramref name="pixels"/> on the clipboard as a picture: through <paramref name="platform"/> where it does
    /// (Windows: a DIB and a PNG, made on a worker), else, or when that clipboard stays busy, through Avalonia's.
    /// </summary>
    private static async Task PutAsync(IClipboard clipboard, IPlatformServices? platform, SKPixmap pixels)
    {
        if (platform is { SetsClipboardPictures: true })
        {
            var (png, dib) = await Task.Run(() =>
            {
                using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
                return (encoded?.ToArray(), Dib(pixels));
            });
            if (png is not null && platform.SetClipboardPicture(png, dib))
                return;
        }
        // Not disposed: the clipboard reads the bitmap when something is pasted, and lets go of it when it is replaced.
        // ponytail: not flushed either, so the picture leaves the clipboard if Filee is quit before the paste; flush
        // (IClipboard.FlushAsync) if that is asked for - it renders a camera photo on the UI thread, on the spot.
        await clipboard.SetBitmapAsync(ToBitmap(pixels));
    }

    /// <summary>
    /// <paramref name="pixels"/> as a DIB for the Windows clipboard: a BITMAPINFOHEADER, then 32-bit blue-green-red rows from
    /// the bottom up (the way every app reads one), on white where the picture is see-through: an app that pastes the
    /// bitmap ignores the fourth byte, and would show black there. The PNG beside it keeps the transparency.
    /// </summary>
    internal static byte[] Dib(SKPixmap pixels)
    {
        var (width, height) = (pixels.Width, pixels.Height);
        var dib = new byte[40 + width * height * 4];
        var header = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height); // positive: bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1); // planes
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32); // bits a pixel; compression 0 = BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], width * height * 4);
        // Premultiplied, a pixel on white is its colour plus the white that shows through: c + (255 - a).
        using var premultiplied = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        pixels.ReadPixels(premultiplied.Info, premultiplied.GetPixels(), premultiplied.RowBytes);
        var source = premultiplied.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            var from = source.Slice(y * premultiplied.RowBytes, width * 4);
            var to = dib.AsSpan(40 + (height - 1 - y) * width * 4, width * 4);
            for (var x = 0; x < to.Length; x += 4)
            {
                var white = 255 - from[x + 3];
                to[x] = (byte)(from[x] + white);
                to[x + 1] = (byte)(from[x + 1] + white);
                to[x + 2] = (byte)(from[x + 2] + white);
                to[x + 3] = 255;
            }
        }
        return dib;
    }

    /// <summary>A copy of <paramref name="picture"/>, a picture in memory (a screen's: BGRA or RGBA), as a bitmap.</summary>
    internal static Bitmap ToBitmap(SKImage picture)
    {
        using var pixels = picture.PeekPixels() ?? throw new ArgumentException("The picture is not in memory", nameof(picture));
        return ToBitmap(pixels);
    }

    /// <summary>A copy of <paramref name="pixels"/> (BGRA or RGBA) as a bitmap.</summary>
    private static Bitmap ToBitmap(SKPixmap pixels) =>
        new(pixels.ColorType == SKColorType.Rgba8888 ? PixelFormats.Rgba8888 : PixelFormats.Bgra8888,
            pixels.AlphaType == SKAlphaType.Unpremul ? AlphaFormat.Unpremul : AlphaFormat.Premul, pixels.GetPixels(),
            new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.RowBytes);

    /// <summary>
    /// The picture at <paramref name="path"/>, upright and with <paramref name="edit"/> (if any) applied, as straight RGBA
    /// pixels; null when it is no still picture the image engine can read (an animation, a PDF, a damaged file), and the
    /// caller copies the file instead.
    /// </summary>
    private static SKBitmap? ReadPicture(string path, ImageEdit? edit, ILogger? log)
    {
        if (FormatRegistry.Detect(path)?.Id is not { } format || ImageToolSupport.StillPictures([path]).Count == 0)
            return null;
        try
        {
            using var frames = ImageReader.Read(path, format, new Preset(), firstFrameOnly: true);
            var image = frames[0];
            image.AutoOrient();
            if (edit is not null)
                ImageEncoder.ApplyEdit(image, edit);
            return EditBridge.ToBitmap(image);
        }
        catch (Exception ex)
        {
            log?.LogInformation(ex, "Could not read {Path} as a picture; it is copied as a file", path);
            return null;
        }
    }
}
