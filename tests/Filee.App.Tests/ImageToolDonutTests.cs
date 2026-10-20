// The tools slice on the image donut: drawn as an accent group, off for files the tools can't edit (animations, mixed
// files, icons), the total file size in the centre, and a tool slice that closes the donut to open its window.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using ImageMagick;

namespace Filee.App.Tests;

public class ImageToolDonutTests
{
    /// <summary>Real small files in a fresh temp folder.</summary>
    private static class TestImages
    {
        private static string Folder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "filee-app-tests", "tools-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string[] Png(int count)
        {
            var folder = Folder();
            return [.. Enumerable.Range(1, count).Select(i =>
            {
                var path = Path.Combine(folder, $"p{i}.png");
                using var image = new MagickImage(MagickColors.SteelBlue, 8, 8);
                image.Write(path);
                return path;
            })];
        }

        public static string AnimatedGif()
        {
            var path = Path.Combine(Folder(), "anim.gif");
            using var frames = new MagickImageCollection { new MagickImage(MagickColors.Red, 8, 8), new MagickImage(MagickColors.Blue, 8, 8) };
            frames.Write(path);
            return path;
        }

        /// <summary>A PNG with an acTL chunk after the header: what makes it an animated PNG (the frames don't matter here).</summary>
        public static string AnimatedPng()
        {
            var bytes = File.ReadAllBytes(Png(1)[0]);
            byte[] acTL = [0, 0, 0, 8, 0x61, 0x63, 0x54, 0x4C, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0]; // length, "acTL", 2 frames, loop, CRC
            var path = Path.Combine(Folder(), "anim.png");
            File.WriteAllBytes(path, [.. bytes[..33], .. acTL, .. bytes[33..]]); // signature (8) + IHDR chunk (25)
            return path;
        }

        public static string Write(string name, MagickFormat format)
        {
            var path = Path.Combine(Folder(), name);
            using var image = new MagickImage(MagickColors.SteelBlue, 8, 8);
            image.Write(path, format);
            return path;
        }

        public static string Text()
        {
            var path = Path.Combine(Folder(), "notes.txt");
            File.WriteAllText(path, "hello");
            return path;
        }
    }

    private static RadialViewModel Load(params string[] files)
    {
        TestServices.EnsureInitialized("ko");
        var vm = AppHost.Get<RadialViewModel>();
        vm.Load(files, clickMode: true);
        return vm;
    }

    private static DonutItem Tool(RadialViewModel vm, string id) =>
        vm.Items.Single(i => i.Id == "tools").Children!.Single(c => c.Id == id);

    [AvaloniaFact]
    public void Image_donut_shows_the_tools_slice_as_an_accent_group_with_three_tools()
    {
        var vm = Load(TestImages.Png(2));
        var tools = vm.Items.Single(i => i.Id == "tools");
        Assert.True(tools.IsAccent && tools.IsEnabled);
        Assert.Equal(3, tools.Children!.Count);
        Assert.All(tools.Children, c => Assert.True(c.IsAccent && c.IsEnabled));
        Assert.DoesNotContain(vm.Items.Where(i => i.Id != "tools"), i => i.IsAccent);
        Assert.Matches(@"^PNG 2개 · \d+ B$", vm.CenterTitle);
    }

    [AvaloniaFact]
    public void The_centre_adds_up_the_files_and_skips_what_is_not_a_file()
    {
        var files = TestImages.Png(2);
        var vm = Load([.. files, Path.GetDirectoryName(files[0])!, @"C:\no\such\file.png"]);
        Assert.EndsWith(" · " + ByteSize.Format(files.Sum(f => new FileInfo(f).Length)), vm.CenterTitle);

        Assert.Equal("JPG 1개", Load(@"C:\no\such\photo.jpg").CenterTitle); // nothing to add up: no size
    }

    [AvaloniaFact]
    public void Tools_are_off_for_an_animated_gif_and_for_mixed_files()
    {
        var vm = Load(TestImages.AnimatedGif());
        var crop = Tool(vm, "crop");
        Assert.False(crop.IsEnabled);
        Assert.False(string.IsNullOrEmpty(crop.DisabledReason));

        vm = Load(TestImages.Png(1)[0], TestImages.Text());
        // mixed: the fallback profile has no tools slice, or its crop is disabled
        Assert.DoesNotContain(vm.Items.SelectMany(i => i.Children ?? []), c => c.Id == "crop" && c.IsEnabled);
    }

    [AvaloniaFact]
    public void Compress_stays_available_for_an_animated_gif()
    {
        var vm = Load(TestImages.AnimatedGif());
        Assert.True(Tool(vm, "compress-image").IsEnabled);
        Assert.True(Tool(vm, "strip-metadata").IsEnabled);
    }

