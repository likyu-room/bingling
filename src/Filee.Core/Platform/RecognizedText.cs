// What the operating system read on a picture for the redact tool's "find what to hide": its lines of text with a box for
// every word, and the faces. Positions are pixels of the picture that was read.

namespace Filee.Core.Platform;

/// <summary>A box on a picture, in its pixels.</summary>
public sealed record PixelBox(double X, double Y, double Width, double Height);

/// <summary>A word as the text recognition read it, with its box in pixels.</summary>
public sealed record RecognizedWord(string Text, double X, double Y, double Width, double Height);

/// <summary>A line of text: what it says, and its words left to right.</summary>
public sealed record RecognizedLine(string Text, IReadOnlyList<RecognizedWord> Words);

/// <summary>What <see cref="IPlatformServices.RecognizeAsync"/> found on a picture.</summary>
/// <param name="TextAvailable">False when the text could not be read here at all (no recognition language on this PC, or
/// another OS): <paramref name="Lines"/> is then empty, though faces may still have been looked for.</param>
/// <param name="Failed">True when looking went wrong (too slow, or the picture could not be read): nothing was found, and
/// that says nothing about the picture.</param>
public sealed record RecognitionResult(IReadOnlyList<RecognizedLine> Lines, IReadOnlyList<PixelBox> Faces, bool TextAvailable, bool Failed = false)
{
    /// <summary>Nothing read, and no text recognition: what a platform without one answers.</summary>
    public static RecognitionResult Empty { get; } = new([], [], false);

    /// <summary>
    /// Where the copy of a picture to read and what was read on it are kept while it is read (<c>%TEMP%\Bingling\find</c>).
    /// They are private, so Bingling empties it when it starts, in case it ended in the middle of one.
    /// </summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), AppIdentity.Folder, "find");
}
