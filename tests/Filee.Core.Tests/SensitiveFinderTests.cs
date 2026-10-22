// What the redact tool's "find what to hide" picks from the text and faces the OCR read: phone numbers, e-mail addresses,
// resident registration numbers, card numbers, addresses and faces, each one only once where the rules overlap, with a box
// over the words it covers, a little larger than they are, as fractions of the picture.

using Filee.Core.Platform;
using Filee.Core.Privacy;

namespace Filee.Core.Tests;

public class SensitiveFinderTests
{
    /// <summary>A line of words side by side, each 20 px tall at y 50, 40 px wide and 10 px apart, from x 100.</summary>
    private static RecognizedLine Line(params string[] words) =>
        new(string.Join(' ', words), [.. words.Select((w, i) => new RecognizedWord(w, 100 + i * 50, 50, 40, 20))]);

    private static RecognitionResult Text(params RecognizedLine[] lines) => new(lines, [], TextAvailable: true);

    private static IReadOnlyList<SensitiveSpot> Find(params RecognizedLine[] lines) => SensitiveFinder.Find(Text(lines), 1000, 500);

    [Theory]
    [InlineData(SensitiveKind.Phone, "010-1234-5678")]
    [InlineData(SensitiveKind.Phone, "02.123.4567")]
    [InlineData(SensitiveKind.Phone, "031 123 4567")]
    [InlineData(SensitiveKind.Phone, "01012345678")]
    [InlineData(SensitiveKind.Email, "kim.minsu@example.com")]
    [InlineData(SensitiveKind.ResidentNumber, "900101-1234567")]
    [InlineData(SensitiveKind.ResidentNumber, "9001011234567")]
    [InlineData(SensitiveKind.CardNumber, "1234-5678-9012-3456")]
    [InlineData(SensitiveKind.CardNumber, "1234567890123456")]
    public void A_line_of_one_word_is_found_as_its_kind(SensitiveKind kind, string word)
    {
        var spot = Assert.Single(Find(Line(word)));
        Assert.Equal(kind, spot.Kind);
        Assert.Equal(word, spot.Text);
    }

    [Fact]
    public void The_box_is_the_word_a_little_larger_as_fractions_of_the_picture()
    {
        // "Tel" is not part of it: only the number's word, 3 px (15 % of its height) larger on every side.
        var spot = Assert.Single(Find(Line("Tel", "010-1234-5678")));
        Assert.Equal(SensitiveKind.Phone, spot.Kind);
        Assert.Equal(147.0 / 1000, spot.Box.X, 6);
        Assert.Equal(47.0 / 500, spot.Box.Y, 6);
        Assert.Equal(46.0 / 1000, spot.Box.Width, 6);
        Assert.Equal(26.0 / 500, spot.Box.Height, 6);
    }

    [Fact]
    public void A_number_the_OCR_split_into_words_is_one_spot_over_all_of_them()
    {
        // As Windows reads "Tel 02 123 4567" and "Card 1234 5678 9012 3456": a word for each group of digits.
        var phone = Assert.Single(Find(Line("Tel", "02", "123", "4567")));
        Assert.Equal(SensitiveKind.Phone, phone.Kind);
        Assert.Equal("02 123 4567", phone.Text);
        Assert.Equal(147.0 / 1000, phone.Box.X, 6); // from the first group's left edge…
        Assert.Equal((290 + 3 - 147.0) / 1000, phone.Box.Width, 6); // …to the last one's right edge

        var card = Assert.Single(Find(Line("Card", "1234", "5678", "9012", "3456")));
        Assert.Equal(SensitiveKind.CardNumber, card.Kind);
        Assert.Equal("1234 5678 9012 3456", card.Text);

        // A hyphen read as a word of its own, and a group cut off at it.
        Assert.Equal(SensitiveKind.Phone, Assert.Single(Find(Line("010", "-", "1234-", "5678"))).Kind);
    }

