// Picks the small picture drawn above a donut slice's name: what the preset does when it is a tool, a turn or a merge,
// otherwise what it makes. The pictures themselves are in Styles/Icons.axaml (Icon.Donut.*, Material Design Icons).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Filee.Core.Formats;
using Filee.Core.Presets;

namespace Filee.App.Controls;

/// <summary>Which icon a donut slice gets.</summary>
public static class DonutIcons
{
    /// <summary>
    /// The icon for <paramref name="preset"/>, or null when nothing fits (a CAD or font format; the slice is then just
    /// its name). A preset's id comes first, so "Merge PDF" is a merge and not a PDF.
    /// </summary>
    public static Geometry? For(Preset preset) => Find(preset.Id switch
    {
        "crop" => "Crop",
        "add-background" => "AddBackground",
        "redact" => "Redact",
        "annotate" => "Annotate",
        "compress-image" => "Compress",
        "strip-metadata" => "StripMetadata",
        "tools" => "Tools",
        "rotate-left" => "RotateLeft",
        "rotate" or "rotate-right" or "rotate-180" => "RotateRight",
        "flip" or "flip-horizontal" => "FlipHorizontal",
        "flip-vertical" => "FlipVertical",
        "merge-pdf" or "zip-all" => "Merge",
        // The same format as the file is only ever a change to the picture itself (half size, grey scale ...).
        _ when preset.TargetFormat == BuiltInData.SameAsSource => "Image",
        _ => NameOf(preset.TargetFormat),
    });

    /// <summary>The icon for what a preset that writes <paramref name="formatId"/> makes (null for a format with none).</summary>
    public static Geometry? ForFormat(string formatId) => Find(NameOf(formatId));

    /// <summary>A plain file: what the toast's circle shows for a format without an icon of its own (CAD, fonts).</summary>
    public static Geometry? AnyFile => Find("File");

    private static string? NameOf(string formatId) => FormatRegistry.FindById(formatId)?.Category switch
    {
        FormatCategory.Image or FormatCategory.Vector => "Image",
        FormatCategory.Pdf or FormatCategory.Document or FormatCategory.Spreadsheet or FormatCategory.Presentation
            or FormatCategory.Hwp or FormatCategory.Text or FormatCategory.Ebook => "Document",
        FormatCategory.Video => "Video",
        FormatCategory.Audio => "Audio",
        // Includes the "folder" target of Extract.
        FormatCategory.Archive => "Archive",
        _ => null,
    };

    private static Geometry? Find(string? name) =>
        name is null ? null : Application.Current?.FindResource("Icon.Donut." + name) as Geometry;
}
