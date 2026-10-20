// File sizes as text the way Explorer shows them: 1024 units, one decimal ("512 B", "1.2 MB").

using System.Globalization;

namespace Filee.Core.Formats;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// "0 B", "512 B", "1.2 KB", "4.0 GB": whole bytes below 1 KB, one decimal above. Number and unit are joined by a
    /// no-break space, so a narrow label (the donut's centre) never puts "B" on a line of its own.
    /// </summary>
    public static string Format(long bytes)
    {
        var size = (double)Math.Max(0, bytes);
        var unit = 0;
        // Up a unit also when the one decimal shown would round to 1024: "1.0 MB", not "1024.0 KB".
        while (Math.Round(size, 1, MidpointRounding.AwayFromZero) >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{size:0} B")
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.0} {Units[unit]}");
    }
}
