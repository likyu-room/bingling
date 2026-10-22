// The redact tool's "find what to hide" on Windows: the text (Windows.Media.Ocr, in the languages of the user's profile)
// and the faces (Windows.Media.FaceAnalysis) on a picture, read on this PC. .NET 10 reaches these WinRT APIs only through
// the Windows SDK projection, which Bingling does not ship; Windows PowerShell 5.1 runs on .NET Framework, which reaches
// them as they are. So a short script is written to a temporary .ps1 and run with -File, the picture and the result file
// as its arguments (never typed into the script, so no character of a path can change what it runs). It writes what it
// read to a UTF-8 JSON file, which the console's code page can't garble.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Filee.Core;
using Filee.Core.Platform;

namespace Filee.Platform.Windows;

/// <summary>Reads text and faces on a picture through Windows PowerShell 5.1.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsTextRecognition
{
    /// <summary>The OCR takes about half a second with PowerShell's start; after this the attempt counts as failed.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly RecognitionResult Failed = RecognitionResult.Empty with { Failed = true };

    /// <summary>Where the script and its result go while it runs; both are deleted after.</summary>
    private static string Folder => Path.Combine(Path.GetTempPath(), AppIdentity.Folder, "ocr");

    /// <summary>
    /// What is on the picture at <paramref name="imagePath"/> (any picture Windows decodes; its EXIF orientation is not
    /// applied, positions are in its pixels as stored). <see cref="RecognitionResult.Failed"/> when PowerShell could not
    /// read it or took longer than <see cref="Timeout"/>; <paramref name="ct"/> stops PowerShell and throws.
    /// </summary>
    public static async Task<RecognitionResult> RecognizeAsync(string imagePath, CancellationToken ct)
    {
        var name = Guid.NewGuid().ToString("N");
        var script = Path.Combine(Folder, name + ".ps1");
        var output = Path.Combine(Folder, name + ".json");
        try
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(script, Script, ct);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in (string[])["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, Path.GetFullPath(imagePath), output])
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000); // so the files it holds can be deleted below
                ct.ThrowIfCancellationRequested();
                Trace.TraceWarning($"Text recognition did not finish within {Timeout.TotalSeconds} seconds");
                return Failed;
            }
            if (process.ExitCode != 0)
            {
                Trace.TraceWarning($"Text recognition failed: {(File.Exists(output) ? await File.ReadAllTextAsync(output, ct) : $"exit code {process.ExitCode}")}");
                return Failed;
            }
            return Parse(await File.ReadAllBytesAsync(output, ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException
            or JsonException or KeyNotFoundException)
        {
            Trace.TraceWarning($"Text recognition failed: {ex}");
            return Failed;
        }
        finally
        {
            Delete(script);
            Delete(output);
        }
    }

    /// <summary>The script's JSON: <c>{"text": bool, "lines": [{"t", "words": [{"t", "x", "y", "w", "h"}]}], "faces": [{"x", "y", "w", "h"}]}</c>.</summary>
    internal static RecognitionResult Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        static double N(JsonElement e, string name) => e.GetProperty(name).GetDouble();
        return new RecognitionResult(
            [.. root.GetProperty("lines").EnumerateArray().Select(line => new RecognizedLine(line.GetProperty("t").GetString() ?? "",
                [.. line.GetProperty("words").EnumerateArray().Select(w => new RecognizedWord(w.GetProperty("t").GetString() ?? "", N(w, "x"), N(w, "y"), N(w, "w"), N(w, "h")))]))],
            [.. root.GetProperty("faces").EnumerateArray().Select(f => new PixelBox(N(f, "x"), N(f, "y"), N(f, "w"), N(f, "h")))],
            root.GetProperty("text").GetBoolean());
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Reads the picture (its first argument) and writes the JSON to its second; on an error, the message there instead and
    /// exit code 1. A picture larger than the OCR takes is read smaller, and the boxes are scaled back to its own pixels.
    /// Faces are looked for in grey, which is what the detector takes; without a recognition language, faces only.
    /// </summary>
    private const string Script = """
        param([string]$Image, [string]$Out)
        $ErrorActionPreference = 'Stop'
        try {
            Add-Type -AssemblyName System.Runtime.WindowsRuntime
            $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
            $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
            $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
            $null = [Windows.Media.FaceAnalysis.FaceDetector, Windows.Media, ContentType = WindowsRuntime]
            $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
                $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
                Select-Object -First 1
            function Await($op, [Type]$type) {
                $task = $asTask.MakeGenericMethod($type).Invoke($null, @($op))
                $task.Wait() | Out-Null
                $task.Result
            }

            $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Image)) ([Windows.Storage.StorageFile])
            $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
            try {
                $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
                $transform = New-Object Windows.Graphics.Imaging.BitmapTransform
                $longest = [Math]::Max($decoder.PixelWidth, $decoder.PixelHeight)
                $max = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
                if ($longest -gt $max) {
                    $transform.ScaledWidth = [uint32][Math]::Floor($decoder.PixelWidth * $max / $longest)
                    $transform.ScaledHeight = [uint32][Math]::Floor($decoder.PixelHeight * $max / $longest)
                }
                $bitmap = Await ($decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8,
                    [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied, $transform,
                    [Windows.Graphics.Imaging.ExifOrientationMode]::IgnoreExifOrientation,
                    [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage)) ([Windows.Graphics.Imaging.SoftwareBitmap])
                $sx = $decoder.PixelWidth / $bitmap.PixelWidth
                $sy = $decoder.PixelHeight / $bitmap.PixelHeight
            } finally {
                $stream.Dispose()
            }
            function Box($r) { @{ x = $r.X * $sx; y = $r.Y * $sy; w = $r.Width * $sx; h = $r.Height * $sy } }

            $lines = New-Object System.Collections.ArrayList
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
            if ($null -ne $engine) {
                $read = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
                foreach ($line in $read.Lines) {
                    $words = New-Object System.Collections.ArrayList
                    foreach ($word in $line.Words) {
                        $box = Box $word.BoundingRect
                        $box.t = $word.Text
                        [void]$words.Add($box)
                    }
                    [void]$lines.Add(@{ t = $line.Text; words = $words })
                }
            }

            $faces = New-Object System.Collections.ArrayList
            if ([Windows.Media.FaceAnalysis.FaceDetector]::IsSupported) {
                $gray = [Windows.Graphics.Imaging.SoftwareBitmap]::Convert($bitmap, [Windows.Graphics.Imaging.BitmapPixelFormat]::Gray8)
                $detector = Await ([Windows.Media.FaceAnalysis.FaceDetector]::CreateAsync()) ([Windows.Media.FaceAnalysis.FaceDetector])
                $found = Await ($detector.DetectFacesAsync($gray)) ([System.Collections.Generic.IList[Windows.Media.FaceAnalysis.DetectedFace]])
                foreach ($face in $found) { [void]$faces.Add((Box $face.FaceBox)) }
            }

            $json = ConvertTo-Json -InputObject @{ text = ($null -ne $engine); lines = $lines; faces = $faces } -Depth 6 -Compress
            [IO.File]::WriteAllText($Out, $json, (New-Object Text.UTF8Encoding $false))
        } catch {
            [IO.File]::WriteAllText($Out, $_.ToString(), (New-Object Text.UTF8Encoding $false))
            exit 1
        }
        """;
}
