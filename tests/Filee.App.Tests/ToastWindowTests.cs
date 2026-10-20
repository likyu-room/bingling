// The bottom-right progress cards: one card per job.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;

namespace Filee.App.Tests;

public class ToastWindowTests
{
    [AvaloniaFact]
    public void A_job_handed_to_the_hidden_toast_gets_one_card()
    {
        // Issue #60: a large file's ring handed its job to the toast, which showed two identical cards.
        TestServices.EnsureInitialized("en");
        var service = AppHost.Get<ConversionService>();
        service.ToastJobs.Clear();
        var toast = new ToastWindow { DataContext = service };
        try
        {
            for (var round = 0; round < 3; round++)
            {
                var job = new JobViewModel(new ConversionJob(["C:/clip.mp4"], new Preset { TargetFormat = "mp4" }, "MP4"),
                    AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { });
                service.Jobs.Insert(0, job);
                service.ShowInToast(job);
                Pump();
                Assert.True(toast.IsVisible);
                Assert.Equal(1, Cards(toast));

                service.ToastJobs.Remove(job);
                service.Jobs.Remove(job);
                Pump();
                Assert.False(toast.IsVisible);
                Assert.Equal(0, Cards(toast));
            }
        }
        finally
        {
            service.ToastJobs.Clear();
            toast.Close();
        }
    }

    [AvaloniaFact]
    public void A_note_on_a_converted_file_is_shown_under_the_card()
    {
        TestServices.EnsureInitialized("en");
        var conversion = new ConversionJob(["C:/scan.pdf", "C:/photos.pdf"], new Preset { TargetFormat = "pdf" }, "Compress PDF");
        conversion.Files[0].State = FileState.Done;
        conversion.Files[0].NoticeKey = "notice.pdf_not_smaller";
        conversion.Files[1].State = FileState.Done;
        var job = new JobViewModel(conversion, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { });

        Assert.True(job.HasNotices);
        Assert.Equal("scan.pdf — Couldn't make it smaller, so it was copied as it is", job.Notices);
        Assert.False(job.HasErrors);
    }

