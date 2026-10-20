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

    /// <summary>
    /// How many files the donut opens the files of to see that they are still pictures. It opens inside Explorer's drag,
    /// a few milliseconds per photo: the rest are looked at when the crop is picked (<see cref="StillPictures"/>).
    /// </summary>
    internal const int LookedIntoOnOpen = 24;

    /// <summary>True when the crop can cut files of format <paramref name="formatId"/>.</summary>
    internal static bool IsEditable(string formatId) => Editable.Contains(formatId);

    /// <summary>
    /// Null when the tools can work on <paramref name="files"/>, otherwise the reason they can't. Crop needs single,
    /// still pictures of an editable format: every file's format is checked, but only the first
    /// <see cref="LookedIntoOnOpen"/> files are opened. <paramref name="formatOnly"/> (compress, strip metadata) only rules
    /// out the formats no tool is offered for: those presets keep an animation's frames and need no picture to look at.
    /// </summary>
    public static string? Reason(IReadOnlyList<string> files, ILocalizer loc, bool formatOnly = false)
    {
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var id = FormatRegistry.Detect(path)?.Id;
            if (id is not null && NotOffered.Contains(id))
                return loc["donut.tool_single_images"];
            if (!formatOnly && (id is null || !IsEditable(id) || (i < LookedIntoOnOpen && !IsStill(path, id))))
                return loc["donut.tool_single_images"];
        }
        return null;
    }

    /// <summary>
    /// The files of <paramref name="files"/> the crop can cut: single, still pictures of an editable format. Opens every
    /// file, so it is called off the UI thread, once the crop is picked.
    /// </summary>
    public static List<string> StillPictures(IEnumerable<string> files) =>
        [.. files.Where(path => FormatRegistry.Detect(path)?.Id is { } id && IsEditable(id) && IsStill(path, id))];

    /// <summary>One frame, read the way the engine reads the format. A file that can't be read is not a picture to edit.</summary>
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