    [AvaloniaFact]
    public void An_animated_png_is_not_a_single_picture()
    {
        var vm = Load(TestImages.AnimatedPng());
        Assert.False(Tool(vm, "crop").IsEnabled);
        Assert.True(Tool(vm, "compress-image").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("s.ico", MagickFormat.Icon)]
    [InlineData("s.xbm", MagickFormat.Xbm)]
    public void No_tool_is_offered_for_icons_and_xbm(string name, MagickFormat format)
    {
        // ICO pads the result to a square and XBM breaks on the space in the tools' file names: all three tools are off.
        var vm = Load(TestImages.Write(name, format));
        foreach (var id in new[] { "crop", "compress-image", "strip-metadata" })
        {
            Assert.False(Tool(vm, id).IsEnabled);
            Assert.False(string.IsNullOrEmpty(Tool(vm, id).DisabledReason));
        }
    }

    [AvaloniaFact]
    public void Only_still_pictures_Filee_can_write_back_get_a_crop()
    {
        TestServices.EnsureInitialized("ko");
        var loc = AppHost.Get<ILocalizer>();
        Assert.NotNull(ImageToolSupport.Reason([@"C:\no\such\v.svg"], loc));
        Assert.Null(ImageToolSupport.Reason([@"C:\no\such\v.svg"], loc, formatOnly: true)); // the format check alone: only icons and XBM
        Assert.Null(ImageToolSupport.Reason(TestImages.Png(2), loc));
        Assert.True(ImageToolSupport.IsEditable("heic")); // HEIC crops through its own writer
        Assert.True(ImageToolSupport.IsEditable("webp"));
        Assert.False(ImageToolSupport.IsEditable("svg"));
        Assert.False(ImageToolSupport.IsEditable("raw")); // read only: there is no writer to cut it with
        Assert.False(ImageToolSupport.IsEditable("ico"));
    }

    /// <summary>A WindowService whose tool window "opens" at once and answers with the edit the test hands it.</summary>
    private sealed class FakeToolWindows(ImageEdit? answer)
        : WindowService(AppHost.Get<ILocalizer>(), AppHost.Get<IServiceProvider>(), AppHost.Get<MemoryTrimmer>())
    {
        public List<(ImageToolKind Tool, string[] Files, PixelPoint Cursor)> Opened { get; } = [];

        public override Task<ImageEdit?> EditImageAsync(ImageToolKind tool, IReadOnlyList<string> files, PixelPoint cursor)
        {
            Opened.Add((tool, [.. files], cursor));
            return Task.FromResult(answer);
        }
    }

    /// <summary>Opens the donut on <paramref name="files"/> at (400, 400) and clicks the crop slice of its tools ring.</summary>
    private static (RadialController Radial, FakeToolWindows Windows) ClickCrop(string[] files, ImageEdit? edit)
    {
        TestServices.EnsureInitialized("ko");
        var windows = new FakeToolWindows(edit);
        var radial = new RadialController(AppHost.Get<RadialViewModel>(), AppHost.Get<ConversionService>(), windows,
            AppHost.Get<Filee.Core.Settings.UserDataStore>(), AppHost.Get<Filee.Core.Platform.IPlatformServices>(),
            AppHost.Get<Microsoft.Extensions.Logging.ILogger<RadialController>>());
        radial.ShowForFiles(400, 400, files);
        Dispatcher.UIThread.RunJobs();
        var donut = radial.Window!.DonutControl;
        var group = donut.Items!.ToList().FindIndex(i => i.Id == "tools");
        var onGroup = DonutGeometry.PointAt(new Point(donut.Bounds.Width / 2, donut.Bounds.Height / 2),
            donut.OuterRadius * 0.6, DonutGeometry.MidAngle(group, donut.Items!.Count));
        donut.SetExternalHit(donut.HitTest(onGroup), onGroup);
        donut.ExpandPendingGroup();
        donut.HandleKey(Key.Enter); // into the ring, on the crop slice
        donut.HandleKey(Key.Enter); // and click it
        return (radial, windows);
    }

    [AvaloniaFact]
    public void A_tool_slice_closes_the_donut_opens_the_tool_and_runs_with_the_edit_it_made()
    {
        TestServices.EnsureInitialized("ko");
        var files = TestImages.Png(2);
        var edit = new ImageEdit { Crop = new CropRect { Width = .5, Height = .5 } };
        var conversions = AppHost.Get<ConversionService>();
        var jobs = conversions.Jobs.Count;
        var (radial, windows) = ClickCrop(files, edit);

        Assert.False(radial.IsVisible);         // closed at once, the tool opens from the next turn of the UI thread
        Assert.Empty(windows.Opened);
        Dispatcher.UIThread.RunJobs();

        var opened = Assert.Single(windows.Opened);
        Assert.Equal(ImageToolKind.Crop, opened.Tool);
        Assert.Equal(files, opened.Files);
        Assert.InRange(opened.Cursor.X, 398, 402);  // where the donut was
        Assert.InRange(opened.Cursor.Y, 398, 402);
        Assert.Equal(jobs + 1, conversions.Jobs.Count);
        var job = conversions.Jobs[0];
        Assert.Equal(files, job.Job.Sources);
        var ran = job.Job.Preset.Image.Edit!.Crop!;     // the edit is on the run, not on the stored preset
        Assert.Equal((.5, .5), (ran.Width, ran.Height));
        Assert.Null(AppHost.Get<Filee.Core.Settings.UserDataStore>().FindPreset("crop")!.Image.Edit);
        job.DismissCommand.Execute(null);
    }

    [AvaloniaFact]
    public void A_tool_closed_without_applying_runs_nothing()
    {
        TestServices.EnsureInitialized("ko");
        var conversions = AppHost.Get<ConversionService>();
        var jobs = conversions.Jobs.Count;
        var (radial, windows) = ClickCrop(TestImages.Png(1), edit: null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(radial.IsVisible);
        Assert.Single(windows.Opened);
        Assert.Equal(jobs, conversions.Jobs.Count);
    }
}
