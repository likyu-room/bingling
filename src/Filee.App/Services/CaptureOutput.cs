// Where the capture window's Save, Edit and Redact put the area it took: a PNG in Pictures\Bingling. The picture goes to a
// temporary PNG first, as the job and the annotate and redact windows take files; Save runs a PNG conversion of it (so its
// card shows in the toast, to see, copy or open), Edit and Redact open their window on it and run what that saves. The
// temporary file goes once the window has closed and the job has completed; a job that failed keeps it for its Retry, for
// a day.

using System.Globalization;
using Avalonia;
using Filee.App.ViewModels;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.Services;

public sealed class CaptureOutput(ConversionService conversions, WindowService windows, ILocalizer loc, ILogger<CaptureOutput> log)
{
    /// <summary>Where the temporary files go, each capture in a folder of its own.</summary>
    internal static string TempRoot => Path.Combine(Path.GetTempPath(), "Bingling", "capture");

    /// <summary>How long a capture whose job did not complete stays in <see cref="TempRoot"/>, for its card's Retry.</summary>
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    /// <summary>
    /// Deletes the capture folders in <paramref name="root"/> last written over a day before <paramref name="now"/> (UTC):
    /// those of jobs that failed, and of an annotate window still open when Bingling ended. Never throws.
    /// </summary>
    internal static void DeleteOld(string root, DateTime now)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var folder in new DirectoryInfo(root).EnumerateDirectories())
            try
            {
                if (now - folder.LastWriteTimeUtc > KeptFor)
                    folder.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use: the next start tries again.
            }
    }

    /// <summary>Where captures are saved: Pictures\Bingling (the job makes it). Tests save in a folder of their own.</summary>
    internal string Folder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Bingling");

    /// <summary>Saves <paramref name="picture"/> as a PNG in <see cref="Folder"/>, and lets go of it. Never throws.</summary>
    public Task SaveAsync(SKImage picture) =>
        RunAsync(picture, "save", temp => Task.FromResult(conversions.Start([temp], new Preset { TargetFormat = "png", Output = SavedHere() })));

    /// <summary>
    /// Opens the window of <paramref name="tool"/> (annotate, or redact) on <paramref name="picture"/> near
    /// <paramref name="cursor"/> (physical pixels), and lets go of it; what the window saves goes to <see cref="Folder"/>,
    /// its Copy to the clipboard as always. Never throws.
    /// </summary>
    public Task EditAsync(SKImage picture, PixelPoint cursor, ImageToolKind tool) =>
        RunAsync(picture, "edit", async temp =>
            await windows.EditImageAsync(tool, [temp], cursor, loc["capture.saves_to"]) is { } result
                ? conversions.StartTool([temp], new Preset
                {
                    NameKey = "preset." + tool.ToString().ToLowerInvariant(), TargetFormat = "png", Kind = PresetKind.Tool, Tool = tool, Output = SavedHere(),
                }, result)
                : null);

    /// <summary>Into <see cref="Folder"/>, under the capture's own name ("Capture 2026-10-22 131500.png", then " (2)").</summary>
    private OutputRule SavedHere() => new() { Location = OutputLocation.CustomFolder, CustomFolder = Folder, FileNamePattern = "{name}" };

    /// <summary>
    /// Writes <paramref name="picture"/> to a temporary PNG, hands it to <paramref name="run"/> and waits for the job it starts.
    /// The PNG goes once that job has completed, or when there was none; a job that did not complete keeps it for its
    /// Retry (<see cref="DeleteOld"/> clears it a day on). A capture that could not be handed on says so.
    /// </summary>
    private async Task RunAsync(SKImage picture, string what, Func<string, Task<JobViewModel?>> run)
    {
        var folder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
        var keep = false;
        try
        {
            var name = loc.Format("capture.file_name", DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture));
            var temp = Path.Combine(folder, name + ".png");
            using (picture)
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    using var png = picture.Encode(SKEncodedImageFormat.Png, 100);
                    using var file = File.Create(temp);
                    png.SaveTo(file);
                });
            if (await run(temp) is { } job)
                keep = (await conversions.FinishedAsync(job)).State != JobState.Completed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Could not {What} the capture", what);
            _ = windows.MessageAsync(null, loc.Format("capture.save_failed", ex.Message));
        }
        finally
        {
            try
            {
                if (!keep && Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogInformation(ex, "Could not delete the capture's temporary folder {Folder}", folder);
            }
        }
    }
}
