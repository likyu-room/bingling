// Draws an image tool's preview inside Avalonia's frame with the code that draws the saved picture
// (ImageEditRenderer), on the Skia canvas Avalonia lends a custom draw operation: the preview is the result (spec §3).

using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using SkiaSharp;

namespace Filee.App.Controls;

/// <summary>Draws <see cref="ImageEdit"/> on a preview picture into <c>output</c> (control coordinates), with the renderer.</summary>
/// <param name="output">Where the result goes on the control: the picture's rectangle (with a backdrop, the whole result's).</param>
/// <param name="picture">The preview picture (upright, smaller than the file).</param>
/// <param name="edit">The edit to draw; copied, as the window keeps changing its own while the frame waits to be drawn.</param>
/// <param name="fullShortSide">The file's short side in pixels, which sizes mosaic cells and the layout as on the saved picture.</param>
internal sealed class EditPreviewDrawOperation(Rect output, SKImage picture, ImageEdit edit, int fullShortSide) : ICustomDrawOperation
{
    /// <summary>
    /// Held while a preview is drawn and while a preview picture is let go of: Avalonia draws on its render thread, and
    /// the window may move on to another file (and dispose this one's picture) before a recorded frame is drawn.
    /// </summary>
    // ponytail: one lock for every preview; a reference count per picture if two editor windows ever draw at once.
    private static readonly Lock Gate = new();

    private readonly ImageEdit _edit = edit.Clone();

    public Rect Bounds => output;

    /// <summary>The control under it takes the pointer.</summary>
    public bool HitTest(Point p) => false;

    /// <summary>Never the same: every frame draws the edit as it is then.</summary>
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } skia)
            return;
        using var lease = skia.Lease();
        lock (Gate)
        {
            if (picture.Handle == IntPtr.Zero)
                return; // let go of since this frame was recorded
            var rect = new SKRect((float)output.Left, (float)output.Top, (float)output.Right, (float)output.Bottom);
            ImageEditRenderer.Draw(lease.SkCanvas, picture, rect, _edit, fullShortSide);
        }
    }

    public void Dispose()
    {
    }

    /// <summary>Disposes a preview picture once no frame is drawing it.</summary>
    public static void Release(SKImage? picture)
    {
        if (picture is null)
            return;
        lock (Gate)
            picture.Dispose();
    }
}
