// What the redact tool's "find what to hide" offers to hide: in the text the OCR read, phone numbers, e-mail addresses,
// resident registration numbers, card numbers and street addresses (Korean formats first, spaces, dots and hyphens where
// people put them), and the faces it found. Names are left out: they can't be told from other words reliably.
//
// The OCR splits what people write as one: "010 1234 5678" comes back as three words, a hyphen may be a word of its own.
// So a line's words are run together before the rules look at it, and a spot's box is the words its letters fall in. But
// run together, two numbers side by side read as one long number ("010 1234 5678 010 9876 5432" holds sixteen digits that
// read as a card number). So a number is looked for from every word on, over the fewest words that hold it whole: it may
// begin and end inside a word only where a label or a particle is written on to it ("Tel:010…", "…5678로"), never between
// two digits, and where its words part, its format must have a place for a space (010 | 1234 | 5678, not 010 1 | 234 5678).
// A false number can still read across two true ones; it is offered beside them, never instead of them, so that every
// digit of a number found ends up in a box. Full-width digits read as digits.
//
// The rules run without backtracking: an ordinary line of Korean could otherwise keep the address rule busy for minutes.

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

    /// <summary>The most words a number is split into: a card as "1234 - 5678 - 9012 - 3456" is seven.</summary>
    private const int MostWords = 8;

    /// <summary>
    /// The rules, the most particular first: where two match the same letters the longer one is kept, and on a tie the one
    /// earlier here.
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
    /// line by line, left to right, then the faces. A box that can't be (not a number, of no size, off the picture) is left out.
    /// </summary>
    public static IReadOnlyList<SensitiveSpot> Find(RecognitionResult read, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return [];
        return
        [
            .. read.Lines.SelectMany(line => InLine(line, width, height)),
            .. read.Faces.Select(f => Fraction(f, width, height) is { } box ? new SensitiveSpot(SensitiveKind.Face, "", box) : null).OfType<SensitiveSpot>(),
        ];
    }

    private static IEnumerable<SensitiveSpot> InLine(RecognizedLine line, int width, int height)
    {
        var words = new Words(line.Words);
        var kept = new List<(SensitiveKind Kind, int Start, int End)>();
        // Longest first, and one that lies inside one kept already is left out (OrderBy is stable: of two alike, the rules'
        // order wins). One that only overlaps another stays: where a long false match reads across two numbers ("9876 1002 4111
        // 1111" between a phone and a card number), each number keeps its own box, so none is left half shown.
        foreach (var found in Rules.SelectMany(r => words.Matches(r.Kind, r.Rule)).OrderByDescending(m => m.End - m.Start))
        {
            if (!kept.Any(k => k.Start <= found.Start && found.End <= k.End))
                kept.Add(found);
        }
        foreach (var (kind, start, end) in kept.OrderBy(k => k.Start))
        {
            var covered = words.Owners[start..end].Where(w => w >= 0).Distinct().Select(w => line.Words[w]).ToList();
            var left = covered.Min(w => w.X);
            var top = covered.Min(w => w.Y);
            var box = new PixelBox(left, top, covered.Max(w => w.X + w.Width) - left, covered.Max(w => w.Y + w.Height) - top);
            if (Fraction(box, width, height) is { } fraction)
                yield return new SensitiveSpot(kind, string.Join(' ', covered.Select(w => w.Text)), fraction);
        }
    }

    /// <summary>
    /// A line's words as one text, and where each came from. Words are joined as they are where the OCR may have split one
    /// thing (between digits, at a hyphen, a dot or an @), and with a space elsewhere, so a label ("Email", "Tel") stays apart
    /// from what follows it.
    /// </summary>
    private sealed class Words
    {
        private readonly List<(int Start, int End)> _spans = []; // of each word with letters, in the text

        public Words(IReadOnlyList<RecognizedWord> words)
        {
            var text = new StringBuilder();
            var owners = new List<int>();
            for (var i = 0; i < words.Count; i++)
            {
                var word = new string([.. words[i].Text.Select(Ascii)]);
                if (word.Length == 0)
                    continue;
                if (text.Length > 0 && !Joins(text[^1], word[0]))
                {
                    text.Append(' ');
                    owners.Add(-1);
                }
                _spans.Add((text.Length, text.Length + word.Length));
                text.Append(word);
                owners.AddRange(Enumerable.Repeat(i, word.Length));
            }
            Text = text.ToString();
            Owners = [.. owners];
        }

        public string Text { get; }

        /// <summary>The word each letter of <see cref="Text"/> came from; -1 for a space put between two.</summary>
        public int[] Owners { get; }

        /// <summary>Where <paramref name="rule"/> matches, as start and end in <see cref="Text"/>.</summary>
        public IEnumerable<(SensitiveKind Kind, int Start, int End)> Matches(SensitiveKind kind, Regex rule)
        {
            if (!rule.IsMatch(Text)) // most lines hold nothing: one pass over them
                yield break;
            if (kind is SensitiveKind.Email or SensitiveKind.Address)
            {
                foreach (Match m in rule.Matches(Text))
                {
                    if (!CutsNumber(m.Index) && !CutsNumber(m.Index + m.Length))
                        yield return (kind, m.Index, m.Index + m.Length);
                }
                yield break;
            }
            // A number: from every word on (also one inside a number found already: that may be a false one reading across
            // two), the fewest words that hold one whole.
            for (var i = 0; i < _spans.Count; i++)
            {
                for (var j = i; j < Math.Min(_spans.Count, i + MostWords); j++)
                {
                    if (WholeNumber(kind, rule, i, j) is { } m)
                    {
                        yield return (kind, m.Index, m.Index + m.Length);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The first match of <paramref name="rule"/> over words <paramref name="i"/> to <paramref name="j"/> that begins in word
        /// i, ends in word j and is a number whole; a match that is not one ("0261022-0001" inside an order number) is passed
        /// over for the next ("…/Tel:010-9876-5432"). Null for none.
        /// </summary>
        private Match? WholeNumber(SensitiveKind kind, Regex rule, int i, int j)
        {
            var (from, to) = (_spans[i].Start, _spans[j].End);
            for (var m = rule.Match(Text, from, to - from); m.Success && m.Index < _spans[i].End; m = m.NextMatch())
            {
                if (IsWhole(kind, m, to))
                    return m;
            }
            return null;
        }

        /// <summary>True when <paramref name="m"/> ends in the word that ends at <paramref name="to"/> and is a number whole.</summary>
        private bool IsWhole(SensitiveKind kind, Match m, int to)
        {
            try
            {
                return Owners[m.Index + m.Length - 1] == Owners[to - 1] && !CutsNumber(m.Index) && !CutsNumber(m.Index + m.Length)
                    && SplitsFit(kind, m) && (kind != SensitiveKind.CardNumber || IsCardNumber(m.Value));
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                return false; // a case these checks did not foresee: this match is passed over, not the whole find
            }
        }

        /// <summary>True when <paramref name="at"/> falls inside a word between two digits: beginning or ending there would cut a number.</summary>
        private bool CutsNumber(int at) =>
            at > 0 && at < Text.Length && Owners[at - 1] == Owners[at] && char.IsAsciiDigit(Text[at - 1]) && char.IsAsciiDigit(Text[at]);

        /// <summary>
        /// True when every place <paramref name="m"/>'s words part falls where its format has a space: a phone number after its
        /// area code or before its last four digits, an ID number after its first six, a card number between groups of four.
        /// </summary>
        private bool SplitsFit(SensitiveKind kind, Match m)
        {
            var digits = m.Value.Count(char.IsAsciiDigit);
            var before = 0;
            for (var at = m.Index + 1; at < m.Index + m.Length; at++)
            {
                if (char.IsAsciiDigit(Text[at - 1]))
                    before++;
                if (Owners[at - 1] == Owners[at])
                    continue;
                var fits = kind switch
                {
                    // 02, 010, and four only for 0504 or +82 10 (after 82, 8210): a date or time with a leading 0 then a
                    // number ("06.24 12345", "0930 123456") is no phone.
                    SensitiveKind.Phone => before is 2 or 3 || before == digits - 4
                        || before == 4 && (m.Value.StartsWith('+') || m.Value.Where(char.IsAsciiDigit).Take(3).SequenceEqual("050")),
                    SensitiveKind.ResidentNumber => before == 6,
                    _ => before % 4 == 0,
                };
                if (!fits)
                    return false;
            }
            return true;
        }
    }

    private static bool Joins(char before, char after) =>
        char.IsAsciiDigit(before) && char.IsAsciiDigit(after) || "-.@".Contains(before) || "-.@".Contains(after);

    /// <summary>
    /// A full-width digit, hyphen or full stop (as Chinese, Japanese and Korean text may have them) as its ASCII one, letter for
    /// letter, so the rules read them and every letter stays where it was. Other digits (Arabic-Indic, …) are no number here.
    /// </summary>
    private static char Ascii(char c) => c switch
    {
        >= '０' and <= '９' => (char)(c - '０' + '0'),
        '－' => '-',
        '．' => '.',
        _ => c,
    };

    /// <summary>
    /// True for sixteen digits a card can have: a right Luhn check digit, and a card's first digits (3 to 6 or 9 — 9 for Korean
    /// domestic cards — or Mastercard's 2221 to 2720). Four years in a row ("2023 2024 2025 2026") pass Luhn, but no card begins 2023.
    /// </summary>
    private static bool IsCardNumber(string match)
    {
        var digits = match.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length != 16)
            return false;
        var first = digits[0] * 1000 + digits[1] * 100 + digits[2] * 10 + digits[3];
        if (digits[0] is not (3 or 4 or 5 or 6 or 9) && first is not (>= 2221 and <= 2720))
            return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = (digits.Length - i) % 2 == 0 ? digits[i] * 2 : digits[i]; // every second digit from the right, doubled
            sum += d > 9 ? d - 9 : d;
        }
        return sum % 10 == 0;
    }

    /// <summary>
    /// <paramref name="box"/> grown by <see cref="Margin"/> of its height, cut to the picture, as fractions of it; null for a
    /// box that can't be (a number that is not one, no size) or that is off the picture altogether.
    /// </summary>
    private static EditRect? Fraction(PixelBox box, int width, int height)
    {
        if (!(double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.Width) && double.IsFinite(box.Height)
            && box.Width > 0 && box.Height > 0))
            return null;
        var grow = box.Height * Margin;
        var left = Math.Max(0, box.X - grow);
        var top = Math.Max(0, box.Y - grow);
        var right = Math.Min(width, box.X + box.Width + grow);
        var bottom = Math.Min(height, box.Y + box.Height + grow);
        if (right <= left || bottom <= top)
            return null;
        return new EditRect { X = left / width, Y = top / height, Width = (right - left) / width, Height = (bottom - top) / height };
    }

    // Digits are [0-9], not \d: \d takes every script's digits, which the checks here can't read.

    /// <summary>
    /// 010-1234-5678, 02.123.4567, 031 123 4567, a safe number 0504-1234-5678, with the area code in brackets (02)123-4567,
    /// with the country code +82 10-1234-5678.
    /// </summary>
    [GeneratedRegex(@"(?:\+82[-. ]?|\(?0)(?:50[0-9]|[0-9]{1,2})\)?[-. ]?[0-9]{3,4}[-. ]?[0-9]{4}", RegexOptions.NonBacktracking)]
    private static partial Regex PhoneRule();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.NonBacktracking)]
    private static partial Regex EmailRule();

    /// <summary>A resident registration number: 900101-1234567.</summary>
    [GeneratedRegex(@"[0-9]{6}[- ]?[1-8][0-9]{6}", RegexOptions.NonBacktracking)]
    private static partial Regex ResidentRule();

    /// <summary>1234-5678-9012-3456, 1234 5678 9012 3456.</summary>
    [GeneratedRegex(@"[0-9]{4}([- ]?[0-9]{4}){3}", RegexOptions.NonBacktracking)]
    private static partial Regex CardRule();

    /// <summary>
    /// On one line: a city or province (…시, …도), a district or county (…구, …군), a road (…로, …길) and its number,
    /// with any words between them: 서울특별시 강남구 테헤란로 123, 경기도 성남시 분당구 판교역로 235.
    /// </summary>
    [GeneratedRegex(@"[가-힣]+(?:시|도)(?: ?[가-힣]+)*?(?:구|군)(?: ?[가-힣0-9]+)*(?:로|길) ?[0-9]+(?:-[0-9]+)?", RegexOptions.NonBacktracking)]
    private static partial Regex AddressRule();
}
