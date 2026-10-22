// Where the capture window's Save and Edit put the area it took: a PNG in Pictures\Bingling. The picture goes to a
// temporary PNG first, as the job and the annotate window take files; Save runs a PNG conversion of it (so its card shows
// in the toast, to see, copy or open), Edit opens the annotate window on it and runs what that saves. The temporary file
// goes once the window has closed and the job is over.

using System.Globalization;
using Avalonia;
using Filee.App.ViewModels;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.Services;

public sealed class CaptureOutput(ConversionService conversions, WindowService windows, ILocalizer loc, ILogger<CaptureOutput> log)
{
    /// <summary>Where the temporary files go, each capture in a folder of its own.</summary>
    internal static string TempRoot => Path.Combine(Path.GetTempPath(), "Bingling", "capture");

    /// <summary>Where captures are saved: Pictures\Bingling (the job makes it). Tests save in a folder of their own.</summary>
    internal string Folder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Bingling");

    /// <summary>Saves <paramref name="picture"/> as a PNG in <see cref="Folder"/>, and lets go of it. Never throws.</summary>
    public Task SaveAsync(SKImage picture) =>
        RunAsync(picture, "save", temp => Task.FromResult(conversions.Start([temp], new Preset { TargetFormat = "png", Output = SavedHere() })));

    /// <summary>
    /// Opens the annotate window on <paramref name="picture"/> near <paramref name="cursor"/> (physical pixels), and lets go
    /// of it; what the window saves goes to <see cref="Folder"/>, its Copy to the clipboard as always. Never throws.
    /// </summary>
    public Task EditAsync(SKImage picture, PixelPoint cursor) =>
        RunAsync(picture, "edit", async temp =>
            await windows.EditImageAsync(ImageToolKind.Annotate, [temp], cursor, loc["capture.saves_to"]) is { } result
                ? conversions.StartTool([temp], new Preset
                {
                    NameKey = "preset.annotate", TargetFormat = "png", Kind = PresetKind.Tool, Tool = ImageToolKind.Annotate, Output = SavedHere(),
                }, result)
                : null);

    /// <summary>Into <see cref="Folder"/>, under the capture's own name ("Capture 2026-10-22 131500.png", then " (2)").</summary>
    private OutputRule SavedHere() => new() { Location = OutputLocation.CustomFolder, CustomFolder = Folder, FileNamePattern = "{name}" };

    /// <summary>Writes <paramref name="picture"/> to a temporary PNG, hands it to <paramref name="run"/> and waits for the job it starts.</summary>
    private async Task RunAsync(SKImage picture, string what, Func<string, Task<JobViewModel?>> run)
    {
        var folder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
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
                await conversions.FinishedAsync(job);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Could not {What} the capture", what);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogInformation(ex, "Could not delete the capture's temporary folder {Folder}", folder);
            }
        }
    }
}
