// Rotate and flip presets: the image turns and mirrors the way the preset names say.

using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;
using ImageMagick.Drawing;

namespace Filee.Engines.Tests;

public class ImageToolTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    /// <summary>A 4 × 2 white image with one red pixel in the top left corner, run through a built-in preset.</summary>
    [Theory]
    [InlineData("rotate-right", "a_right.png", 2, 4, 1, 0)]
    [InlineData("rotate-left", "a_left.png", 2, 4, 0, 3)]
    [InlineData("rotate-180", "a_180.png", 4, 2, 3, 1)]
    [InlineData("flip-horizontal", "a_mirrored.png", 4, 2, 3, 0)]
    [InlineData("flip-vertical", "a_flipped.png", 4, 2, 0, 1)]
    public async Task The_corner_pixel_moves_where_the_preset_says(string presetId, string name, int width, int height, int x, int y)
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "a.png");
        using (var image = new MagickImage(MagickColors.White, 4, 2))
        {
            new Drawables().FillColor(MagickColors.Red).Point(0, 0).Draw(image);
            image.Write(source, MagickFormat.Png);
        }

        var job = await fx.ConvertAsync([source], BuiltInData.CreatePresets().Single(p => p.Id == presetId));

        Assert.Equal(JobState.Completed, job.State);
        var output = job.Outputs.Single();
        Assert.Equal(name, Path.GetFileName(output));
        using var result = new MagickImage(output);
        Assert.Equal(((uint)width, (uint)height), (result.Width, result.Height));
        var red = result.GetPixels().GetPixel(x, y).ToColor()!;
        Assert.True(red.R > 200 && red.G < 60, $"{presetId}: ({x},{y}) is {red}");
    }

    private static Preset Tool(string id) => BuiltInData.CreatePresets().Single(p => p.Id == id);

    /// <summary>A 40 × 20 white image with a red 10 × 10 square in the top left corner.</summary>
    private static MagickImage Marked()
    {
        var image = new MagickImage(MagickColors.White, 40, 20);
        new Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 9, 9).Draw(image);
        return image;
    }

    private static bool IsRed(IMagickImage<byte> image, int x, int y) =>
        image.GetPixels().GetPixel(x, y).ToColor() is { } c && c.R > 200 && c.G < 80 && c.B < 80;

    /// <summary>
    /// Writes <paramref name="image"/> (and disposes it) as <paramref name="name"/>, whose extension names the format, in
    /// a fresh folder. <paramref name="compressionLevel"/> sets the zlib level of a PNG.
    /// </summary>
    private string Write(MagickImage image, string name, int? compressionLevel = null)
    {
        var path = Path.Combine(fx.NewFolder(), name);
        using (image)
        {
            if (compressionLevel is { } level)
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", level);
            image.Write(path);
        }
        return path;
    }

    /// <summary>
    /// A JPEG as phones save it: stored 40 × 20, shown turned right (EXIF orientation 6), so on screen 20 × 40 with the
    /// red square at the top right.
    /// </summary>
    private string WritePhone(uint quality = 90)
    {
        var image = Marked();
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)OrientationType.RightTop);
        image.SetProfile(exif);
        image.Orientation = OrientationType.RightTop; // ImageMagick writes this into the profile
        image.Quality = quality;
        return Write(image, "phone.jpg");
    }

    /// <summary>Random pixels from a few levels per channel: a picture that zlib packs better the harder it tries.</summary>
    private static MagickImage Noise(uint width, uint height)
    {
        var random = new Random(1);
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(random.Next(8) * 32);
        return new MagickImage(pixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGB));
    }

    [Fact]
    public async Task A_phone_photo_turns_the_way_it_looks_and_says_so_in_its_orientation()
    {
        var source = WritePhone();
        using (var written = new MagickImage(source))
            Assert.Equal(OrientationType.RightTop, written.Orientation); // the test photo is what it says

        var job = await fx.ConvertAsync([source], Tool("rotate-left"));

        Assert.Equal(JobState.Completed, job.State);
        using var result = new MagickImage(job.Outputs.Single());
        // Turned left from how it looked: back to 40 × 20 with red at the top left, and no tag turning it again.
        Assert.Equal((40u, 20u), (result.Width, result.Height));
        Assert.True(IsRed(result, 3, 3));
        Assert.True(result.Orientation is OrientationType.TopLeft or OrientationType.Undefined, $"{result.Orientation}");
    }

    /// <summary>
    /// EXIF data with orientation <paramref name="orientation"/> and a small JPEG preview, as cameras and phones write
    /// it: a TIFF header, IFD0 with the orientation, IFD1 pointing at the preview.
    /// </summary>
    private static ExifProfile ExifWithThumbnail(ushort orientation)
    {
        byte[] thumbnail;
        using (var small = new MagickImage(MagickColors.Blue, 8, 4))
            thumbnail = small.ToByteArray(MagickFormat.Jpeg);
        var data = new List<byte>();
        void U16(int v) => data.AddRange(BitConverter.GetBytes((ushort)v));
        void U32(int v) => data.AddRange(BitConverter.GetBytes((uint)v));
        data.AddRange("II"u8.ToArray());
        U16(42);
        U32(8);
        U16(1); // IFD0 at 8: one entry
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U32(26); // IFD1 at 8 + 2 + 12 + 4
        U16(2); // IFD1: the preview's offset and length
        U16(0x0201); U16(4); U32(1); U32(26 + 2 + 24 + 4);
        U16(0x0202); U16(4); U32(1); U32(thumbnail.Length);
        U32(0);
        data.AddRange(thumbnail);
        return new ExifProfile([.. "Exif\0\0"u8.ToArray(), .. data]);
    }

    [Fact]
    public async Task A_turned_photo_drops_the_unturned_preview_in_its_EXIF_data()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "camera.jpg");
        using (var image = Marked())
        {
            image.SetProfile(ExifWithThumbnail(1));
            image.Write(source, MagickFormat.Jpeg);
        }
        using (var written = new MagickImage(source))
            Assert.True(written.GetExifProfile()!.ThumbnailLength > 0); // the test photo has a preview

        var job = await fx.ConvertAsync([source], Tool("rotate-right"));

        Assert.Equal(JobState.Completed, job.State);
        using var result = new MagickImage(job.Outputs.Single());
        var exif = result.GetExifProfile();
        Assert.NotNull(exif); // the metadata stays (KeepMetadata), only the stale preview goes
        Assert.Equal(0u, exif.ThumbnailLength);
    }

    [Fact]
    public async Task A_good_JPG_keeps_its_quality_when_it_turns()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "photo.jpg");
        using (var image = Marked())
        {
            image.Quality = 95;
            image.Write(source, MagickFormat.Jpeg);
        }

        var job = await fx.ConvertAsync([source], Tool("flip-horizontal"));

        using var result = new MagickImage(job.Outputs.Single());
        Assert.True(result.Quality >= 95, $"quality {result.Quality}"); // not 90, the default for converting to JPG
    }

    [Fact]
    public async Task Every_frame_of_an_animation_turns()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "anim.gif");
        using (var frames = new MagickImageCollection())
        {
            for (var i = 0; i < 2; i++)
            {
                var frame = Marked();
                frame.AnimationDelay = 20;
                frames.Add(frame);
            }
            frames.Write(source, MagickFormat.Gif);
        }

        var job = await fx.ConvertAsync([source], Tool("rotate-right"));

        Assert.Equal(JobState.Completed, job.State);
        using var result = new MagickImageCollection(job.Outputs.Single());
        Assert.Equal(2, result.Count);
        Assert.All(result, frame =>
        {
            Assert.Equal((20u, 40u), (frame.Width, frame.Height));
            Assert.True(IsRed(frame, 16, 3)); // top left went to the top right
        });
    }

    [Fact]
    public async Task A_PDF_is_not_copied_as_if_rotated_it_fails_and_says_why()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "doc.pdf");
        using (var pdf = new PdfSharp.Pdf.PdfDocument())
        {
            pdf.AddPage();
            pdf.Save(source);
        }

        var job = await fx.ConvertAsync([source], Tool("rotate-left"));

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("error.cannot_turn", job.Files.Single().ErrorKey);
        Assert.False(File.Exists(Path.Combine(dir, "doc_left.pdf")));
    }

    [Fact]
    public async Task A_turned_jpeg_keeps_its_own_quality_instead_of_the_presets_higher_one()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "phone.jpg");
        using (var photo = new MagickImage(MagickColors.SkyBlue, 320, 240))
        {
            photo.Draw(new Drawables().FillColor(MagickColors.OrangeRed).Ellipse(160, 120, 90, 60, 0, 360));
            photo.Quality = 75;
            photo.Write(source, MagickFormat.Jpeg);
        }

        var job = await fx.ConvertAsync([source], Tool("rotate-left")); // the preset says quality 95

        Assert.Equal(JobState.Completed, job.State);
        using var turned = new MagickImage(job.Outputs.Single());
        Assert.Equal((240u, 320u), (turned.Width, turned.Height));
        Assert.InRange(turned.Quality, 70u, 75u);
    }

    [Fact]
    public async Task A_long_image_of_turned_pages_still_runs_top_to_bottom()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "slides.pdf");
        using (var pdf = new PdfSharp.Pdf.PdfDocument())
        {
            for (var i = 0; i < 2; i++)
            {
                var page = pdf.AddPage();
                page.Width = PdfSharp.Drawing.XUnit.FromPoint(200); // landscape slides
                page.Height = PdfSharp.Drawing.XUnit.FromPoint(100);
            }
            pdf.Save(source);
        }
        var preset = Tool("pdf-long-image");
        preset.Image.Rotation = ImageRotation.Right90;

        var job = await fx.ConvertAsync([source], preset);

        Assert.Equal(JobState.Completed, job.State);
        using var result = new MagickImage(job.Outputs.Single());
        // Each page turned (100 × 200), then stacked: four times as tall as wide, not the whole strip on its side.
        Assert.InRange(result.Height / (double)result.Width, 3.5, 4.5);
    }

    [Fact]
    public async Task An_image_into_a_PDF_is_turned_on_the_way()
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "wide.png");
        using (var image = Marked())
            image.Write(source, MagickFormat.Png);
        var preset = Tool("rotate-left");
        preset.TargetFormat = "pdf";

        var job = await fx.ConvertAsync([source], preset);

        Assert.Equal(JobState.Completed, job.State);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(job.Outputs.Single(), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(pdf.Pages[0].Height.Point > pdf.Pages[0].Width.Point); // landscape image, portrait page
    }

    /// <summary>The built-in crop preset with a box (fractions of the picture as it is shown).</summary>
    private static Preset Crop(double x, double y, double w, double h, double? aspect = null)
    {
        var p = Tool("crop").Clone();
        p.Image.Edit = new ImageEdit { Crop = new CropRect { X = x, Y = y, Width = w, Height = h, Aspect = aspect } };
        return p;
    }

    [Fact]
    public async Task Crop_keeps_the_box_and_the_format()
    {
        // 40×20 PNG, red 10×10 top-left. Box = left quarter, full height → 10×20, red at (0,0).
        var job = await fx.ConvertAsync([Write(Marked(), "a.png")], Crop(0, 0, .25, 1));
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("a Cropped.png", Path.GetFileName(job.Outputs.Single()));
        using var r = new MagickImage(job.Outputs.Single());
        Assert.Equal((10u, 20u), (r.Width, r.Height));
        Assert.True(IsRed(r, 0, 0));
        Assert.Equal((0, 0), (r.Page.X, r.Page.Y)); // no leftover canvas offset
    }

    [Fact]
    public async Task Crop_of_a_turned_phone_photo_cuts_what_the_user_sees()
    {
        // Stored 40×20 with orientation 6 (shown 20×40, red top-right). Box = top half as seen → 20×20 with red at the top right.
        var job = await fx.ConvertAsync([WritePhone()], Crop(0, 0, 1, .5));
        using var r = new MagickImage(job.Outputs.Single());
        Assert.Equal((20u, 20u), (r.Width, r.Height));
        Assert.True(IsRed(r, 18, 1));
        Assert.Contains(r.Orientation, new[] { OrientationType.TopLeft, OrientationType.Undefined });
    }

    [Fact]
    public async Task Compress_never_raises_jpeg_quality_and_keeps_the_photo_upright()
    {
        var source = WritePhone(quality: 70);              // orientation 6, quality 70, EXIF present
        var job = await fx.ConvertAsync([source], Tool("compress-image"));
        var output = job.Outputs.Single();
        Assert.Equal("phone Compressed.jpg", Path.GetFileName(output));
        using var r = new MagickImage(output);
        Assert.True(r.Quality <= 70, $"quality {r.Quality}");
        Assert.Equal((20u, 40u), (r.Width, r.Height));     // turned upright
        Assert.Null(r.GetExifProfile());
    }

    [Fact]
    public async Task Strip_metadata_removes_exif_and_keeps_orientation_visually()
    {
        var job = await fx.ConvertAsync([WritePhone()], Tool("strip-metadata"));
        using var r = new MagickImage(job.Outputs.Single());
        Assert.Null(r.GetExifProfile());
        Assert.Equal((20u, 40u), (r.Width, r.Height));
        Assert.True(IsRed(r, 18, 1));
    }

    [Fact]
    public async Task Compressing_a_png_stays_lossless_and_not_larger()
    {
        var source = Write(Noise(256, 256), "n.png", compressionLevel: 1); // little compression on purpose
        var job = await fx.ConvertAsync([source], Tool("compress-image"));
        var output = job.Outputs.Single();
        Assert.True(new FileInfo(output).Length <= new FileInfo(source).Length);
        using var a = new MagickImage(source);
        using var b = new MagickImage(output);
        Assert.Equal(0, a.Compare(b, ErrorMetric.Absolute));
    }

    /// <summary>
    /// Raster formats ImageMagick reads and writes whose tool output is not right, so the tools (crop, compress, strip
    /// metadata) must not be offered for them. The tests below the theory say why for each.
    /// ICO: the cropped picture is padded to a square and stored in the icon sizes. XBM: ImageMagick names the data in the
    /// file after the file name, and the tools' names ("s Cropped") hold a space, which is no C identifier: the file is not
    /// valid XBM. (CUR crops fine: a cursor is only scaled down past 256 × 256.)
    /// </summary>
    private static readonly string[] ToolsMustNotOffer = ["ico", "xbm"];

    /// <summary>Every format the image engine reads and writes, so a format added later is covered without touching this test.</summary>
    public static TheoryData<string> RasterFormats() =>
        [.. ImageEncoder.Readable.Intersect(ImageEncoder.Writable).Except(ToolsMustNotOffer)];

    /// <summary>
    /// The crop tool's output size on every raster format ImageMagick reads and writes here (a 64 × 48 picture, the box
    /// is its middle half → 32 × 24).
    /// </summary>
    [Theory]
    [MemberData(nameof(RasterFormats))]
    public async Task Crop_cuts_every_raster_format_to_the_box(string format)
    {
        var job = await CropAsync(format);
        using var r = new MagickImage(job.Outputs.Single(), ImageEncoder.ToMagickFormat(format));
        Assert.Equal((32u, 24u), (r.Width, r.Height));
    }

    private async Task<ConversionJob> CropAsync(string format)
    {
        var source = Path.Combine(fx.NewFolder(), $"s.{FormatRegistry.Get(format).PrimaryExtension}");
        using (var picture = new MagickImage(MagickColors.White, 64, 48))
            ImageEncoder.WriteImage(picture, source, format); // APNG through Filee's own writer, the others by ImageMagick
        var job = await fx.ConvertAsync([source], Crop(.25, .25, .5, .5));
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => $"{f.ErrorKey} {f.ErrorDetail}")));
        return job;
    }

    [Fact]
    public async Task An_icon_is_squared_not_cropped_so_the_tools_must_not_offer_ICO()
    {
        Assert.Contains("ico", ToolsMustNotOffer);
        var job = await CropAsync("ico");
        using var r = new MagickImage(job.Outputs.Single(), MagickFormat.Icon);
        Assert.Equal(r.Width, r.Height); // the box is 32 × 24
    }

    [Fact]
    public async Task XBM_made_from_a_name_with_a_space_is_not_valid_so_the_tools_must_not_offer_XBM()
    {
        Assert.Contains("xbm", ToolsMustNotOffer);
        var job = await CropAsync("xbm");
        var header = (await File.ReadAllLinesAsync(job.Outputs.Single(), TestContext.Current.CancellationToken))[0];
        Assert.Equal("#define s Cropped_width 32", header); // the name of the data is "s Cropped": no identifier
    }
}
