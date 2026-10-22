// Puts a finished job's results on the clipboard for the toast card's Copy button: one picture as a picture (to paste in a
// chat or a document), anything else as files (to paste in a folder or an attachment). The image tool window's Copy puts
// its edited picture there the same way, without writing a file.

using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;

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
    public static async Task<bool> CopyAsync(IClipboard clipboard, IStorageProvider storage, IReadOnlyList<string> outputs, ILogger? log)
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
                // Not disposed: the clipboard reads the bitmap when something is pasted, and lets go of it when it is replaced.
                // ponytail: not flushed either, so the picture leaves the clipboard if Filee is quit before the paste; flush
                // (IClipboard.FlushAsync) if that is asked for - it renders a camera photo on the UI thread, on the spot.
                await clipboard.SetBitmapAsync(picture);
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
    public static async Task<bool> CopyEditedAsync(IClipboard clipboard, string path, ImageEdit edit, ILogger? log)
    {
        try
        {
            if (await Task.Run(() => ReadPicture(path, edit, log)) is not { } picture)
            {
                log?.LogWarning("Nothing to copy: {Path} can't be read as a picture", path);
                return false;
            }
            await clipboard.SetBitmapAsync(picture); // not disposed, as in CopyAsync
            return true;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Could not copy the edited picture to the clipboard ({Path})", path);
            return false;
        }
    }

    /// <summary>
    /// The picture at <paramref name="path"/>, upright and with <paramref name="edit"/> (if any) applied, as a bitmap; null
    /// when it is no still picture the image engine can read (an animation, a PDF, a damaged file), and the caller copies
    /// the file instead.
    /// </summary>
    private static Bitmap? ReadPicture(string path, ImageEdit? edit, ILogger? log)
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
            using var pixels = EditBridge.ToBitmap(image);
            return new Bitmap(PixelFormats.Rgba8888, AlphaFormat.Unpremul, pixels.GetPixels(), new PixelSize(pixels.Width, pixels.Height),
                new Vector(96, 96), pixels.RowBytes);
        }
        catch (Exception ex)
        {
            log?.LogInformation(ex, "Could not read {Path} as a picture; it is copied as a file", path);
            return null;
        }
    }
}
