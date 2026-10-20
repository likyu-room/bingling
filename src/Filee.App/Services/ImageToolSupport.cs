// Which files the image tools (crop, compress, strip metadata) can work on, and the reason when they can't.

using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;

namespace Filee.App.Services;

public static class ImageToolSupport
{
    /// <summary>
    /// Raster formats the image engine reads and writes whose tool output is not right, so no tool is offered for them.
    /// ICO: the result is padded to a square and stored in the icon sizes. XBM: ImageMagick names the data in the file
    /// after the file name, and the tools' names ("s Cropped") hold a space, which is no C identifier: the file is invalid.
    /// </summary>
    private static readonly string[] NotOffered = ["ico", "xbm"];

    /// <summary>
    /// Formats the crop cuts and writes back as they are: the ones the image engine reads and writes, and HEIC (the engine
    /// only reads it, but it has its own writer). Vector, PDF and read-only formats (camera RAW, OpenEXR ...) are not here.
    /// </summary>
    private static readonly HashSet<string> Editable =
        [.. ImageEncoder.Readable.Intersect(ImageEncoder.Writable).Except(NotOffered), "heic"];

    /// <summary>True when the crop can cut files of format <paramref name="formatId"/>.</summary>
    internal static bool IsEditable(string formatId) => Editable.Contains(formatId);

    /// <summary>
    /// Null when the tools can work on <paramref name="files"/>, otherwise the reason they can't. Crop needs single,
    /// still pictures of an editable format. <paramref name="formatOnly"/> (compress, strip metadata) only rules out the
    /// formats no tool is offered for: those presets keep an animation's frames and need no picture to look at.
    /// </summary>
    public static string? Reason(IReadOnlyList<string> files, ILocalizer loc, bool formatOnly = false)
    {
        foreach (var path in files)
        {
            var id = FormatRegistry.Detect(path)?.Id;
            if (id is not null && NotOffered.Contains(id))
                return loc["donut.tool_single_images"];
            if (!formatOnly && (id is null || !IsEditable(id) || !IsStill(path, id)))
                return loc["donut.tool_single_images"];
        }
        return null;
    }

    /// <summary>
    /// One frame, read the way the engine reads the format. A file that can't be read is not a picture to edit.
    /// ponytail: pings on the UI thread as the donut opens, stopping at the first file that fails; a selection of thousands
    /// of animated files could be felt: cap the files looked at, or ping off the UI thread, if that turns out slow.
    /// </summary>
    private static bool IsStill(string path, string formatId)
    {
        // Magick sees an animated PNG as one picture; the engine reads its frames with its own reader.
        if (formatId is "png" or "apng" && Apng.IsAnimated(path))
            return false;
        try
        {
            using var frames = new MagickImageCollection();
            frames.Ping(path, ImageReader.Settings(formatId, new Preset()));
            return frames.Count == 1;
        }
        catch (Exception)
        {
            // Missing, locked or damaged: whatever the reason, the donut must still open, just without the tool.
            return false;
        }
    }
}
