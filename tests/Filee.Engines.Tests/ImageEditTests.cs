// The redact and add-background tools end to end (the edit runs through a conversion, as saving does), and the
// renderer's promise to the editor window: a preview drawn small looks like the saved picture made small.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;
using ImageMagick.Drawing;
using SkiaSharp;
using static Filee.Engines.Tests.ImageSamples;

namespace Filee.Engines.Tests;

public class ImageEditTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    /// <summary>The built-in tool preset <paramref name="toolId"/> with <paramref name="edit"/>, as the editor window saves it.</summary>
    private static Preset Edited(string toolId, ImageEdit edit)
    {
        var p = Tool(toolId).Clone();
        p.Image.Edit = edit;
        return p;
    }

    /// <summary>One box to hide (fractions of the picture as it is shown).</summary>
    private static ImageEdit Redact(RedactMode mode, double x, double y, double w, double h) =>
        new() { Redact = new RedactEdit { Mode = mode, Boxes = [new EditRect { X = x, Y = y, Width = w, Height = h }] } };

    private static ImageEdit Backdrop(BackgroundEdit background) => new() { Background = background };

    private async Task<MagickImage> RunAsync(string source, Preset preset)
    {
        var job = await fx.ConvertAsync([source], preset);
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => $"{f.ErrorKey} {f.ErrorDetail}")));
        return new MagickImage(job.Outputs.Single());
    }

    private static (byte R, byte G, byte B, byte A) Rgba(IMagickImage<byte> image, int x, int y) =>
        image.GetPixels().GetPixel(x, y).ToColor() is { } c ? (c.R, c.G, c.B, c.A) : default;

    private static readonly (byte, byte, byte, byte) Sky = (0xBE, 0xE3, 0xFF, 0xFF);

    [Fact]
    public async Task Black_box_turns_every_pixel_in_it_black()
    {
        var source = fx.Write(Noise(400, 300), "n.png");

        using var r = await RunAsync(source, Edited("redact", Redact(RedactMode.Black, .25, .25, .5, .5)));

        Assert.Equal((400u, 300u), (r.Width, r.Height));
        var rgb = r.GetPixels().ToByteArray(PixelMapping.RGB)!;
        for (var y = 75; y < 225; y++)
        {
            for (var x = 100; x < 300; x++)
                Assert.True(rgb[(y * 400 + x) * 3] == 0 && rgb[(y * 400 + x) * 3 + 1] == 0 && rgb[(y * 400 + x) * 3 + 2] == 0, $"({x},{y})");
        }
        using var original = new MagickImage(source);
        var before = original.GetPixels().ToByteArray(PixelMapping.RGB)!;
        Assert.Equal(before.AsSpan(0, 75 * 400 * 3).ToArray(), rgb.AsSpan(0, 75 * 400 * 3).ToArray()); // above the box: untouched
    }

    [Fact]
    public async Task Mosaic_cells_are_uniform_and_at_least_8px()
    {
        // 400 × 300: a cell is max(8, 300 / 40 = 7.5) = 8 px, on a grid from the top left corner.
        var source = fx.Write(Noise(400, 300), "n.png");

        using var r = await RunAsync(source, Edited("redact", Redact(RedactMode.Mosaic, 0, 0, 1, 1)));

        var cell = Rgba(r, 8, 8);
        for (var y = 8; y < 16; y++)
        {
            for (var x = 8; x < 16; x++)
                Assert.Equal(cell, Rgba(r, x, y));
        }
        // Its neighbours are cells of their own (noise averages to another colour), so the cell is 8 px, not more.
        Assert.NotEqual(cell, Rgba(r, 7, 8));
        Assert.NotEqual(cell, Rgba(r, 16, 8));
        Assert.NotEqual(cell, Rgba(r, 8, 16));
    }

    [Fact]
    public void Mosaic_on_a_see_through_picture_shows_nothing_through_its_cells()
    {
        // Half transparent noise: a picture drawn under the half transparent cells would show through them.
        using var picture = new SKBitmap(Info(80, 80));
        var random = new Random(1);
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
                picture.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 128));
        }

        using var r = ImageEditRenderer.Render(picture, Redact(RedactMode.Mosaic, 0, 0, 1, 1));

        var cell = r.GetPixel(8, 8);
        Assert.Equal(128, cell.Alpha);
        for (var y = 8; y < 16; y++)
        {
            for (var x = 8; x < 16; x++)
                Assert.Equal(cell, r.GetPixel(x, y));
        }
    }

    [Fact]
    public async Task Every_frame_of_an_animation_is_redacted()
    {
        var source = Path.Combine(fx.NewFolder(), "anim.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 40, 20) { AnimationDelay = 20 });
            frames.Add(new MagickImage(MagickColors.Lime, 40, 20) { AnimationDelay = 20 });
            frames.Write(source, MagickFormat.Gif);
        }

        var job = await fx.ConvertAsync([source], Edited("redact", Redact(RedactMode.Black, 0, 0, .5, 1)));

        Assert.Equal(JobState.Completed, job.State);
        using var result = new MagickImageCollection(job.Outputs.Single());
        Assert.Equal(2, result.Count);
        Assert.All(result, frame =>
        {
            Assert.Equal((0, 0, 0, 255), Rgba(frame, 5, 10));
            Assert.NotEqual((0, 0, 0, 255), Rgba(frame, 35, 10));
        });
    }

    [Fact]
    public async Task Redacted_output_has_no_exif_and_no_thumbnail()
    {
        var source = Path.Combine(fx.NewFolder(), "camera.jpg");
        using (var image = Marked())
        {
            image.SetProfile(ExifWithThumbnail(1));
            image.SetProfile(ColorProfiles.SRGB);
            image.Write(source, MagickFormat.Jpeg);
        }
        using (var written = new MagickImage(source))
            Assert.True(written.GetExifProfile()!.ThumbnailLength > 0); // the test photo has a preview
        var preset = Edited("redact", Redact(RedactMode.Black, 0, 0, .25, .5));
        preset.Image.KeepMetadata = true; // a redact preset made to keep metadata: the EXIF data still goes

        using var r = await RunAsync(source, preset);

        Assert.Null(r.GetExifProfile());
        Assert.NotNull(r.GetColorProfile()); // the colour profile is no secret: kept, as stripping metadata keeps it
    }

    [Fact]
    public async Task Redact_on_a_turned_phone_photo_hits_the_visible_spot()
    {
        // Stored 40 × 20 with orientation 6: shown 20 × 40 with the red square at the top right.
        using var r = await RunAsync(fx.WritePhone(), Edited("redact", Redact(RedactMode.Black, 0, 0, .5, .5)));

        Assert.Equal((20u, 40u), (r.Width, r.Height));
        var (red, green, blue, _) = Rgba(r, 3, 3);
        Assert.True(red < 64 && green < 64 && blue < 64, $"({red},{green},{blue})"); // the visible top left (a JPEG: near black)
        Assert.True(IsRed(r, 18, 1)); // the red square next to it is still there
        Assert.True(r.Quality <= 90, $"quality {r.Quality}"); // still a JPEG to the encoder: saved at its own quality, not 95
    }

    [Fact]
    public async Task Auto_background_adds_padding_on_every_side()
    {
        // 200 × 100 at 10 %: p = 0.10 × 100 = 10 px on each side.
        var source = fx.Write(new MagickImage(MagickColors.Red, 200, 100), "logo.jpg");
        var edit = Backdrop(new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#BEE3FF", Padding = .1, Shadow = 0 });

        var job = await fx.ConvertAsync([source], Edited("add-background", edit));

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("logo with background.png", Path.GetFileName(job.Outputs.Single()));
        using var r = new MagickImage(job.Outputs.Single());
        Assert.Equal(MagickFormat.Png, r.Format);
        Assert.Equal((220u, 120u), (r.Width, r.Height));
        foreach (var (x, y) in new[] { (5, 60), (214, 60), (110, 5), (110, 114) })
            Assert.Equal(Sky, Rgba(r, x, y));
        Assert.True(IsRed(r, 110, 60));
    }

    [Fact]
    public async Task Fixed_ratio_background_is_exactly_16_by_9()
    {
        // 200 × 100 with 8 % (8 px) is 216 × 116; the smallest 16:9 canvas holding it is 16·14 × 9·14.
        var source = fx.Write(new MagickImage(MagickColors.Red, 200, 100), "wide.png");

        using var r = await RunAsync(source, Edited("add-background", Backdrop(new BackgroundEdit { Aspect = "16:9" })));

        Assert.Equal((224u, 126u), (r.Width, r.Height));
        Assert.True(IsRed(r, 112, 63)); // in the middle
    }

    [Fact]
    public async Task Outside_a_rounded_corner_is_background()
    {
        // The picture sits at (10, 10)-(210, 110) with corners of 0.25 × 100 = 25 px.
        var source = fx.Write(new MagickImage(MagickColors.Red, 200, 100), "card.png");
        var edit = Backdrop(new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#BEE3FF", Padding = .1, Corner = .25, Shadow = 0 });

        using var r = await RunAsync(source, Edited("add-background", edit));

        Assert.Equal(Sky, Rgba(r, 11, 11));
        Assert.Equal(Sky, Rgba(r, 208, 108));
        Assert.True(IsRed(r, 35, 11)); // past the curve, along the top edge
    }

    [Fact]
    public async Task Transparent_logo_shows_the_background_through()
    {
        // 100 × 100, transparent but for a red disc in the middle; 8 px of padding.
        var logo = new MagickImage(MagickColors.Transparent, 100, 100);
        new Drawables().FillColor(MagickColors.Red).Circle(50, 50, 70, 50).Draw(logo);
        var source = fx.Write(logo, "logo.png");

        using var r = await RunAsync(source, Edited("add-background", Backdrop(new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#BEE3FF" })));

        Assert.Equal(Sky, Rgba(r, 8 + 10, 8 + 50)); // was transparent, under the default shadow's reach too
        Assert.True(IsRed(r, 58, 58));
    }

    [Fact]
    public async Task A_grey_picture_gets_a_colour_backdrop()
    {
        // ImageMagick keeps a grey picture in one channel: drawn on as such, the gradient would come out grey too.
        var grey = new MagickImage(MagickColors.Gray, 100, 100);
        grey.ColorSpace = ColorSpace.Gray;
        var source = fx.Write(grey, "scan.png");

        using var r = await RunAsync(source, Edited("add-background", Backdrop(new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#BEE3FF" })));

        Assert.Equal(Sky, Rgba(r, 2, 2));
    }

    [Fact]
    public void Backdrops_fill_the_whole_canvas()
    {
        using var picture = Filled(20, 20, SKColors.Red);
        var background = new BackgroundEdit { Shadow = 0 }; // the "peach" gradient, 135°: start top left, end bottom right
        using (var gradient = ImageEditRenderer.Render(picture, Backdrop(background)))
        {
            AssertNear(new SKColor(0xFF, 0xD3, 0xA5), gradient.GetPixel(0, 0));
            AssertNear(new SKColor(0xFD, 0x65, 0x85), gradient.GetPixel(gradient.Width - 1, gradient.Height - 1));
        }

        background.Kind = BackgroundKind.Image;
        background.ImagePath = fx.Write(new MagickImage(MagickColors.Lime, 30, 10), "backdrop.png");
        using (var filled = ImageEditRenderer.Render(picture, Backdrop(background)))
            Assert.Equal(SKColors.Lime, filled.GetPixel(0, 0));

        background.ImagePath = Path.Combine(fx.NewFolder(), "gone.png");
        using (var white = ImageEditRenderer.Render(picture, Backdrop(background)))
            Assert.Equal(SKColors.White, white.GetPixel(0, 0));
    }

    [Fact]
    public void Preview_scale_matches_full_size()
    {
        // The editor window draws a small copy of the picture; saving draws the original. Both must look the same.
        using var full = Scene(800, 600);
        var edit = new ImageEdit
        {
            Redact = new RedactEdit { Mode = RedactMode.Mosaic, Boxes = [new EditRect { X = .1, Y = .2, Width = .4, Height = .3 }] },
            Background = new BackgroundEdit { Value = "sky", Padding = .08, Corner = .05, Shadow = 60, Aspect = "1:1" },
        };
        using var saved = ImageEditRenderer.Render(full, edit);
        Assert.Equal((896, 896), (saved.Width, saved.Height)); // 800 × 600 and 48 px each side, made square
        var smaller = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var expected = saved.Resize(Info(224, 224), smaller);

        using var small = full.Resize(Info(200, 150), smaller);
        using var smallImage = SKImage.FromBitmap(small);
        using var preview = new SKBitmap(Info(224, 224));
        using (var canvas = new SKCanvas(preview))
        {
            canvas.Clear(SKColors.Transparent);
            ImageEditRenderer.Draw(canvas, smallImage, SKRect.Create(224, 224), edit, fullShortSide: 600);
        }

        var a = expected.GetPixelSpan();
        var b = preview.GetPixelSpan();
        var sums = new long[4];
        for (var i = 0; i < a.Length; i++)
            sums[i % 4] += Math.Abs(a[i] - b[i]);
        var pixels = a.Length / 4.0;
        Assert.All(sums, sum => Assert.True(sum / pixels <= 6, $"mean difference {sum / pixels:F2}"));
    }

    private static SKImageInfo Info(int width, int height) => new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    private static SKBitmap Filled(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(Info(width, height));
        bitmap.Erase(color);
        return bitmap;
    }

    /// <summary>A picture with smooth colour and a few hard edges: a diagonal gradient, a disc and a bar.</summary>
    private static SKBitmap Scene(int width, int height)
    {
        var bitmap = new SKBitmap(Info(width, height));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            [SKColors.Orange, SKColors.Teal], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, width, height, paint);
        paint.Shader = null;
        paint.Color = SKColors.Navy;
        canvas.DrawCircle(width * .7f, height * .5f, height * .25f, paint);
        paint.Color = SKColors.White;
        canvas.DrawRect(width * .1f, height * .7f, width * .5f, height * .1f, paint);
        return bitmap;
    }

    private static void AssertNear(SKColor expected, SKColor actual) =>
        Assert.True(Math.Abs(expected.Red - actual.Red) <= 3 && Math.Abs(expected.Green - actual.Green) <= 3
            && Math.Abs(expected.Blue - actual.Blue) <= 3, $"{actual} is not {expected}");
}
