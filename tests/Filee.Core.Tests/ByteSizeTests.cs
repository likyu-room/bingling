// File sizes read like Explorer's: 1024 units, one decimal.

using Filee.Core.Formats;

namespace Filee.Core.Tests;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1258291, "1.2 MB")]
    [InlineData(4509715660, "4.2 GB")]
    public void Sizes_read_like_explorer(long bytes, string text) => Assert.Equal(text, ByteSize.Format(bytes));

    [Theory]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(4294967296, "4.0 GB")]
    [InlineData(-5, "0 B")]
    public void Units_change_at_1024(long bytes, string text) => Assert.Equal(text, ByteSize.Format(bytes));
}
