// Colour of a ✓ drawn on a coloured disc in XAML, chosen the way the donut's outcome badge chooses it.

using Avalonia.Data.Converters;
using Avalonia.Media;
using Filee.App.Controls;

namespace Filee.App.Views;

/// <summary>Turns a disc's brush into the brush of the glyph on it: white or near-black, whichever reads.</summary>
public static class BadgeGlyphConverter
{
    /// <summary>Use as <c>Converter={x:Static v:BadgeGlyphConverter.Instance}</c> on the disc's Background.</summary>
    public static readonly IValueConverter Instance = new FuncValueConverter<IBrush?, IBrush>(DonutMenu.BadgeGlyphBrush);
}
