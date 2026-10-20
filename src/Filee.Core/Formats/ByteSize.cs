// File sizes as text the way Explorer shows them: 1024 units, one decimal ("512 B", "1.2 MB").

using System.Globalization;

namespace Filee.Core.Formats;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>"0 B", "512 B", "1.2 KB", "4.0 GB": whole bytes below 1 KB, one decimal above.</summary>
    public static string Format(long bytes)
    {
        var size = (double)Math.Max(0, bytes);
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{size:0} B")
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.0} {Units[unit]}");
    }
}
