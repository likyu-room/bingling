// A picture copied from Bingling must paste as a picture everywhere: apps that take the clipboard's bitmap (CF_BITMAP,
// KakaoTalk and Paint among them) as well as those that take its PNG.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Filee.Platform.Windows;

namespace Filee.Platform.Tests;

public sealed class WindowsClipboardTests
{
    [Fact]
    public void A_picture_on_the_clipboard_pastes_in_colour_as_a_bitmap_and_keeps_its_png()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Win32 clipboard is Windows only.");
            return;
        }
        // 4 x 2, bottom-up: the bottom row steel blue (70, 130, 180), the top row orange (255, 122, 26).
        var dib = new byte[40 + 4 * 2 * 4];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14), 32);
        for (var i = 0; i < 8; i++)
        {
            var (b, g, r) = i < 4 ? (180, 130, 70) : (26, 122, 255);
            dib[40 + i * 4] = (byte)b;
            dib[40 + i * 4 + 1] = (byte)g;
            dib[40 + i * 4 + 2] = (byte)r;
            dib[40 + i * 4 + 3] = 255;
        }
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 1, 2, 3 };

        Assert.True(new WindowsPlatformServices().SetClipboardPicture(png, dib));

        // Clipboard listeners (Windows' clipboard history among them) open it to read the new picture: wait for them.
        var opened = false;
        for (var attempt = 0; attempt < 40 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(50);
        Assert.True(opened);
        try
        {
            // The bitmap Windows makes from the DIB: in colour (Avalonia's own was one bit a pixel, so it pasted black).
            var bitmap = GetClipboardData(2); // CF_BITMAP
            Assert.NotEqual(0, bitmap);
            Assert.True(GetObjectW(bitmap, Marshal.SizeOf<BITMAP>(), out var info) > 0);
            Assert.True(info.bmBitsPixel >= 24, $"{info.bmBitsPixel} bits a pixel");
            var dc = CreateCompatibleDC(0);
            var old = SelectObject(dc, bitmap);
            Assert.Equal(0x00B48246u, GetPixel(dc, 1, 1)); // bottom row: steel blue, as 0x00BBGGRR
            Assert.Equal(0x001A7AFFu, GetPixel(dc, 1, 0)); // top row: orange
            SelectObject(dc, old);
            DeleteDC(dc);

            var stored = GetClipboardData(RegisterClipboardFormatW("PNG"));
            Assert.NotEqual(0, stored);
            Assert.Equal((nuint)png.Length, GlobalSize(stored));
        }
        finally
        {
            CloseClipboard();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(nint handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint handle);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint dc, int x, int y);
}
