// What the redact tool's "find what to hide" offers to hide: in the text the OCR read, phone numbers, e-mail addresses,
// resident registration numbers, card numbers and street addresses (Korean formats first, spaces, dots and hyphens where
// people put them), and the faces it found. Names are left out: they can't be told from other words reliably.
//
// The OCR splits what people write as one: "010 1234 5678" comes back as three words, a hyphen may be a word of its own.
// So a line's words are run together before the rules look at it, and a spot's box is the words its letters fall in.

using System.Text;
using System.Text.RegularExpressions;
using Filee.Core.Platform;
using Filee.Core.Presets;

namespace Filee.Core.Privacy;

/// <summary>What a <see cref="SensitiveSpot"/> is.</summary>
public enum SensitiveKind
{
    Phone,
    Email,
    ResidentNumber,
    CardNumber,
    Address,
    Face,
}

/// <summary>A part of a picture to offer for hiding.</summary>
/// <param name="Text">The words it covers, as read; empty for a face.</param>
/// <param name="Box">Where it is, as fractions (0 to 1) of the picture, a little larger than the words or the face.</param>
public sealed record SensitiveSpot(SensitiveKind Kind, string Text, EditRect Box);

/// <summary>Picks the <see cref="SensitiveSpot"/>s from what <see cref="IPlatformServices.RecognizeAsync"/> read.</summary>
public static partial class SensitiveFinder
{
    /// <summary>How much a box grows on every side, as a fraction of its height: the OCR's boxes hug the letters.</summary>
    private const double Margin = 0.15;

    /// <summary>
    /// The rules, the most particular first: where two match the same letters the longer one is kept, and on a tie the one
    /// earlier here (a card number holds digits that read as an ID number, an ID number digits that read as a phone number).
    /// </summary>
    private static readonly (SensitiveKind Kind, Regex Rule)[] Rules =
    [
        (SensitiveKind.ResidentNumber, ResidentRule()),
        (SensitiveKind.CardNumber, CardRule()),
        (SensitiveKind.Phone, PhoneRule()),
        (SensitiveKind.Email, EmailRule()),
        (SensitiveKind.Address, AddressRule()),
    ];

    /// <summary>
    /// The spots in <paramref name="read"/>, a picture <paramref name="width"/> × <paramref name="height"/> pixels: the text's
    /// line by line, left to right, then the faces.
    /// </summary>
    public static IReadOnlyList<SensitiveSpot> Find(RecognitionResult read, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return [];
        return
        [
            .. read.Lines.SelectMany(line => InLine(line, width, height)),
            .. read.Faces.Select(f => new SensitiveSpot(SensitiveKind.Face, "", Fraction(f, width, height))),
        ];
    }

    private static IEnumerable<SensitiveSpot> InLine(RecognizedLine line, int width, int height)
    {
        var (text, owners) = RunTogether(line.Words);
        var kept = new List<(SensitiveKind Kind, int Start, int End)>();
        // Longest first; OrderBy is stable, so a tie keeps the rules' order.
        foreach (var (kind, match) in Rules.SelectMany(r => r.Rule.Matches(text).Select(m => (r.Kind, m))).OrderByDescending(m => m.m.Length))
        {
            var (start, end) = (match.Index, match.Index + match.Length);
            if (!kept.Any(k => start < k.End && k.Start < end))
                kept.Add((kind, start, end));
        }
        foreach (var (kind, start, end) in kept.OrderBy(k => k.Start))
        {
            var words = owners[start..end].Where(w => w >= 0).Distinct().Select(w => line.Words[w]).ToList();
            var left = words.Min(w => w.X);
            var top = words.Min(w => w.Y);
            var box = new PixelBox(left, top, words.Max(w => w.X + w.Width) - left, words.Max(w => w.Y + w.Height) - top);
            yield return new SensitiveSpot(kind, string.Join(' ', words.Select(w => w.Text)), Fraction(box, width, height));
        }
    }

    /// <summary>
    /// The line's words as one text, and the word each of its letters came from (-1 for a space put between two). Words
    /// are joined as they are where the OCR may have split one thing (between digits, at a hyphen, a dot or an @), and
    /// with a space elsewhere, so a label ("Email", "Tel") stays apart from what follows it.
    /// </summary>
    private static (string Text, int[] Owners) RunTogether(IReadOnlyList<RecognizedWord> words)
    {
        var text = new StringBuilder();
        var owners = new List<int>();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i].Text;
            if (word.Length == 0)
                continue;
            if (text.Length > 0 && !Joins(text[^1], word[0]))
            {
                text.Append(' ');
                owners.Add(-1);
            }
            text.Append(word);
            owners.AddRange(Enumerable.Repeat(i, word.Length));
        }
        return (text.ToString(), [.. owners]);
    }

    private static bool Joins(char before, char after) =>
        char.IsAsciiDigit(before) && char.IsAsciiDigit(after) || "-.@".Contains(before) || "-.@".Contains(after);

    /// <summary><paramref name="box"/> grown by <see cref="Margin"/> of its height, kept on the picture, as fractions of it.</summary>
    private static EditRect Fraction(PixelBox box, int width, int height)
    {
        var grow = box.Height * Margin;
        var left = Math.Max(0, box.X - grow);
        var top = Math.Max(0, box.Y - grow);
        var right = Math.Min(width, box.X + box.Width + grow);
        var bottom = Math.Min(height, box.Y + box.Height + grow);
        return new EditRect { X = left / width, Y = top / height, Width = (right - left) / width, Height = (bottom - top) / height };
    }

    /// <summary>010-1234-5678, 02.123.4567, 031 123 4567.</summary>
    [GeneratedRegex(@"0\d{1,2}[-. ]?\d{3,4}[-. ]?\d{4}")]
    private static partial Regex PhoneRule();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRule();

    /// <summary>A resident registration number: 900101-1234567.</summary>
    [GeneratedRegex(@"\d{6}[- ]?[1-8]\d{6}")]
    private static partial Regex ResidentRule();

    /// <summary>1234-5678-9012-3456, 1234 5678 9012 3456.</summary>
    [GeneratedRegex(@"\d{4}([- ]?\d{4}){3}")]
    private static partial Regex CardRule();

    /// <summary>
    /// On one line: a city or province (…시, …도), a district or county (…구, …군), a road (…로, …길) and its number,
    /// with any words between them: 서울특별시 강남구 테헤란로 123, 경기도 성남시 분당구 판교역로 235.
    /// </summary>
    [GeneratedRegex(@"[가-힣]+(?:시|도)(?: ?[가-힣]+)*?(?:구|군)(?: ?[가-힣\d]+)*(?:로|길) ?\d+(?:-\d+)?")]
    private static partial Regex AddressRule();
}
