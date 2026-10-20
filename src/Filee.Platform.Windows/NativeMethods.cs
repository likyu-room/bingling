// Win32 P/Invoke declarations used by the Windows platform layer. Keep this list minimal.

using System.Runtime.InteropServices;

namespace Filee.Platform.Windows;

internal static partial class NativeMethods
{
    public const int SM_CXDRAG = 68;
    public const uint GA_ROOT = 2;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    public const uint ASFW_ANY = uint.MaxValue;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    public const nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassName(nint hwnd, [Out] char[] className, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSize(nint process, nint minimumSize, nint maximumSize);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    // Package queries (appmodel.h, Windows 8+ / PackagePathType_EffectiveExternal: Windows 10 2004+).
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int PackagePathType_EffectiveExternal = 5;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public unsafe struct PACKAGE_ID
    {
        public uint Reserved;
        public uint ProcessorArchitecture;
        public ulong Version;
        public char* Name;
        public char* Publisher;
        public char* ResourceId;
        public char* PublisherId;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, char** packageFullNames, ref uint bufferLength, char* buffer);

    // Not exported by kernel32.dll (only by kernelbase.dll): import it from its API set, Windows 10 1903 and later.
    [LibraryImport("api-ms-win-appmodel-runtime-l1-1-3.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetPackagePathByFullName2(string packageFullName, int packagePathType, ref uint pathLength, char* path);

    [LibraryImport("kernel32.dll")]
    public static unsafe partial int PackageFamilyNameFromId(PACKAGE_ID* packageId, ref uint packageFamilyNameLength, char* packageFamilyName);

    // Tokens and integrity levels (Elevation).
    public const uint TOKEN_QUERY = 0x0008;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int TokenElevation = 20;
    public const int TokenIntegrityLevel = 25;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetTokenInformation(nint token, int informationClass, void* information, uint length, out uint returnLength);

    /// <summary>Pointer to the sub-authority count (a byte) inside a SID.</summary>
    [LibraryImport("advapi32.dll")]
    public static partial nint GetSidSubAuthorityCount(nint sid);

    /// <summary>Pointer to sub-authority <paramref name="index"/> (a DWORD) inside a SID.</summary>
    [LibraryImport("advapi32.dll")]
    public static partial nint GetSidSubAuthority(nint sid, uint index);

    /// <summary>The desktop window of the shell (Explorer's Progman), or 0 when no shell runs.</summary>
    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    // GDI screen capture (the Peach Glass backdrop). SRCCOPY only: CAPTUREBLT would also take layered windows, such as
    // the picture Explorer drags along under the cursor.
    public const uint SRCCOPY = 0x00CC0020;
    public const uint BI_RGB = 0, DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    /// <summary>A bitmap whose pixels are at <paramref name="bits"/> (the header stands for a BITMAPINFO without colours).</summary>
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint dc, in BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    public const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint owner, string text, string caption, uint type);

    /// <summary>Returns the Win32 class name of a window ("CabinetWClass" for Explorer, ...).</summary>
    public static string ClassNameOf(nint hwnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }
}
