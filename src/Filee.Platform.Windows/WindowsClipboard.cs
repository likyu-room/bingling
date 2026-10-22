// Puts a picture on the Win32 clipboard as a DIB and a PNG. Avalonia's own clipboard (12.1) also offers the bitmap
// (CF_BITMAP) it makes, and that one is one bit a pixel: KakaoTalk, Paint and every app that pastes the bitmap showed
// black. Without it Windows makes the bitmap from the DIB, in colour.

using System.Runtime.InteropServices;

namespace Filee.Platform.Windows;

internal static class WindowsClipboard
{
    private static readonly uint PngFormat = NativeMethods.RegisterClipboardFormat("PNG");

    /// <summary>The clipboard's owner: a message-only window (EmptyClipboard with none makes SetClipboardData fail).</summary>
    private static nint _owner;

    /// <summary>
    /// Replaces what is on the clipboard with <paramref name="dib"/> (CF_DIB) and <paramref name="png"/> ("PNG"). Waits up
    /// to half a second for another app that holds the clipboard. True when the DIB is there.
    /// </summary>
    public static bool SetPicture(byte[] png, byte[] dib)
    {
        if (_owner == 0)
            _owner = NativeMethods.CreateWindowEx(0, "STATIC", "Bingling clipboard", 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, 0, 0);
        if (_owner == 0)
            return false;
        for (var attempt = 0; !NativeMethods.OpenClipboard(_owner); attempt++)
        {
            if (attempt == 10)
                return false;
            Thread.Sleep(50);
        }
        try
        {
            if (!NativeMethods.EmptyClipboard() || !Put(NativeMethods.CF_DIB, dib))
                return false;
            Put(PngFormat, png); // the bitmap pastes without it; an app that wants the PNG gets the DIB then
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>Hands <paramref name="data"/> to the clipboard, which owns the memory from then on.</summary>
    private static bool Put(uint format, byte[] data)
    {
        var memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);
        if (memory == 0)
            return false;
        var at = NativeMethods.GlobalLock(memory);
        if (at == 0)
        {
            NativeMethods.GlobalFree(memory);
            return false;
        }
        Marshal.Copy(data, 0, at, data.Length);
        NativeMethods.GlobalUnlock(memory);
        if (NativeMethods.SetClipboardData(format, memory) != 0)
            return true;
        NativeMethods.GlobalFree(memory);
        return false;
    }
}
