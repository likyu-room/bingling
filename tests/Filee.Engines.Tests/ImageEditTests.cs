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
    public void Mosaic_cells_at_box_edges_off_the_grid_are_never_thinner_than_a_cell()
    {
        // 400 × 300: cells of 8 px on a grid from the top left corner. The first box runs from x 3 to 24 and from y 5 to the
        // bottom edge (300 = 37 × 8 + 4): cut at the grid lines alone, its first column would be 5 px wide, its first row
        // 3 px and its last 4, and its corner 5 × 3 px of nearly the picture itself. The second, 6 × 6 px across the grid
        // lines at x 200 and y 104, is smaller than a cell: one cell.
        using var picture = new SKBitmap(Info(400, 300));
        var random = new Random(2);
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 400; x++)
                picture.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        }
        var edit = new ImageEdit
        {
            Redact = new RedactEdit
            {
                Mode = RedactMode.Mosaic,
                Boxes =
                [
                    new EditRect { X = 3 / 400.0, Y = 5 / 300.0, Width = 21 / 400.0, Height = 295 / 300.0 },
                    new EditRect { X = 196 / 400.0, Y = 100 / 300.0, Width = 6 / 400.0, Height = 6 / 300.0 },
                ],
            },
        };

        using var r = ImageEditRenderer.Render(picture, edit);

        // A run of one colour along a row or a column of the box is a cell's side (noise averages differ cell to cell).
        for (var y = 5; y < 300; y++)
            Assert.All(Runs(Region(r, 3, y, 21, 1)), run => Assert.True(run >= 8, $"a cell {run} px wide in row {y}"));
        for (var x = 3; x < 24; x++)
            Assert.All(Runs(Region(r, x, 5, 1, 295)), run => Assert.True(run >= 8, $"a cell {run} px high in column {x}"));
        Assert.Single(Region(r, 196, 100, 6, 6).Distinct());
    }

    /// <summary>The lengths of the runs of one colour along <paramref name="line"/>, in order.</summary>
    private static List<int> Runs(SKColor[] line)
    {
        var runs = new List<int> { 1 };
        for (var i = 1; i < line.Length; i++)
        {
            if (line[i] == line[i - 1])
                runs[^1]++;
            else
                runs.Add(1);
        }
        return runs;
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

    /// <summary>Black lettering on white, big enough that a cell of 8 px still holds a stroke of it: what a blur is asked to hide.</summary>
    private static SKBitmap Lettering(int width, int height)
    {
        var bitmap = Filled(width, height, SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        using var font = AnnotationFonts.Create(height * .075f);
        string[] lines = ["Filee 1234 5678", "가나다라 마바사 아자차", "kim@mail.com 010-1234-5678", "900101-1234567 서울시 강남구", "ACCOUNT 110-234-567890", "Filee 가나다 1234"];
        for (var i = 0; i < lines.Length; i++)
            canvas.DrawText(lines[i], width * .05f, height * (.15f + .14f * i), font, paint);
        return bitmap;
    }

    /// <summary>The picture with <paramref name="box"/> of it blurred as it stands, the way the blur would do it without a mosaic under it.</summary>
    private static SKBitmap BlurredDirectly(SKBitmap picture, SKRectI box, float sigma)
    {
        var result = picture.Copy();
        using var image = SKImage.FromBitmap(picture);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) };
        canvas.ClipRect(SKRect.Create(box.Left, box.Top, box.Width, box.Height));
        canvas.SaveLayer(paint);
        canvas.DrawImage(image, 0, 0);
        canvas.Restore();
        return result;
    }

    /// <summary>The mean absolute difference per channel between two pictures of one size, over <paramref name="area"/> of them.</summary>
    private static double MeanDifference(SKBitmap a, SKBitmap b, SKRectI area)
    {
        long sum = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue) + Math.Abs(p.Alpha - q.Alpha);
            }
        }
        return sum / (4.0 * area.Width * area.Height);
    }

    /// <summary>The share of <paramref name="area"/> in which the two pictures differ by more than <paramref name="levels"/> (0 to 255) on average over the channels.</summary>
    private static double Differing(SKBitmap a, SKBitmap b, SKRectI area, int levels)
    {
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                if (Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue) > 3 * levels)
                    count++;
            }
        }
        return (double)count / (area.Width * area.Height);
    }

    /// <summary>The mean brightness (0 to 255) of <paramref name="area"/> of an opaque picture.</summary>
    private static double MeanLuma(SKBitmap picture, SKRectI area)
    {
        double sum = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var c = picture.GetPixel(x, y);
                sum += 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
            }
        }
        return sum / (area.Width * area.Height);
    }

    [Fact]
    public void Blur_softens_the_mosaic_of_the_box_so_nothing_finer_than_a_cell_is_left()
    {
        // 400 × 300: a cell is 8 px, so the blur is 0.6 × 8 = 4.8. The box is x 40 to 360 and y 30 to 270.
        var box = new SKRectI(40, 30, 360, 270);
        using var picture = Lettering(400, 300);
        var edit = Redact(RedactMode.Blur, .1, .1, .8, .8);

        using var blurred = ImageEditRenderer.Render(picture, edit);

        Assert.True(Differing(blurred, picture, box, 8) > 0.2, "the box still shows the picture");
        // Not a black box, and as bright as the picture it hides: blurring cells keeps their average.
        Assert.InRange(MeanLuma(blurred, box), MeanLuma(picture, box) - 8, MeanLuma(picture, box) + 8);
        // Not the mosaic left as it is either.
        using var mosaic = ImageEditRenderer.Render(picture, Redact(RedactMode.Mosaic, .1, .1, .8, .8));
        Assert.True(Differing(blurred, mosaic, box, 4) > 0.2, "the cells are not blurred");
        // Not the picture blurred where it stood: the mosaic comes first, so the blur works on cells.
        using var direct = BlurredDirectly(picture, box, 4.8f);
        Assert.True(Differing(blurred, direct, box, 4) > 0.1, "the box is the picture blurred, with no mosaic under it");

        // The proof of "nothing finer than a cell": the mosaic of the picture, hidden again, comes out the same, while a
        // blur of the picture itself tells the two apart (the lettering inside a cell).
        using var again = ImageEditRenderer.Render(mosaic, edit);
        Assert.True(MeanDifference(blurred, again, box) <= 0.5, "what the blur works on is not the mosaic");
        // And it is that mosaic blurred by 0.6 of a cell (4.8 px); a blur of 4.2 or 5.4 differs by 0.4 or 0.8 already. Taken
        // away from the edge, where the box has to stop the blur and this reference does not.
        using var reference = BlurredDirectly(mosaic, box, 4.8f);
        Assert.True(MeanDifference(blurred, reference, new SKRectI(56, 46, 344, 254)) <= 0.05, "not the mosaic blurred by 0.6 of a cell");

        // The blur stays in the box: it neither fades towards see-through at the edge nor leaks out of it.
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 400; x++)
            {
                if (box.Contains(x, y))
                    Assert.True(blurred.GetPixel(x, y).Alpha == 255, $"({x},{y}) is see-through");
                else
                    Assert.True(blurred.GetPixel(x, y) == picture.GetPixel(x, y), $"({x},{y}) changed outside the box");
            }
        }
    }

    [Fact]
    public void Blur_preview_looks_like_the_saved_picture_made_small()
    {
        // 800 × 600: a cell is 15 px and the blur 9; the preview is a quarter, a cell 3.75 px and the blur 2.25.
        using var full = Lettering(800, 600);
        var edit = Redact(RedactMode.Blur, .1, .2, .4, .3);
        using var quarter = AtQuarterScale(full, edit);

        Assert.Equal((200, 150), (quarter.Saved.Width, quarter.Saved.Height));
        var area = new SKRectI(20, 30, 100, 75); // the box at that size
        var difference = MeanDifference(quarter.Saved, quarter.Drawn, area);
        Assert.True(difference <= 6, $"mean difference {difference:F2} in the box");
        // And the comparison is between two blurs of cells, not two black boxes or two pictures of lettering.
        using var small = full.Resize(Info(200, 150), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        Assert.True(MeanDifference(quarter.Drawn, small, area) > 5);
        Assert.InRange(MeanLuma(quarter.Drawn, area), MeanLuma(small, area) - 8, MeanLuma(small, area) + 8);
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
    public void A_save_lets_its_backdrop_picture_go_and_the_preview_keeps_its_own()
    {
        // A save reads the user's picture at full size (up to hundreds of MB): kept, it would stay until another is picked.
        using var picture = Filled(20, 20, SKColors.Red);
        var path = fx.Write(new MagickImage(MagickColors.Lime, 30, 10), "wall.png");
        var edit = Backdrop(new BackgroundEdit { Kind = BackgroundKind.Image, ImagePath = path, Shadow = 0 });

        using (var saved = ImageEditRenderer.Render(picture, edit))
            Assert.Equal(SKColors.Lime, saved.GetPixel(0, 0));
        Assert.NotEqual(path, ImageEditRenderer.CachedBackdropPath);

        // The preview draws on every frame, from a small copy: that one is kept.
        using var image = SKImage.FromBitmap(picture);
        using var preview = new SKBitmap(Info(30, 30));
        using (var canvas = new SKCanvas(preview))
            ImageEditRenderer.Draw(canvas, image, SKRect.Create(30, 30), edit, 0);
        Assert.Equal(SKColors.Lime, preview.GetPixel(0, 0));
        Assert.Equal(path, ImageEditRenderer.CachedBackdropPath);
    }

    [Theory]
    [InlineData(RedactMode.Mosaic)]
    [InlineData(RedactMode.Blur)]
    public void Preview_scale_matches_full_size(RedactMode mode)
    {
        // The editor window draws a small copy of the picture; saving draws the original. Both must look the same.
        using var full = Scene(800, 600);
        var edit = new ImageEdit
        {
            Redact = new RedactEdit { Mode = mode, Boxes = [new EditRect { X = .1, Y = .2, Width = .4, Height = .3 }] },
            Background = new BackgroundEdit { Value = "sky", Padding = .08, Corner = .05, Shadow = 60, Aspect = "1:1" },
        };
        using var quarter = AtQuarterScale(full, edit);

        Assert.Equal((224, 224), (quarter.Saved.Width, quarter.Saved.Height)); // 800 × 600 and 48 px each side, made square, a quarter
        Assert.All(MeanDifference(quarter), mean => Assert.True(mean <= 6, $"mean difference {mean:F2}"));
    }

    [Fact]
    public void Preview_scale_matches_full_size_with_annotations()
    {
        using var full = Scene(800, 600);
        var plain = new ImageEdit
        {
            Redact = new RedactEdit { Mode = RedactMode.Mosaic, Boxes = [new EditRect { X = .1, Y = .2, Width = .4, Height = .3 }] },
            Background = new BackgroundEdit { Value = "sky", Padding = .08, Corner = .05, Shadow = 60, Aspect = "1:1" },
        };
        var edit = plain.Clone();
        edit.Annotations =
        [
            Note(AnnotationKind.Pen, "#FF3B30", StrokeLevel.Thick, [(.08, .1), (.2, .25), (.15, .4), (.3, .45)]),
            Note(AnnotationKind.Highlighter, "#FFCC00", StrokeLevel.Normal, [(.4, .1), (.9, .12)]),
            Note(AnnotationKind.Arrow, "#007AFF", StrokeLevel.Thick, [(.1, .9), (.45, .6)]),
            Note(AnnotationKind.Line, "#000000", StrokeLevel.Thick, [(.5, .9), (.95, .95)]),
            Note(AnnotationKind.Rectangle, "#34C759", StrokeLevel.Thick, [(.55, .3), (.9, .55)]),
            Note(AnnotationKind.Ellipse, "#AF52DE", StrokeLevel.Thick, [(.6, .6), (.85, .85)]),
            Note(AnnotationKind.Text, "#FF9500", StrokeLevel.Thick, [(.05, .55)], text: "Filee 가나다\nsecond line"),
            Note(AnnotationKind.Number, "#FFFFFF", StrokeLevel.Thick, [(.92, .08)], number: 3),
        ];

        using var without = AtQuarterScale(full, plain);
        using var with = AtQuarterScale(full, edit);

        // Looks the same ...
        Assert.All(MeanDifference(with), mean => Assert.True(mean <= 6, $"mean difference {mean:F2}"));
        // ... and the drawings are as big: a mean can hide strokes of the wrong thickness, so count the pixels they changed.
        var saved = Changed(without.Saved, with.Saved);
        var drawn = Changed(without.Drawn, with.Drawn);
        Assert.True(saved > 1500, $"{saved} pixels drawn on"); // not an empty comparison
        Assert.InRange(drawn, saved * 0.8, saved * 1.2);
    }

    /// <summary>The result of an edit made a quarter as big, and the edit drawn at that size as the editor's preview does.</summary>
    private sealed record QuarterScale(SKBitmap Saved, SKBitmap Drawn) : IDisposable
    {
        public void Dispose()
        {
            Saved.Dispose();
            Drawn.Dispose();
        }
    }

    /// <summary><paramref name="edit"/> saved at full size and then made a quarter as big, and drawn directly from a quarter-size copy.</summary>
    private static QuarterScale AtQuarterScale(SKBitmap full, ImageEdit edit)
    {
        using var saved = ImageEditRenderer.Render(full, edit);
        var smaller = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        var expected = saved.Resize(Info(saved.Width / 4, saved.Height / 4), smaller);

        using var small = full.Resize(Info(full.Width / 4, full.Height / 4), smaller);
        using var smallImage = SKImage.FromBitmap(small);
        var preview = new SKBitmap(Info(expected.Width, expected.Height));
        using var canvas = new SKCanvas(preview);
        canvas.Clear(SKColors.Transparent);
        ImageEditRenderer.Draw(canvas, smallImage, SKRect.Create(preview.Width, preview.Height), edit, fullShortSide: Math.Min(full.Width, full.Height));
        return new QuarterScale(expected, preview);
    }

    /// <summary>The mean difference per channel between the two pictures.</summary>
    private static double[] MeanDifference(QuarterScale pair)
    {
        var a = pair.Saved.GetPixelSpan();
        var b = pair.Drawn.GetPixelSpan();
        var sums = new long[4];
        for (var i = 0; i < a.Length; i++)
            sums[i % 4] += Math.Abs(a[i] - b[i]);
        var pixels = a.Length / 4.0;
        return [.. sums.Select(sum => sum / pixels)];
    }

    /// <summary>How many pixels differ clearly between two pictures of one size.</summary>
    private static int Changed(SKBitmap before, SKBitmap after)
    {
        var count = 0;
        for (var y = 0; y < before.Height; y++)
        {
            for (var x = 0; x < before.Width; x++)
            {
                SKColor a = before.GetPixel(x, y), b = after.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 90)
                    count++;
            }
        }
        return count;
    }

    [Fact]
    public async Task Red_line_changes_the_pixels_it_crosses()
    {
        // 400 × 200: a thick line is 0.016 × 200 = 3.2 px wide, from (40, 100) to (360, 100), so rows 99 and 100 are all red.
        var source = fx.Write(new MagickImage(MagickColors.White, 400, 200), "plain.png");
        var line = Note(AnnotationKind.Line, "#FF3B30", StrokeLevel.Thick, [(.1, .5), (.9, .5)]);

        var job = await fx.ConvertAsync([source], Edited("annotate", Drawn(line)));

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("plain Annotated.png", Path.GetFileName(job.Outputs.Single()));
        using var r = new MagickImage(job.Outputs.Single());
        foreach (var x in new[] { 60, 200, 340 })
        {
            Assert.True(IsRed(r, x, 99), $"({x},99)");
            Assert.True(IsRed(r, x, 100), $"({x},100)");
        }
        var rgb = r.GetPixels().ToByteArray(PixelMapping.RGB)!;
        int changed = 0, outside = 0;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 400; x++)
            {
                var i = (y * 400 + x) * 3;
                if (rgb[i] == 255 && rgb[i + 1] == 255 && rgb[i + 2] == 255)
                    continue;
                changed++;
                if (y < 97 || y > 102 || x < 37 || x > 363)
                    outside++;
            }
        }
        Assert.True(changed > 900, $"{changed} pixels changed"); // about 320 × 3.2
        Assert.Equal(0, outside); // nothing but the line itself
    }

    [Fact]
    public void Korean_text_draws_glyphs_not_boxes()
    {
        // Thick text is 0.07 × 300 = 21 px: three syllables fit well inside x 30 to 130, y 60 to 110.
        using var picture = Filled(600, 300, SKColors.White);
        SKColor[] TextRegion(string text)
        {
            using var result = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Text, "#000000", StrokeLevel.Thick, [(.05, .2)], text: text)));
            return Region(result, 30, 60, 100, 50);
        }

        var hangul = TextRegion("가나다");
        var boxes = TextRegion("□□□");

        Assert.NotEqual(TextRegion(""), hangul); // something is drawn
        Assert.NotEqual(boxes, hangul); // and not the missing-glyph box in every place
    }

    [Fact]
    public void Arrow_has_a_head_at_its_end()
    {
        // 600 × 300, thick: the line is 4.8 px, the head 19.2 long and 14.4 wide, its tip at (540, 150).
        using var picture = Filled(600, 300, SKColors.White);
        using var arrow = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Arrow, "#FF3B30", StrokeLevel.Thick, [(.1, .5), (.9, .5)])));
        using var line = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Line, "#FF3B30", StrokeLevel.Thick, [(.1, .5), (.9, .5)])));

        Assert.True(IsReddish(arrow.GetPixel(300, 150)));
        // 14 px behind the tip the head is 10.5 px wide: red 4 px off the middle, where the line alone is not.
        Assert.True(IsReddish(arrow.GetPixel(526, 154)));
        Assert.True(IsReddish(arrow.GetPixel(526, 145)));
        Assert.False(IsReddish(arrow.GetPixel(400, 154))); // along the stem the line is only 4.8 px
        Assert.False(IsReddish(arrow.GetPixel(515, 154))); // the head has not begun
        Assert.False(IsReddish(arrow.GetPixel(545, 150))); // and does not go past its tip
        Assert.False(IsReddish(line.GetPixel(526, 154))); // a plain line has no head
        Assert.True(IsReddish(line.GetPixel(526, 150)));
    }

    [Fact]
    public void Number_sticker_uses_black_digits_on_yellow()
    {
        // 600 × 600, thick: a sticker of 0.09 × 600 = 54 px (radius 27) around (300, 300).
        using var picture = Filled(600, 600, SKColors.White);
        using var yellow = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Number, "#FFCC00", StrokeLevel.Thick, [(.5, .5)], number: 8)));
        using var red = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Number, "#FF3B30", StrokeLevel.Thick, [(.5, .5)], number: 8)));

        AssertNear(new SKColor(0xFF, 0xCC, 0x00), yellow.GetPixel(300 - 20, 300)); // the disc, off the digit
        AssertNear(new SKColor(0xFF, 0x3B, 0x30), red.GetPixel(300 - 20, 300));
        // Inside the disc: yellow has dark ink and no white; red has white ink and no dark.
        Assert.True(Count(yellow, 300, 300, 24, c => c.Red < 100 && c.Green < 100 && c.Blue < 100) > 30);
        Assert.Equal(0, Count(yellow, 300, 300, 24, c => c.Red > 235 && c.Green > 235 && c.Blue > 235));
        Assert.True(Count(red, 300, 300, 24, c => c.Red > 235 && c.Green > 235 && c.Blue > 235) > 30);
        Assert.Equal(0, Count(red, 300, 300, 24, c => c.Green < 30 && c.Blue < 30));
    }

    [Fact]
    public void Highlighter_overlap_inside_one_stroke_does_not_darken()
    {
        // A cross made of one stroke: the middle is drawn twice but must look like the arms.
        using var picture = Filled(600, 600, SKColors.White);
        var cross = Note(AnnotationKind.Highlighter, "#FF3B30", StrokeLevel.Normal, [(.2, .5), (.8, .5), (.5, .2), (.5, .8)]);
        using var one = ImageEditRenderer.Render(picture, Drawn(cross));
        var overlap = Note(AnnotationKind.Highlighter, "#FF3B30", StrokeLevel.Normal, [(.2, .5), (.8, .5)]);
        using var two = ImageEditRenderer.Render(picture, Drawn(overlap, overlap.Clone()));

        // 40 % of red over white: (255, 255 - 0.4 × 196, 255 - 0.4 × 207) ≈ (255, 177, 172).
        var arm = one.GetPixel(200, 300);
        AssertNear(new SKColor(255, 177, 172), arm);
        Assert.Equal(arm, one.GetPixel(300, 300)); // where the stroke crosses itself
        Assert.Equal(arm, one.GetPixel(300, 250)); // and on the arm that comes back over it
        // Two strokes on top of each other are two layers, and do darken: that is what the single layer avoids.
        Assert.True(two.GetPixel(200, 300).Green < arm.Green - 30);
    }

    [Fact]
    public void Highlighter_is_three_times_as_thick_as_a_pen()
    {
        // Normal is 0.008 × 1000 = 8 px, so 24 px for the highlighter, aligned to whole pixels around the middle.
        using var picture = Filled(1000, 1000, SKColors.White);
        using var pen = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Pen, "#000000", StrokeLevel.Normal, [(.2, .5), (.8, .5)])));
        using var marker = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Highlighter, "#000000", StrokeLevel.Normal, [(.2, .5), (.8, .5)])));

        Assert.Equal(8, Column(pen, 500).Count(c => !IsWhite(c)));
        Assert.Equal(24, Column(marker, 500).Count(c => !IsWhite(c)));
    }

    [Theory]
    [InlineData(StrokeLevel.Thin, 4, 50)]
    [InlineData(StrokeLevel.Normal, 8, 65)]
    [InlineData(StrokeLevel.Thick, 16, 90)]
    public void Sizes_follow_the_stroke_level(StrokeLevel level, int line, int sticker)
    {
        // On a 1000 × 1000 picture the fractions are whole pixels: 0.004, 0.008 and 0.016 for lines, 0.05, 0.065 and 0.09 for stickers.
        using var picture = Filled(1000, 1000, SKColors.White);
        using var pen = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Pen, "#000000", level, [(.2, .5), (.8, .5)])));
        using var number = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Number, "#FFCC00", level, [(.5, .5)], number: 1)));

        Assert.Equal(line, Column(pen, 500).Count(c => !IsWhite(c)));
        Assert.InRange(Column(number, 500).Count(c => !IsWhite(c)), sticker - 1, sticker + 1);
    }

    [Fact]
    public void Text_follows_the_stroke_level_and_lines_are_one_and_a_quarter_sizes_apart()
    {
        using var picture = Filled(1000, 1000, SKColors.White);
        SKRectI Ink(AnnotationKind kind, StrokeLevel level, string text)
        {
            using var result = ImageEditRenderer.Render(picture, Drawn(Note(kind, "#000000", level, [(.1, .1)], text: text)));
            return Bounds(result);
        }

        var thin = Ink(AnnotationKind.Text, StrokeLevel.Thin, "가");
        var normal = Ink(AnnotationKind.Text, StrokeLevel.Normal, "가");
        var thick = Ink(AnnotationKind.Text, StrokeLevel.Thick, "가");
        Assert.True(thin.Height < normal.Height && normal.Height < thick.Height, $"{thin.Height} {normal.Height} {thick.Height}");
        // A syllable is about as tall as its font size (45 px at Normal), a little less.
        Assert.InRange(normal.Height, 30, 45);

        var two = Ink(AnnotationKind.Text, StrokeLevel.Normal, "가\n가");
        Assert.InRange(two.Height - normal.Height, 45 * 1.25 - 1.5, 45 * 1.25 + 1.5); // the second line is 56.25 px lower
        Assert.Equal(normal.Top, two.Top);
        Assert.Equal(normal.Left, two.Left);
    }

    [Fact]
    public void A_tap_with_the_pen_leaves_a_dot()
    {
        using var picture = Filled(600, 600, SKColors.White);
        using var result = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Pen, "#000000", StrokeLevel.Thick, [(.5, .5)])));

        Assert.False(IsWhite(result.GetPixel(300, 300)));
        Assert.True(IsWhite(result.GetPixel(300, 290)));
    }

    [Fact]
    public void Rectangle_and_ellipse_are_outlines_only()
    {
        using var picture = Filled(600, 600, SKColors.White);
        using var rectangle = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Rectangle, "#000000", StrokeLevel.Thick, [(.8, .8), (.2, .2)])));
        using var ellipse = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Ellipse, "#000000", StrokeLevel.Thick, [(.2, .2), (.8, .8)])));

        Assert.False(IsWhite(rectangle.GetPixel(300, 120))); // corners may come in any order: the top edge
        Assert.False(IsWhite(rectangle.GetPixel(120, 300)));
        Assert.True(IsWhite(rectangle.GetPixel(300, 300))); // inside is the picture
        Assert.False(IsWhite(ellipse.GetPixel(300, 120)));
        Assert.False(IsWhite(ellipse.GetPixel(480, 300)));
        Assert.True(IsWhite(ellipse.GetPixel(300, 300)));
        Assert.True(IsWhite(ellipse.GetPixel(125, 125))); // the box's corner is outside the ellipse
    }

    [Fact]
    public void A_colour_that_cannot_be_read_draws_red()
    {
        using var picture = Filled(600, 300, SKColors.White);
        using var result = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Pen, "not a colour", StrokeLevel.Thick, [(.2, .5), (.8, .5)])));

        AssertNear(new SKColor(0xFF, 0x3B, 0x30), result.GetPixel(300, 150));
    }

    [Fact]
    public void Default_colours_draw_as_themselves()
    {
        Assert.Equal("#FF3B30 #FF9500 #FFCC00 #34C759 #007AFF #AF52DE #000000 #FFFFFF", string.Join(' ', ImageEditRenderer.AnnotationColors));
        using var picture = Filled(400, 400, SKColors.Gray);
        foreach (var color in ImageEditRenderer.AnnotationColors)
        {
            // A sticker of 0.09 × 400 = 36 px: its disc, left of the digit, is the colour.
            using var result = ImageEditRenderer.Render(picture, Drawn(Note(AnnotationKind.Number, color, StrokeLevel.Thick, [(.5, .5)], number: 1)));
            AssertNear(SKColor.Parse(color), result.GetPixel(200 - 13, 200));
        }
    }

    [Fact]
    public async Task Annotation_on_a_turned_phone_photo_lands_where_drawn()
    {
        // Stored 400 × 200 with orientation 6: shown 200 × 400 with the red square at the top right (x 100-200, y 0-100).
        // A thick sticker is 0.09 × 200 = 18 px: blue around (50, 60), the visible upper left.
        var sticker = Note(AnnotationKind.Number, "#007AFF", StrokeLevel.Thick, [(.25, .15)], number: 1);

        using var r = await RunAsync(fx.WritePhone(scale: 10), Edited("annotate", Drawn(sticker)));

        Assert.Equal((200u, 400u), (r.Width, r.Height));
        bool IsBlue(int x, int y) => Rgba(r, x, y) is var (red, _, blue, _) && blue > 200 && red < 70;
        Assert.True(IsBlue(44, 60));
        Assert.True(IsBlue(56, 60));
        Assert.False(IsBlue(170, 100)); // where it would be if it were drawn on the picture before it is turned
        Assert.True(IsRed(r, 150, 50)); // the red square is still there
    }

    private static ImageEdit Drawn(params Annotation[] notes) => new() { Annotations = [.. notes] };

    /// <summary>One drawing; <paramref name="points"/> are fractions of the picture as it is shown.</summary>
    private static Annotation Note(AnnotationKind kind, string color, StrokeLevel width, (double X, double Y)[] points,
        string? text = null, int number = 0) =>
        new() { Kind = kind, Color = color, Width = width, Points = [.. points.Select(p => new EditPoint { X = p.X, Y = p.Y })], Text = text, Number = number };

    private static bool IsWhite(SKColor c) => c.Red > 250 && c.Green > 250 && c.Blue > 250;

    private static bool IsReddish(SKColor c) => c.Red > 200 && c.Green < 80 && c.Blue < 80;

    private static SKColor[] Region(SKBitmap bitmap, int left, int top, int width, int height) =>
        [.. Enumerable.Range(0, width * height).Select(i => bitmap.GetPixel(left + i % width, top + i / width))];

    private static SKColor[] Column(SKBitmap bitmap, int x) => Region(bitmap, x, 0, 1, bitmap.Height);

    /// <summary>How many pixels in the disc of <paramref name="radius"/> around (<paramref name="cx"/>, <paramref name="cy"/>) satisfy <paramref name="match"/>.</summary>
    private static int Count(SKBitmap bitmap, int cx, int cy, int radius, Func<SKColor, bool> match)
    {
        var count = 0;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius && match(bitmap.GetPixel(x, y)))
                    count++;
            }
        }
        return count;
    }

    /// <summary>The smallest box around everything that is not white.</summary>
    private static SKRectI Bounds(SKBitmap bitmap)
    {
        var box = SKRectI.Empty;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!IsWhite(bitmap.GetPixel(x, y)))
                    box = box.IsEmpty ? new SKRectI(x, y, x + 1, y + 1) : SKRectI.Union(box, new SKRectI(x, y, x + 1, y + 1));
            }
        }
        return box;
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