    [AvaloniaFact]
    public async Task A_finished_image_job_shows_a_thumbnail_and_how_much_smaller_it_got()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 1200, 800);
        var output = dir.Reencoded(source, "photo.jpg", quality: 30);
        var vm = Finished(source, output);

        await vm.LoadFinishDetailsAsync();

        Assert.NotNull(vm.Thumbnail);
        // The short side is the card's 56 px (headless: scale 1); the card cuts the long side to its square.
        Assert.Equal(56, vm.Thumbnail!.PixelSize.Height);
        Assert.Equal(84, vm.Thumbnail.PixelSize.Width);
        Assert.Matches(@"^[\d.]+ (KB|MB) → [\d.]+ (KB|MB) · \d+% 작아짐$", vm.SizeChange);
        Assert.False(vm.ShowsStatus);
        // The pictures were read into memory: the user can move or delete them straight away.
        File.Delete(source);
        File.Delete(output);
    }

    [AvaloniaFact]
    public async Task A_photo_the_phone_turned_gets_an_upright_thumbnail()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("phone.png", 200, 100);
        var output = Path.Combine(dir.Path, "phone.jpg");
        using (var turned = new MagickImage(source) { Orientation = OrientationType.RightTop })
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            turned.SetProfile(exif);
            turned.Quality = 30;
            turned.Write(output, MagickFormat.Jpeg);
        }
        var vm = Finished(source, output);

        await vm.LoadFinishDetailsAsync();

        // Stored 200 x 100, shown 100 x 200: the short side is the width.
        Assert.Equal(56, vm.Thumbnail!.PixelSize.Width);
        Assert.Equal(112, vm.Thumbnail.PixelSize.Height);
    }

    [AvaloniaFact]
    public async Task A_finished_document_job_shows_no_thumbnail_but_the_size()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = Finished(dir.Bytes("note.txt", 4096), dir.Bytes("note.pdf", 2048));

        await vm.LoadFinishDetailsAsync();

        Assert.Null(vm.Thumbnail);
        Assert.Equal("4.0 KB → 2.0 KB · 50% 작아짐", vm.SizeChange);
        // No picture to show: the card gets the circle with the icon of what was made.
        Assert.NotNull(vm.FormatIcon);
    }

    [AvaloniaTheory]
    [InlineData(2048, 1024, "2.0 KB → 1.0 KB · 50% 작아짐")]
    [InlineData(1024, 3072, "1.0 KB → 3.0 KB · 200% 커짐")]
    [InlineData(1000, 1000, "1000 B → 1000 B · 0% 작아짐")]
    [InlineData(1000, 1004, "1000 B → 1004 B · 0% 작아짐")] // under half a percent: "0%", whichever way it went
    [InlineData(1000, 1006, "1000 B → 1006 B · 1% 커짐")]
    public async Task The_size_change_names_both_sizes_and_the_whole_percent(long before, long after, string expected)
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = Finished(dir.Bytes("a.txt", before), dir.Bytes("a.pdf", after));

        await vm.LoadFinishDetailsAsync();

        Assert.Equal(expected, vm.SizeChange);
    }

    [AvaloniaFact]
    public async Task Files_that_cannot_be_read_leave_the_card_as_it_was()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();

        // A picture that is not one: no thumbnail, but the sizes are still true.
        var broken = Finished(dir.Bytes("a.png", 3000), dir.Bytes("a.jpg", 1500));
        await broken.LoadFinishDetailsAsync();
        Assert.Null(broken.Thumbnail);
        Assert.Equal("2.9 KB → 1.5 KB · 50% 작아짐", broken.SizeChange);

        // The output is gone (moved already): nothing to compare, nothing thrown.
        var gone = Finished(dir.Bytes("b.png", 3000), Path.Combine(dir.Path, "b.jpg"));
        await gone.LoadFinishDetailsAsync();
        Assert.Null(gone.Thumbnail);
        Assert.Null(gone.SizeChange);
        Assert.True(gone.ShowsStatus);
    }

    [AvaloniaFact]
    public async Task A_job_that_finishes_loads_its_details_once_and_shows_the_check()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();

        var vm = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");

        Assert.True(vm.IsSucceeded);
        Assert.NotNull(vm.Thumbnail);
        Assert.Matches(@"^[\d.]+ (B|KB|MB) → [\d.]+ (B|KB|MB) · \d+% (작아짐|커짐)$", vm.SizeChange);
        // Another refresh (the queue reports progress many times) must not read the files again.
        var thumbnail = vm.Thumbnail;
        vm.Refresh();
        await Task.Delay(100);
        Pump();
        Assert.Same(thumbnail, vm.Thumbnail);
        vm.DismissCommand.Execute(null);
    }

    /// <summary>
    /// The finished card in both looks and both modes: the thumbnail with the size under the title, and the check on the
    /// success colour, which must be readable on it (the donut's badge has the same rule).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ThemeStyle.Classic, ThemeMode.Light)]
    [InlineData(ThemeStyle.Classic, ThemeMode.Dark)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Light)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Dark)]
    public async Task The_finished_card_shows_the_picture_the_size_and_a_readable_check(ThemeStyle style, ThemeMode mode)
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 600, 400);
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.Theme.Clone();
        var service = AppHost.Get<ConversionService>();
        service.ToastJobs.Clear();
        var toast = new ToastWindow { DataContext = service };
        // The headless clock leaves the card's slide-in half done; without it the screenshot shows the card at rest.
        foreach (var slide in toast.Styles.OfType<Avalonia.Styling.Style>().Where(s => s.Animations.Count > 0).ToList())
            toast.Styles.Remove(slide);
        try
        {
            store.Settings.Theme.Style = style;
            store.Settings.Theme.Mode = mode;
            AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
            var vm = await RunToTheEnd(source, "to-jpg", showInToast: true);
            Pump();

            var badge = toast.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("success-badge"));
            Assert.True(badge.IsEffectivelyVisible);
            var disc = Assert.IsAssignableFrom<ISolidColorBrush>(badge.Background).Color;
            var glyph = Assert.IsAssignableFrom<ISolidColorBrush>(badge.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>().Single().Foreground).Color;
            Assert.True(Contrast(glyph, disc) >= 4.5, $"{style} {mode}: {glyph} on {disc} is {Contrast(glyph, disc):0.0}:1");
            Assert.Contains(toast.GetVisualDescendants().OfType<Image>(), i => ReferenceEquals(i.Source, vm.Thumbnail) && i.IsEffectivelyVisible);
            Assert.Contains(toast.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.SizeChange && t.IsEffectivelyVisible);
            Save(toast, $"toast-finished-{style}-{mode}.png".ToLowerInvariant());
        }
        finally
        {
            service.ToastJobs.Clear();
            service.Jobs.Clear();
            toast.Close();
            store.Settings.Theme.Style = before.Style;
            store.Settings.Theme.Mode = before.Mode;
            AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
        }
    }

    /// <summary>With no picture to show the card has the circle with the icon of the format it made, and no empty picture.</summary>
    [AvaloniaTheory]
    [InlineData(ThemeStyle.Classic)]
    [InlineData(ThemeStyle.PeachGlass)]
    public async Task A_card_without_a_picture_shows_the_icon_of_its_format_in_a_circle(ThemeStyle style)
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.Theme.Clone();
        var service = AppHost.Get<ConversionService>();
        service.ToastJobs.Clear();
        var toast = new ToastWindow { DataContext = service };
        foreach (var slide in toast.Styles.OfType<Avalonia.Styling.Style>().Where(s => s.Animations.Count > 0).ToList())
            toast.Styles.Remove(slide);
        try
        {
            store.Settings.Theme.Style = style;
            AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
            var vm = Finished(dir.Bytes("note.txt", 4096), dir.Bytes("note.pdf", 2048));
            await vm.LoadFinishDetailsAsync();
            service.Jobs.Insert(0, vm);
            service.ShowInToast(vm);
            Pump();

            var circle = toast.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("format-circle"));
            Assert.True(circle.IsEffectivelyVisible);
            Assert.Same(vm.FormatIcon, circle.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>().Single().Data);
            Assert.DoesNotContain(toast.GetVisualDescendants().OfType<Image>(), i => i.IsEffectivelyVisible);
            Save(toast, $"toast-document-{style}.png".ToLowerInvariant());
        }
        finally
        {
            service.ToastJobs.Clear();
            service.Jobs.Clear();
            toast.Close();
            store.Settings.Theme.Style = before.Style;
            AppHost.Get<ThemeService>().Apply(store.Settings.Theme, "ko");
        }
    }

    /// <summary>A job for <paramref name="source"/> that is done and wrote <paramref name="outputs"/>.</summary>
    private static JobViewModel Finished(string source, params string[] outputs)
    {
        var conversion = new ConversionJob([source], new Preset { TargetFormat = "jpg" }, "JPG");
        conversion.Files[0].State = FileState.Done;
        conversion.Files[0].Outputs.AddRange(outputs);
        return new JobViewModel(conversion, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { });
    }

    /// <summary>Converts <paramref name="source"/> with a real job and waits until its card has the size.</summary>
    private static async Task<JobViewModel> RunToTheEnd(string source, string presetId, bool showInToast = false)
    {
        var vm = AppHost.Get<ConversionService>().Start([source], AppHost.Get<UserDataStore>().FindPreset(presetId)!, showInToast)!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((vm.SizeChange is null || vm.Thumbnail is null) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            Pump();
        }
        Assert.NotNull(vm.SizeChange);
        return vm;
    }

    /// <summary>WCAG contrast ratio of two colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Lin(byte v) => v / 255.0 is var c && c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double L(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void Save(TopLevel window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>A folder of its own for the files a test writes, gone when the test is.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filee-tests", "toast-" + Guid.NewGuid().ToString("N")[..8]);

        public TempFolder() => Directory.CreateDirectory(Path);

        /// <summary>A picture of random noise (so it does not compress well), written in the format of <paramref name="name"/>.</summary>
        public string NoisyPicture(string name, uint width, uint height)
        {
            using var image = new MagickImage(MagickColors.SteelBlue, width, height);
            image.AddNoise(NoiseType.Random);
            var path = System.IO.Path.Combine(Path, name);
            image.Write(path);
            return path;
        }

        /// <summary><paramref name="source"/> written again as <paramref name="name"/> at a lower quality.</summary>
        public string Reencoded(string source, string name, uint quality)
        {
            using var image = new MagickImage(source) { Quality = quality };
            var path = System.IO.Path.Combine(Path, name);
            image.Write(path);
            return path;
        }

        /// <summary>A file of <paramref name="length"/> bytes that is no format at all.</summary>
        public string Bytes(string name, long length)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, new byte[length]);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // Output still being written by a slow job: the temp folder is not worth failing a test over.
            }
        }
    }

    private static int Cards(ToastWindow toast) =>
        toast.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "Jobs").ItemsPanelRoot?.Children.Count ?? 0;

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
