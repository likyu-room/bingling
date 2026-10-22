// The colour codes the eyedropper copies: HEX, RGB and HSL as a designer types them.

using Filee.Core.Formats;

namespace Filee.Core.Tests;

public class ColorCodesTests
{
    [Fact]
    public void A_colour_reads_as_hex_rgb_and_hsl()
    {
        Assert.Equal("#FF7A1A", ColorCodes.Hex(255, 122, 26));
        Assert.Equal("rgb(255, 122, 26)", ColorCodes.Rgb(255, 122, 26));
        Assert.Equal("hsl(25, 100%, 55%)", ColorCodes.Hsl(255, 122, 26));
    }

    [Theory]
    [InlineData(0, 0, 0, "#000000", "hsl(0, 0%, 0%)")]
    [InlineData(255, 255, 255, "#FFFFFF", "hsl(0, 0%, 100%)")]
    [InlineData(128, 128, 128, "#808080", "hsl(0, 0%, 50%)")] // grey: no hue, no saturation
    [InlineData(0, 128, 0, "#008000", "hsl(120, 100%, 25%)")]
    [InlineData(0, 0, 255, "#0000FF", "hsl(240, 100%, 50%)")]
    [InlineData(255, 0, 1, "#FF0001", "hsl(0, 100%, 50%)")] // 359.76 rounds to 360, which is 0
    [InlineData(10, 4, 7, "#0A0407", "hsl(330, 43%, 3%)")]
    public void Every_colour_has_a_code(byte r, byte g, byte b, string hex, string hsl)
    {
        Assert.Equal(hex, ColorCodes.Hex(r, g, b));
        Assert.Equal(hsl, ColorCodes.Hsl(r, g, b));
    }
}
