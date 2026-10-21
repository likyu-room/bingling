// The typeface for text drawn on a picture by the annotate tool. Hangul must not come out as boxes (spec §3.4), so it is
// Malgun Gothic (every Windows has it); where that is missing, whatever font Skia finds that has Hangul; and only then
// Skia's default. The same font draws the preview and the saved picture, so both break the same way or not at all.

using SkiaSharp;

namespace Filee.Engines.Magick;

internal static class AnnotationFonts
{
    // One typeface for the process: looked up on first use, and used by the preview on every frame.
    // ponytail: no per-character fallback (CadFonts has one), so a script Malgun lacks, like Hanzi outside Hanja, is
    // missing-glyph boxes. Add a run split like CadFonts.Layout if annotating Chinese or Hindi text matters.
    private static readonly Lazy<SKTypeface> Face = new(Find);

    /// <summary>The typeface's family, for a text box that shows a text being typed as it will be drawn.</summary>
    public static string FamilyName => Face.Value.FamilyName;

    /// <summary>A font of <paramref name="size"/> pixels, set up so a small preview lays text out like the saved picture (no hinting).</summary>
    public static SKFont Create(float size) => new(Face.Value, size)
    {
        Hinting = SKFontHinting.None,
        Subpixel = true,
        LinearMetrics = true,
        Edging = SKFontEdging.Antialias,
    };

    private static SKTypeface Find()
    {
        // MatchFamily returns null for a family that is not installed (FromFamilyName would silently substitute).
        if (SKFontManager.Default.MatchFamily("Malgun Gothic") is { } malgun && malgun.ContainsGlyph('가'))
            return malgun;
        return SKFontManager.Default.MatchCharacter('가') ?? SKTypeface.Default;
    }
}
