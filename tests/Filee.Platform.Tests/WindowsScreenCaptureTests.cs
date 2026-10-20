// The Peach Glass donut's backdrop: a GDI capture of the screen where the donut will show.

using Filee.Platform.Windows;

namespace Filee.Platform.Tests;

public sealed class WindowsScreenCaptureTests
{
    [Fact]
    public void The_screen_is_captured_at_the_size_asked_for()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Screen capture through GDI is Windows only.");
            return;
        }
        var capture = new WindowsPlatformServices().CaptureScreen(0, 0, 32, 16);
        if (capture is null && !DesktopDcAvailable())
        {
            Assert.Skip("This session has no desktop to capture (GetDC(0) failed).");
            return;
        }

        // A headless desktop may give a black picture; the size is what matters here.
        Assert.NotNull(capture);
        Assert.Equal(32, capture.Width);
        Assert.Equal(16, capture.Height);
        Assert.Equal(32 * 16 * 4, capture.Bgra.Length);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool DesktopDcAvailable()
    {
        var dc = NativeMethods.GetDC(0);
        if (dc == 0)
            return false;
        NativeMethods.ReleaseDC(0, dc);
        return true;
    }
}