    [Theory]
    [InlineData("900101-1234567", SensitiveKind.ResidentNumber)] // holds "01-1234567", which reads as a phone number
    [InlineData("4000 0123 4567 8901", SensitiveKind.CardNumber)] // holds "00123456789" and "0123 4567 8901"
    [InlineData("1234 5678 9012 3456", SensitiveKind.CardNumber)] // holds "123456-7890123", which reads as an ID number
    [InlineData("801231-2345678", SensitiveKind.ResidentNumber)]
    public void Where_two_rules_match_the_same_digits_only_the_longer_is_kept(string text, SensitiveKind kind)
    {
        var spot = Assert.Single(Find(Line(text.Split(' '))));
        Assert.Equal(kind, spot.Kind);
    }

    [Fact]
    public void Two_numbers_on_one_line_are_two_spots_in_reading_order()
    {
        Assert.Equal([SensitiveKind.Email, SensitiveKind.Phone], Find(Line("kim@example.com", "010-1234-5678")).Select(s => s.Kind));
        // The number's digits do not run into the e-mail address, whose name part may hold digits and hyphens.
        Assert.Equal([SensitiveKind.Phone, SensitiveKind.Email], Find(Line("010-1234-5678", "kim@example.com")).Select(s => s.Kind));
    }

    [Fact]
    public void A_label_before_an_email_address_is_not_part_of_it()
    {
        var spot = Assert.Single(Find(Line("Email", "kim.minsu@example.com")));
        Assert.Equal("kim.minsu@example.com", spot.Text);
        Assert.Equal(147.0 / 1000, spot.Box.X, 6);
    }

    [Theory]
    [InlineData("서울특별시 강남구 테헤란로 123")]
    [InlineData("서울시 강남구 테헤란로123")]
    [InlineData("경기도 성남시 분당구 판교역로 235")]
    [InlineData("부산광역시 해운대구 해운대로 264-1")]
    [InlineData("경기도 양평군 양평읍 양근로 105")]
    [InlineData("서울특별시 중구 남대문로5길 10")]
    public void An_address_with_a_city_a_district_a_road_and_a_number_is_found(string address)
    {
        var words = address.Split(' ');
        var spot = Assert.Single(Find(Line(["주소:", .. words])));
        Assert.Equal(SensitiveKind.Address, spot.Kind);
        Assert.Equal(address, spot.Text);
    }

    [Theory]
    [InlineData("Hello world 2026")]
    [InlineData("2026-10-22 13:15")]
    [InlineData("서울 강남 맛집")]
    [InlineData("Page 12 of 30")]
    [InlineData("1234 5678")]
    public void Ordinary_text_is_not_found(string text) => Assert.Empty(Find(Line(text.Split(' '))));

    [Fact]
    public void Faces_are_found_with_their_box_a_little_larger()
    {
        var result = new RecognitionResult([], [new PixelBox(400, 100, 100, 200)], TextAvailable: false);
        var face = Assert.Single(SensitiveFinder.Find(result, 1000, 500));
        Assert.Equal(SensitiveKind.Face, face.Kind);
        Assert.Equal(370.0 / 1000, face.Box.X, 6); // 30 px: 15 % of the face's height
        Assert.Equal(70.0 / 500, face.Box.Y, 6);
        Assert.Equal(160.0 / 1000, face.Box.Width, 6);
        Assert.Equal(260.0 / 500, face.Box.Height, 6);
    }

    [Fact]
    public void A_box_at_the_edge_stays_on_the_picture()
    {
        var result = new RecognitionResult([], [new PixelBox(0, 450, 100, 50)], TextAvailable: false);
        var box = Assert.Single(SensitiveFinder.Find(result, 1000, 500)).Box;
        Assert.Equal(0, box.X);
        Assert.Equal(1, box.Y + box.Height, 6);
        Assert.Equal(107.5 / 1000, box.Width, 6);
        Assert.Equal(57.5 / 500, box.Height, 6);
    }

    [Fact]
    public void Text_comes_before_faces_and_a_picture_of_no_size_has_nothing()
    {
        var result = new RecognitionResult([Line("010-1234-5678")], [new PixelBox(10, 10, 50, 50)], TextAvailable: true);
        Assert.Equal([SensitiveKind.Phone, SensitiveKind.Face], SensitiveFinder.Find(result, 1000, 500).Select(s => s.Kind));
        Assert.Empty(SensitiveFinder.Find(result, 0, 500));
        Assert.Empty(SensitiveFinder.Find(RecognitionResult.Empty, 1000, 500));
    }
}
