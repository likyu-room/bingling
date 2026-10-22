// What the redact tool's "find what to hide" picks from the text and faces the OCR read: phone numbers, e-mail addresses,
// resident registration numbers, card numbers, addresses and faces, each one only once where the rules overlap, two numbers
// side by side as two, with a box over the words it covers, a little larger than they are, as fractions of the picture.

using System.Diagnostics;
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
    [InlineData(SensitiveKind.CardNumber, "4111-1111-1111-1111")]
    [InlineData(SensitiveKind.CardNumber, "5500000000000004")]
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
        // As Windows reads "Tel 02 123 4567" and "Card 4111 1111 1111 1111": a word for each group of digits.
        var phone = Assert.Single(Find(Line("Tel", "02", "123", "4567")));
        Assert.Equal(SensitiveKind.Phone, phone.Kind);
        Assert.Equal("02 123 4567", phone.Text);
        Assert.Equal(147.0 / 1000, phone.Box.X, 6); // from the first group's left edge…
        Assert.Equal((290 + 3 - 147.0) / 1000, phone.Box.Width, 6); // …to the last one's right edge

        var card = Assert.Single(Find(Line("Card", "4111", "1111", "1111", "1111")));
        Assert.Equal(SensitiveKind.CardNumber, card.Kind);
        Assert.Equal("4111 1111 1111 1111", card.Text);

        // A hyphen read as a word of its own, a group cut off at it, a number cut anywhere.
        Assert.Equal(SensitiveKind.Phone, Assert.Single(Find(Line("010", "-", "1234-", "5678"))).Kind);
        Assert.Equal("01 0-1234 -5678", Assert.Single(Find(Line("01", "0-1234", "-5678")), s => s.Kind == SensitiveKind.Phone).Text);
    }

    [Theory]
    [InlineData("010 1234 5678 010 9876 5432", "010 1234 5678", "010 9876 5432")] // once read as a card number
    [InlineData("010 123 4567 010 987 6543", "010 123 4567", "010 987 6543")]
    [InlineData("02 123 4567 02 765 4321", "02 123 4567", "02 765 4321")]
    [InlineData("10 010 1234 5678", "010 1234 5678")] // a number before it is not part of it
    public void Numbers_side_by_side_are_each_their_own_spot_over_all_of_their_words(string line, params string[] phones)
    {
        var spots = Find(Line(line.Split(' ')));
        Assert.All(spots, s => Assert.Equal(SensitiveKind.Phone, s.Kind));
        Assert.Equal(phones, spots.Select(s => s.Text));
        // The second one's box reaches its last word: nothing of it is left to read.
        var last = Line(line.Split(' ')).Words[^1];
        Assert.Equal((last.X + last.Width + 3) / 1000, spots[^1].Box.X + spots[^1].Box.Width, 6);
    }

    [Theory]
    [InlineData("010-1234-5678로 연락 주세요", SensitiveKind.Phone, "010-1234-5678로")] // the particle is written on to it
    [InlineData("Tel:010-1234-5678", SensitiveKind.Phone, "Tel:010-1234-5678")]
    [InlineData("kim@example.com으로 보내 주세요", SensitiveKind.Email, "kim@example.com으로")]
    [InlineData("주민번호:900101-1234567", SensitiveKind.ResidentNumber, "주민번호:900101-1234567")]
    public void A_number_with_a_label_or_a_particle_written_on_to_it_is_found_with_that_word(string line, SensitiveKind kind, string text)
    {
        var spot = Assert.Single(Find(Line(line.Split(' '))));
        Assert.Equal(kind, spot.Kind);
        Assert.Equal(text, spot.Text);
    }

    [Theory]
    [InlineData("4111 1111 1111 1111", true)] // a Visa test number
    [InlineData("5105 1051 0510 5100", true)] // a Mastercard one
    [InlineData("4000 0566 5566 5556", true)]
    [InlineData("4111 1111 1111 1112", false)] // the check digit is wrong
    [InlineData("2023 2024 2025 2026", false)] // its check digit fits, but no card begins 2023
    [InlineData("1234 5678 9012 3456", false)]
    public void A_card_number_has_a_right_check_digit_and_a_card_s_first_digits(string number, bool card)
    {
        var spots = Find(Line(number.Split(' ')));
        if (card)
            Assert.Equal(SensitiveKind.CardNumber, Assert.Single(spots).Kind);
        else
            Assert.Empty(spots); // nor anything else in its digits
    }

    [Theory]
    [InlineData("900101-1234567", SensitiveKind.ResidentNumber)] // holds "01-1234567", which reads as a phone number
    [InlineData("801231-2345678", SensitiveKind.ResidentNumber)]
    [InlineData("4000056655665556", SensitiveKind.CardNumber)] // holds "00056655665", "056655665556" and "400005-6655665"
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
    [InlineData("합계 150000 120000 30000")] // a row of amounts
    [InlineData("주문번호 20261022-0001234")]
    [InlineData("송장번호 6012 3456 7890")]
    public void Ordinary_text_is_not_found(string text) => Assert.Empty(Find(Line(text.Split(' '))));

    [Fact]
    public void Long_lines_of_ordinary_Korean_are_looked_through_at_once()
    {
        // "…도" and "…구" with no road and number after them: the address rule once took seconds on a line like this, and
        // twice as long for every few more letters.
        var words = "저도 친구와 함께 학교로 가는 길에 맛있는 음식을 먹었어요 서울시 강남구 가가가 정말 즐거운 하루였습니다".Split(' ');
        var line = Line([.. Enumerable.Repeat(words, 5).SelectMany(w => w)]);
        Assert.True(line.Text.Length >= 200, $"{line.Text.Length} letters");
        SensitiveFinder.Find(Text(Line("서울시 강남구")), 1000, 500); // the rules are made on first use

        var clock = Stopwatch.StartNew();
        var spots = SensitiveFinder.Find(Text([.. Enumerable.Repeat(line, 10)]), 1000, 500);
        clock.Stop();

        Assert.Empty(spots);
        Assert.True(clock.ElapsedMilliseconds < 100, $"took {clock.ElapsedMilliseconds} ms");
    }

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
    public void A_box_that_cannot_be_is_left_out()
    {
        var result = new RecognitionResult(
            [new RecognizedLine("010-1234-5678", [new RecognizedWord("010-1234-5678", double.PositiveInfinity, 50, 40, 20)])],
            [
                new PixelBox(100, 100, -50, 20), // a negative width
                new PixelBox(100, 100, 50, 0), // no height
                new PixelBox(double.NaN, 100, 50, 50),
                new PixelBox(-500, 100, 10, 10), // off the picture altogether
                new PixelBox(100, 100, double.PositiveInfinity, 50),
                new PixelBox(900, 400, 300, 300), // partly off it: cut to the picture
            ],
            TextAvailable: true);
        var box = Assert.Single(SensitiveFinder.Find(result, 1000, 500)).Box;
        Assert.Equal(1, box.X + box.Width, 6);
        Assert.Equal(1, box.Y + box.Height, 6);
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
