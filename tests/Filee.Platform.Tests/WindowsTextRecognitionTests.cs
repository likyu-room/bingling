// The redact tool's "find what to hide" on Windows: Windows PowerShell 5.1 reads a picture with the OCR and the face
// detector of Windows, on this PC: also from a folder with quotes, dollars and Hangul in its name, and where the execution
// policy lets no script run; a picture it can't read, a cancel and a time limit end PowerShell and leave no file. Skipped on
// another OS, and where this PC has no text recognition language.

using System.Diagnostics;
using Filee.Core.Platform;
using Filee.Core.Privacy;
using Filee.Platform.Windows;
using SkiaSharp;

namespace Filee.Platform.Tests;

public sealed class WindowsTextRecognitionTests
{
    /// <summary>A white PNG with <paramref name="text"/> in black, large, from (40, 60), in a new folder named <paramref name="folder"/>.</summary>
    private static string Picture(string text, string folder = "ocr")
    {
        var path = Path.Combine(Path.GetTempPath(), "filee-platform-tests", Guid.NewGuid().ToString("N")[..8], folder, "phone.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

    private static void DeletePicture(string path) => Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path))!, recursive: true);

    /// <summary>The result files written since <paramref name="since"/> and left behind.</summary>
    private static IEnumerable<string> LeftSince(DateTime since) => Directory.Exists(RecognitionResult.TempFolder)
        ? new DirectoryInfo(RecognitionResult.TempFolder).EnumerateFiles().Where(f => f.CreationTimeUtc >= since).Select(f => f.Name)
        : [];

    private static bool Running(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // gone
        }
    }

    /// <summary>Reads <paramref name="path"/> and finds the phone number on it; false when this PC has no text recognition language.</summary>
    private static async Task<bool> ReadsThePhoneNumber(string path, IReadOnlyDictionary<string, string>? environment = null, Action<Process>? started = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var clock = Stopwatch.StartNew();
        var read = await WindowsTextRecognition.RecognizeAsync(path, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(15), environment, started);
        clock.Stop();
        Assert.False(read.Failed);
        if (!read.TextAvailable)
            return false;
        TestContext.Current.TestOutputHelper?.WriteLine($"Read in {clock.ElapsedMilliseconds} ms: {string.Join(" / ", read.Lines.Select(l => l.Text))}");

        var phone = Assert.Single(SensitiveFinder.Find(read, 800, 200), s => s.Kind == SensitiveKind.Phone);
        Assert.Contains("010", phone.Text);
        // The number is right of the word before it, on the line drawn at 60 to 108 px.
        Assert.InRange(phone.Box.X, 0.1, 0.4);
        Assert.InRange(phone.Box.Y, 0.15, 0.4);
        Assert.InRange(phone.Box.Y + phone.Box.Height, 0.45, 0.7);
        Assert.InRange(phone.Box.X + phone.Box.Width, 0.5, 0.95);
        return true;
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
        try
        {
            // Through the platform services, as the redact window asks.
            var read = await new WindowsPlatformServices().RecognizeAsync(path, TestContext.Current.CancellationToken);
            Assert.False(read.Failed);
            if (!await ReadsThePhoneNumber(path))
                Assert.Skip("This PC has no text recognition language.");
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Fact]
    public async Task A_picture_in_a_folder_with_quotes_dollars_backticks_spaces_and_Hangul_is_read()
    {
        // Every character PowerShell would read as code in a string, but a double quote, which Windows keeps out of names.
        var path = Picture("전화 010-1234-5678", "odd 'q $x `y [1] 한글 &;%PATH% $(exit 3)");
        try
        {
            if (!await ReadsThePhoneNumber(path))
                Assert.Skip("Needs Windows and a text recognition language.");
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Theory]
    [InlineData("AllSigned")]
    [InlineData("Restricted")]
    public async Task It_runs_where_the_execution_policy_lets_no_script_run(string policy)
    {
        // A group policy forces this on company PCs; to PowerShell's check it looks as this setting for the process does.
        var path = Picture("전화 010-1234-5678");
        var since = DateTime.UtcNow;
        string[] arguments = [];
        try
        {
            if (!await ReadsThePhoneNumber(path, new Dictionary<string, string> { ["PSExecutionPolicyPreference"] = policy }, p => arguments = [.. p.StartInfo.ArgumentList]))
            {
                Assert.Skip("Needs Windows and a text recognition language.");
                return;
            }
            // The commands come on standard input: no script file, and nothing that asks to bypass the policy.
            Assert.DoesNotContain("-File", arguments);
            Assert.DoesNotContain("-ExecutionPolicy", arguments);
            Assert.NotEmpty(arguments);
            Assert.Empty(LeftSince(since));
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_fails_without_throwing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Picture("");
        try
        {
            await File.WriteAllTextAsync(path, "not a picture", TestContext.Current.CancellationToken);

            var read = await new WindowsPlatformServices().RecognizeAsync(path, TestContext.Current.CancellationToken);

            Assert.True(read.Failed);
            Assert.Empty(read.Lines);
            Assert.Empty(read.Faces);
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Fact]
    public async Task Cancelling_stops_PowerShell_and_leaves_no_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Picture("010-1234-5678");
        var since = DateTime.UtcNow;
        var id = 0;
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)); // PowerShell takes longer than this
            var clock = Stopwatch.StartNew();
            var reading = WindowsTextRecognition.RecognizeAsync(path, cancel.Token, TimeSpan.FromSeconds(15), started: p => id = p.Id);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed} to stop");
            Assert.NotEqual(0, id);
            Assert.False(Running(id));
            Assert.Empty(LeftSince(since));
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Fact]
    public async Task Taking_too_long_fails_stops_PowerShell_and_leaves_no_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        var path = Picture("010-1234-5678");
        var since = DateTime.UtcNow;
        var id = 0;
        try
        {
            var read = await WindowsTextRecognition.RecognizeAsync(path, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(150), started: p => id = p.Id);
            Assert.True(read.Failed);
            Assert.Empty(read.Lines);
            Assert.NotEqual(0, id);
            Assert.False(Running(id));
            Assert.Empty(LeftSince(since));
        }
        finally
        {
            DeletePicture(path);
        }
    }

    [Fact]
    public void A_box_the_script_could_not_have_meant_is_no_spot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The text recognition of Windows is Windows only.");
            return;
        }
        // 1e400 reads as infinity, without an error.
        var read = WindowsTextRecognition.Parse("""
            {"text": true, "lines": [{"t": "010-1234-5678", "words": [{"t": "010-1234-5678", "x": 1e400, "y": 10, "w": 40, "h": 10}]}],
             "faces": [{"x": 10, "y": 10, "w": -40, "h": 40}, {"x": 10, "y": 10, "w": 40, "h": 0}]}
            """u8.ToArray());
        Assert.Equal(double.PositiveInfinity, read.Lines[0].Words[0].X);
        Assert.Empty(SensitiveFinder.Find(read, 800, 200));
    }
}
