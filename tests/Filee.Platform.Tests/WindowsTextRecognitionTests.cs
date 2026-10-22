// The redact tool's "find what to hide" on Windows: Windows PowerShell 5.1 reads a picture with the OCR and the face
// detector of Windows, on this PC. Skipped on another OS, and where this PC has no text recognition language.

using System.Diagnostics;
using Filee.Core.Platform;
using Filee.Core.Privacy;
using Filee.Platform.Windows;
using SkiaSharp;

namespace Filee.Platform.Tests;

public sealed class WindowsTextRecognitionTests
{
    /// <summary>A white PNG with <paramref name="text"/> in black, large, from (40, 60).</summary>
    private static string Picture(string text)
    {
        var folder = Path.Combine(Path.GetTempPath(), "filee-platform-tests", "ocr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "phone.png");
        using var bitmap = new SKBitmap(800, 200);
        using (var canvas = new SKCanvas(bitmap))
        using (var font = new SKFont(SKTypeface.FromFamilyName("Malgun Gothic"), 48))
        using (var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText(text, 40, 60 + 48, font, ink);
        }
        using (var file = File.Create(path))
            bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
        return path;
    }

    [Fact]
    public async Task A_phone_number_on_a_picture_is_read_with_where_it_is()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Picture("전화 010-1234-5678");
        var clock = Stopwatch.StartNew();
        var read = await new WindowsPlatformServices().RecognizeAsync(path, TestContext.Current.CancellationToken);
        clock.Stop();
        Assert.False(read.Failed);
        if (!read.TextAvailable)
        {
            Assert.Skip("This PC has no text recognition language.");
            return;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"Read in {clock.ElapsedMilliseconds} ms: {string.Join(" / ", read.Lines.Select(l => l.Text))}");

        var phone = Assert.Single(SensitiveFinder.Find(read, 800, 200), s => s.Kind == SensitiveKind.Phone);
        Assert.Contains("010", phone.Text);
        // The number is right of the word before it, on the line drawn at 60 to 108 px.
        Assert.InRange(phone.Box.X, 0.1, 0.4);
        Assert.InRange(phone.Box.Y, 0.15, 0.4);
        Assert.InRange(phone.Box.Y + phone.Box.Height, 0.45, 0.7);
        Assert.InRange(phone.Box.X + phone.Box.Width, 0.5, 0.95);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_fails_without_throwing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Path.Combine(Path.GetTempPath(), "filee-platform-tests", "not-a-picture-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "not a picture", TestContext.Current.CancellationToken);

        var read = await new WindowsPlatformServices().RecognizeAsync(path, TestContext.Current.CancellationToken);

        Assert.True(read.Failed);
        Assert.Empty(read.Lines);
        Assert.Empty(read.Faces);
        File.Delete(path);
    }

    [Fact]
    public async Task Cancelling_stops_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Picture("010-1234-5678");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // PowerShell takes longer to start
        var clock = Stopwatch.StartNew();
        var reading = new WindowsPlatformServices().RecognizeAsync(path, cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed} to stop");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
