// Colour codes as designers type them, for the eyedropper's colour card: "#FF7A1A", "rgb(255, 122, 26)", "hsl(25, 100%, 55%)".

using System.Globalization;

namespace Filee.Core.Formats;

public static class ColorCodes
{
    /// <summary>"#FF7A1A": upper-case, no alpha.</summary>
    public static string Hex(byte r, byte g, byte b) => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");

    /// <summary>"rgb(255, 122, 26)".</summary>
    public static string Rgb(byte r, byte g, byte b) => string.Create(CultureInfo.InvariantCulture, $"rgb({r}, {g}, {b})");

    /// <summary>"hsl(25, 100%, 55%)": hue in degrees, saturation and lightness in per cent, each rounded to a whole number.</summary>
    public static string Hsl(byte r, byte g, byte b)
    {
        var (red, green, blue) = (r / 255.0, g / 255.0, b / 255.0);
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2;
        var d = max - min;
        double hue = 0, saturation = 0;
        if (d > 0)
        {
            saturation = d / (1 - Math.Abs(2 * lightness - 1));
            hue = max == red ? (green - blue) / d : max == green ? (blue - red) / d + 2 : (red - green) / d + 4;
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"hsl({Round(hue * 60 + 360) % 360}, {Round(saturation * 100)}%, {Round(lightness * 100)}%)");
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
