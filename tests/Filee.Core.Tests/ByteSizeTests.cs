// File sizes read like Explorer's: 1024 units, one decimal, and the number never parted from its unit.

using Filee.Core.Formats;

namespace Filee.Core.Tests;

public class ByteSizeTests
{
    /// <summary>The text with a no-break space between number and unit, as ByteSize writes it.</summary>
    private static string Nbsp(string text) => text.Replace(' ', ' ');

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1258291, "1.2 MB")]
    [InlineData(4509715660, "4.2 GB")]
    public void Sizes_read_like_explorer(long bytes, string text) => Assert.Equal(Nbsp(text), ByteSize.Format(bytes));

    [Theory]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(4294967296, "4.0 GB")]
    [InlineData(-5, "0 B")]
    public void Units_change_at_1024(long bytes, string text) => Assert.Equal(Nbsp(text), ByteSize.Format(bytes));

    [Theory]
    [InlineData(1048524, "1023.9 KB")] // 1023.949 KB: still shown in KB
    [InlineData(1048525, "1.0 MB")]    // 1023.950 KB would show as "1024.0 KB"
    [InlineData(1048575, "1.0 MB")]
    [InlineData(1073689395, "1023.9 MB")]
    [InlineData(1073689396, "1.0 GB")]
    public void A_number_that_would_round_to_1024_moves_up_a_unit(long bytes, string text) =>
        Assert.Equal(Nbsp(text), ByteSize.Format(bytes));

    [Fact]
    public void Number_and_unit_are_joined_by_a_no_break_space()
    {
        // "620 B" in the donut's narrow centre must not wrap between "620" and "B".
        Assert.Equal("620 B", ByteSize.Format(620));
        Assert.DoesNotContain(' ', ByteSize.Format(5_000_000));
    }
}
