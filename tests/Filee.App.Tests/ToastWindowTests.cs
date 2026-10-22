// The bottom-right progress cards: one card per job.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
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
        Assert.Matches(@"^[\d.]+\u00A0(KB|MB) → [\d.]+\u00A0(KB|MB) · \d+% 작아짐$", vm.SizeChange);
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
    public async Task An_animation_gets_a_thumbnail_of_its_first_frame()
    {
        // GIF to WebP, compress on a GIF, video to GIF: the card shows one frame and reads no more (ImageToolTests).
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var output = Path.Combine(dir.Path, "anim.gif");
        using (var frames = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue })
                frames.Add(new MagickImage(color, 120, 80) { AnimationDelay = 10 });
            frames.Write(output, MagickFormat.Gif);
        }
        var vm = Finished(dir.Bytes("clip.mp4", 50_000), output);

        await vm.LoadFinishDetailsAsync();

        Assert.NotNull(vm.Thumbnail);
        Assert.Equal((84, 56), (vm.Thumbnail!.PixelSize.Width, vm.Thumbnail.PixelSize.Height));
    }

    [AvaloniaFact]
    public async Task Toast_thumbnail_of_an_animated_png_reads_one_frame()
    {
        // The card shows the first frame and decodes no other: a later frame that can't be decoded must not cost the picture.
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = Finished(dir.Bytes("sticker.gif", 50_000), dir.AnimatedPngWithDamagedLaterFrames("sticker.apng", frames: 3));

        await vm.LoadFinishDetailsAsync();

        Assert.NotNull(vm.Thumbnail);
        Assert.Equal((84, 56), (vm.Thumbnail!.PixelSize.Width, vm.Thumbnail.PixelSize.Height));
    }

    [AvaloniaTheory]
    [InlineData(20000, 100, 224, 56)]
    [InlineData(100, 20000, 56, 224)]
    public async Task A_panorama_thumbnail_keeps_the_middle_four_times_as_long_as_it_is_short(int width, int height, int thumbWidth, int thumbHeight)
    {
        // Not 11200 × 56: the card shows the middle square of it anyway.
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var output = Path.Combine(dir.Path, "pano.png");
        using (var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height))
            image.Write(output, MagickFormat.Png);
        var vm = Finished(dir.Bytes("pano.txt", 50_000), output);

        await vm.LoadFinishDetailsAsync();

        Assert.Equal((thumbWidth, thumbHeight), (vm.Thumbnail!.PixelSize.Width, vm.Thumbnail.PixelSize.Height));
    }

    [AvaloniaFact]
    public async Task A_cancelled_job_that_wrote_a_file_still_says_it_was_cancelled()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var conversion = new ConversionJob([dir.Bytes("a.txt", 2048), dir.Bytes("b.txt", 2048)], new Preset { TargetFormat = "pdf" }, "PDF");
        await using (var queue = new JobQueue(new ConverterCatalog([new TextToPdf(1024, cancels: conversion)])))
            await queue.RunAsync(conversion);
        Assert.Equal(JobState.Cancelled, conversion.State); // the test job is what it should be: stopped, one file written
        Assert.Single(conversion.Outputs);

        var vm = new JobViewModel(conversion, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { });
        await vm.LoadFinishDetailsAsync();

        Assert.NotNull(vm.SizeChange);
        Assert.Equal("취소됨", vm.Status);
        Assert.True(vm.ShowsStatus);
    }

    [AvaloniaFact]
    public async Task Files_left_out_because_they_got_no_smaller_say_so()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var preset = new Preset { TargetFormat = "pdf", Image = { OnlyIfSmaller = true } };
        // The converter writes 2 KB: more than a.txt and c.txt, less than b.txt.
        var some = new ConversionJob([dir.Bytes("a.txt", 1024), dir.Bytes("b.txt", 4096)], preset, "PDF");
        var all = new ConversionJob([dir.Bytes("c.txt", 1024)], preset, "PDF");
        await using (var queue = new JobQueue(new ConverterCatalog([new TextToPdf(2048)])))
        {
            await queue.RunAsync(some);
            await queue.RunAsync(all);
        }
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        var one = new JobViewModel(some, loc, platform, _ => { });
        Assert.Equal("완료", one.Status);
        Assert.Equal("a.txt — 건너뜀 (더 작아지지 않음)", one.Notices);

        // Nothing written: the reason is the status, not "the file exists", and it is not said twice.
        var none = new JobViewModel(all, loc, platform, _ => { });
        Assert.Equal("건너뜀 (더 작아지지 않음)", none.Status);
        Assert.False(none.HasNotices);
    }

    [AvaloniaFact]
    public async Task The_size_before_is_the_one_the_source_had_when_the_job_was_handed_over()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.Bytes("a.txt", 4096);
        var vm = Finished(source, dir.Bytes("a.pdf", 2048));

        File.Delete(source); // a watch folder moved the original away once the job was done
        await vm.LoadFinishDetailsAsync();

        Assert.Equal("4.0\u00A0KB → 2.0\u00A0KB · 50% 작아짐", vm.SizeChange);
    }

    [AvaloniaFact]
    public void A_format_without_an_icon_of_its_own_gets_a_plain_file_in_the_circle()
    {
        TestServices.EnsureInitialized("ko");
        var font = new JobViewModel(new ConversionJob(["C:/a.otf"], new Preset { TargetFormat = "ttf" }, "TTF"),
            AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { });
        Assert.True(Avalonia.Application.Current!.TryGetResource("Icon.Donut.File", null, out var file));
        Assert.Same(file, font.FormatIcon);
    }

    [AvaloniaFact]
    public async Task A_finished_document_job_shows_no_thumbnail_but_the_size()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = Finished(dir.Bytes("note.txt", 4096), dir.Bytes("note.pdf", 2048));

        await vm.LoadFinishDetailsAsync();

        Assert.Null(vm.Thumbnail);
        Assert.Equal("4.0\u00A0KB → 2.0\u00A0KB · 50% 작아짐", vm.SizeChange);
        // No picture to show: the card gets the circle with the icon of what was made.
        Assert.NotNull(vm.FormatIcon);
    }

    [AvaloniaTheory]
    [InlineData(2048, 1024, "2.0\u00A0KB → 1.0\u00A0KB · 50% 작아짐")]
    [InlineData(1024, 3072, "1.0\u00A0KB → 3.0\u00A0KB · 200% 커짐")]
    [InlineData(1000, 1000, "1000\u00A0B → 1000\u00A0B · 0% 작아짐")]
    [InlineData(1000, 1004, "1000\u00A0B → 1004\u00A0B · 0% 작아짐")] // under half a percent: "0%", whichever way it went
    [InlineData(1000, 1006, "1000\u00A0B → 1006\u00A0B · 1% 커짐")]
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
        Assert.Equal("2.9\u00A0KB → 1.5\u00A0KB · 50% 작아짐", broken.SizeChange);

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
        Assert.Matches(@"^[\d.]+\u00A0(B|KB|MB) → [\d.]+\u00A0(B|KB|MB) · \d+% (작아짐|커짐)$", vm.SizeChange);
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
            // The card's buttons are in the screenshot: Copy beside Show, and the thumbnail is the one to press.
            Assert.Contains(toast.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("copy") && b.IsEffectivelyVisible);
            Assert.Contains(toast.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("thumbnail") && b.IsEffectivelyVisible);
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

    /// <summary>The thumbnail is a button: the hand over it, the open-in-new mark on it, "Open result" for a tip, and a press opens the first result.</summary>
    [AvaloniaFact]
    public async Task Pressing_the_thumbnail_opens_the_result_in_the_app_the_system_has_for_it()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 600, 400);
        var output = dir.Reencoded(source, "photo.jpg", quality: 30);
        var other = dir.Reencoded(source, "second.jpg", quality: 50);
        var opened = new List<string>();
        var vm = Finished(source, [output, other], open: opened.Add);
        await vm.LoadFinishDetailsAsync();
        using var shown = new ShownCard(vm);

        var thumbnail = shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("thumbnail"));
        Assert.True(thumbnail.IsEffectivelyVisible);
        Assert.Equal("결과 열기", ToolTip.GetTip(thumbnail));
        Assert.Equal("결과 열기", Avalonia.Automation.AutomationProperties.GetName(thumbnail));
        Assert.Equal(new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand).ToString(), thumbnail.Cursor?.ToString());
        Assert.Same(vm.Thumbnail, thumbnail.GetVisualDescendants().OfType<Image>().Single().Source);

        // Nothing is opened by looking, however long the pointer stays.
        Assert.Empty(opened);
        shown.Click(thumbnail);
        Assert.Equal([output], opened); // the first result, not all of them
    }

    /// <summary>The mark that tells a thumbnail can be pressed is there while the pointer is on it and only then.</summary>
    [AvaloniaFact]
    public async Task The_thumbnail_shows_it_can_be_pressed_while_the_pointer_is_on_it()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");
        using var shown = new ShownCard(vm);
        var thumbnail = shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("thumbnail"));
        var mark = thumbnail.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("open-mark"));

        Assert.Equal(0, mark.Opacity);
        shown.Toast.MouseMove(shown.CentreOf(thumbnail));
        Pump();
        Assert.Equal(1, mark.Opacity);
        Assert.False(mark.IsHitTestVisible); // a press goes to the button, not to the mark
        Assert.Contains(mark.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>(), i => i.IsEffectivelyVisible);
        shown.Save("toast-thumbnail-hover.png");

        shown.Toast.MouseMove(new Avalonia.Point(2, 2)); // the corner of the window, off the card's picture
        Pump();
        Assert.Equal(0, mark.Opacity);
    }

    /// <summary>A result that has gone (moved, deleted) or has no app: pressing does nothing to the card, nothing is thrown, and the log says why.</summary>
    [AvaloniaFact]
    public async Task Pressing_the_thumbnail_of_a_result_that_cannot_be_opened_does_nothing()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 600, 400);
        var output = dir.Reencoded(source, "photo.jpg", quality: 30);
        var log = new ListLogger();
        var vm = Finished(source, [output], open: _ => throw new InvalidOperationException("no app for this"), log: log);
        await vm.LoadFinishDetailsAsync();

        vm.OpenResultCommand.Execute(null); // would throw on the UI thread if the failure were not swallowed

        Assert.NotNull(vm.Thumbnail); // the card is as it was
        var line = Assert.Single(log.Lines);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, line.Level);
        Assert.Contains("photo.jpg", line.Message);
    }

    /// <summary>Copy is a button of the picture's card: next to "Show", with its own tip, and not on a card that has no picture.</summary>
    [AvaloniaFact]
    public async Task The_copy_button_is_on_a_card_with_a_picture_and_not_on_one_without()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var picture = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");
        using (var shown = new ShownCard(picture))
        {
            var copy = shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("copy"));
            Assert.True(copy.IsEffectivelyVisible);
            Assert.Contains(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "복사" && t.IsEffectivelyVisible);
            Assert.DoesNotContain(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "복사됨" && t.IsEffectivelyVisible);
            Assert.Equal("결과를 복사해요. 대화창이나 문서에 붙여 넣을 수 있어요", ToolTip.GetTip(copy));
        }

        // A document: the circle with the format's icon, and nothing to copy as a picture.
        var document = Finished(dir.Bytes("note.txt", 4096), [dir.Bytes("note.pdf", 2048)]);
        await document.LoadFinishDetailsAsync();
        using (var shown = new ShownCard(document))
            Assert.False(shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("copy")).IsEffectivelyVisible);
    }

    /// <summary>Pressing Copy: the picture is on the clipboard, and the button says "Copied" for a moment and then goes back.</summary>
    [AvaloniaFact]
    public async Task Pressing_copy_puts_the_picture_on_the_clipboard_and_says_so_for_a_moment()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");
        using var shown = new ShownCard(vm);
        var copy = shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("copy"));

        shown.Click(copy);
        await Until(() => vm.JustCopied);
        Pump();

        Assert.Contains(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "복사됨" && t.IsEffectivelyVisible);
        Assert.DoesNotContain(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "복사" && t.IsEffectivelyVisible);
        Assert.Equal((600, 400), await ClipboardPictureSize(shown.Toast.Clipboard!));
        shown.Save("toast-copied.png");

        await Until(() => !vm.JustCopied, seconds: 5); // back to "Copy" by itself
        Pump();
        Assert.Contains(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "복사" && t.IsEffectivelyVisible);
    }

    /// <summary>The result is gone: the button does not claim to have copied, and the clipboard keeps what it had.</summary>
    [AvaloniaFact]
    public async Task Pressing_copy_on_a_result_that_is_gone_does_not_say_copied()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var vm = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");
        using var shown = new ShownCard(vm);
        await shown.Toast.Clipboard!.SetTextAsync("what was there before");
        File.Delete(vm.Job.Outputs.Single()); // moved away after the card had its picture

        shown.Click(shown.Toast.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("copy")));
        await Until(() => !vm.IsCopying); // the attempt is over, whichever way it went
        Pump();

        Assert.False(vm.JustCopied);
        Assert.Equal("what was there before", await shown.Toast.Clipboard.TryGetTextAsync());
    }

    /// <summary>Copy pressed again while it still says "Copied" gives it a full moment again instead of cutting it short.</summary>
    [AvaloniaFact]
    public async Task Copying_again_keeps_the_label_for_another_full_moment()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 100, 100);
        var vm = Finished(source, [dir.Reencoded(source, "photo.jpg", quality: 30)]);

        // 400 ms between the first moment's end and the second's: a stalled UI thread on a loaded PC does not eat it.
        var first = vm.ShowCopiedAsync(TimeSpan.FromMilliseconds(1000));
        await Task.Delay(400);
        var second = vm.ShowCopiedAsync(TimeSpan.FromMilliseconds(1000));
        await first; // the first moment is over: the second one has 400 ms more
        Assert.True(vm.JustCopied);
        await second;
        Assert.False(vm.JustCopied);
    }

    /// <summary>A second press while the first copy is still being made (a camera's photo takes a while) does nothing.</summary>
    [AvaloniaFact]
    public async Task A_second_press_while_the_first_copy_is_still_being_made_does_nothing()
    {
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var source = dir.NoisyPicture("photo.png", 3000, 2000);
        var vm = Finished(source, [source]);
        using var shown = new ShownCard(vm);
        var (clipboard, storage) = (shown.Toast.Clipboard!, shown.Toast.StorageProvider);

        var first = vm.CopyResultAsync(clipboard, storage);
        Assert.True(vm.IsCopying); // the picture is being read on another thread
        var second = vm.CopyResultAsync(clipboard, storage);
        Assert.True(second.IsCompleted); // turned away at once, nothing read
        Assert.False(await second);
        Assert.True(await first);
        Assert.False(vm.IsCopying);

        Assert.True(await vm.CopyResultAsync(clipboard, storage)); // once it is done, a press copies again
    }

    /// <summary>
    /// The card's button row must fit its 240 px column in every language, for both cards that have Copy or Retry: the one of a job
    /// that went well (Copy and Show) and the one of a job that failed in part (Retry and Show, no Copy: with it the row was cut
    /// off in Korean, Spanish and Hindi even as an icon). Nothing may stick out of the column or lie over another button.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ko")]
    [InlineData("es")]
    [InlineData("hi")]
    [InlineData("pl")]
    [InlineData("zh-CN")]
    public async Task The_buttons_of_a_finished_card_fit_it_in_every_language(string language)
    {
        TestServices.EnsureInitialized(language);
        using var dir = new TempFolder();
        var service = AppHost.Get<ConversionService>();
        var preset = AppHost.Get<UserDataStore>().FindPreset("to-jpg")!;
        var well = service.Start([dir.NoisyPicture("photo.png", 600, 400)], preset, showInToast: false)!;
        var inPart = service.Start([dir.NoisyPicture("second.png", 600, 400), dir.Bytes("unknown.xyz", 200)], preset, showInToast: false)!;
        await Until(() => new[] { well, inPart }.All(j => !j.IsRunning && j.Thumbnail is not null && j.SizeChange is not null), seconds: 30);
        Assert.True(inPart.CanRetry);

        foreach (var vm in new[] { well, inPart })
        {
            using var shown = new ShownCard(vm);
            var toast = shown.Toast;
            var buttons = toast.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && (b.Command == vm.RetryCommand || b.Classes.Contains("copy") || b.Command == vm.OpenFolderCommand)).ToList();
            // Copy and Show for the card that went well; Retry and Show for the other, which has no Copy.
            Assert.Equal(2, buttons.Count);
            Assert.Equal(vm == well, buttons.Any(b => b.Classes.Contains("copy")));
            Assert.Equal(vm != well, buttons.Any(b => b.Command == vm.RetryCommand));
            Assert.Contains(buttons, b => b.Command == vm.OpenFolderCommand);

            var column = buttons[0].GetVisualAncestors().OfType<StackPanel>().First(p => Grid.GetColumn(p) == 1);
            double Left(Control c) => c.TranslatePoint(default, toast)!.Value.X;
            double Right(Control c) => c.TranslatePoint(new Avalonia.Point(c.Bounds.Width, 0), toast)!.Value.X;
            buttons.Sort((a, b) => Left(a).CompareTo(Left(b)));
            var card = vm == well ? "went well" : "failed in part";
            foreach (var button in buttons)
            {
                Assert.True(Right(button) <= Right(column) + 0.5, $"{language}, {card}: a button ends at {Right(button)}, the card's column at {Right(column)}");
                Assert.True(Left(button) >= Left(column) - 0.5, $"{language}, {card}: a button starts at {Left(button)}, the card's column at {Left(column)}");
                Assert.True(button.Bounds.Width >= button.DesiredSize.Width - 0.5, $"{language}, {card}: a button is {button.Bounds.Width} wide, it needs {button.DesiredSize.Width}");
            }
            for (var i = 1; i < buttons.Count; i++)
                Assert.True(Left(buttons[i]) >= Right(buttons[i - 1]) - 0.5, $"{language}, {card}: the buttons overlap");
            // What failed is told in full, on a line of its own: no button lies over it (it was cut off behind Retry).
            if (vm == inPart)
            {
                var status = toast.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.Status && t.IsEffectivelyVisible);
                var statusBox = new Avalonia.Rect(status.TranslatePoint(default, toast)!.Value, status.Bounds.Size);
                foreach (var button in buttons)
                    Assert.False(statusBox.Intersects(new Avalonia.Rect(button.TranslatePoint(default, toast)!.Value, button.Bounds.Size)),
                        $"{language}: the status {statusBox} is under a button");
                status.Measure(new Avalonia.Size(column.Bounds.Width, double.PositiveInfinity)); // as much room as the card's column has
                Assert.True(statusBox.Height >= status.DesiredSize.Height - 1, $"{language}: \"{vm.Status}\" has {statusBox.Height} of the {status.DesiredSize.Height} it needs");
            }
            shown.Save($"toast-{(vm == well ? "copy" : "partial")}-{language}.png");
        }
    }
    /// <summary>A job for <paramref name="source"/> that is done and wrote <paramref name="outputs"/>.</summary>
    private static JobViewModel Finished(string source, params string[] outputs) => Finished(source, outputs, null);

    /// <summary>The same, with what opens a file in place of the system's (so a test sees what was opened) and where the card logs.</summary>
    private static JobViewModel Finished(string source, string[] outputs, Action<string>? open, Microsoft.Extensions.Logging.ILogger? log = null)
    {
        var conversion = new ConversionJob([source], new Preset { TargetFormat = "jpg" }, "JPG");
        conversion.Files[0].State = FileState.Done;
        conversion.Files[0].Outputs.AddRange(outputs);
        return new JobViewModel(conversion, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>(), _ => { }, open: open, log: log);
    }

    /// <summary>
    /// The toast window with one job's card shown at rest (no slide-in half done in the screenshot), and the toast's list
    /// emptied again when it goes.
    /// </summary>
    private sealed class ShownCard : IDisposable
    {
        private readonly ConversionService _service = AppHost.Get<ConversionService>();

        public ShownCard(JobViewModel job)
        {
            _service.ToastJobs.Clear();
            Toast = new ToastWindow { DataContext = _service };
            foreach (var slide in Toast.Styles.OfType<Avalonia.Styling.Style>().Where(s => s.Animations.Count > 0).ToList())
                Toast.Styles.Remove(slide);
            if (!_service.Jobs.Contains(job))
                _service.Jobs.Insert(0, job);
            _service.ShowInToast(job);
            Pump();
        }

        public ToastWindow Toast { get; }

        /// <summary>The middle of <paramref name="control"/>, in the toast's own points.</summary>
        public Avalonia.Point CentreOf(Control control) => control.TranslatePoint(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Toast)!.Value;

        /// <summary>A left click on the middle of <paramref name="control"/>.</summary>
        public void Click(Control control)
        {
            var at = CentreOf(control);
            Toast.MouseDown(at, Avalonia.Input.MouseButton.Left);
            Toast.MouseUp(at, Avalonia.Input.MouseButton.Left);
        }

        public void Save(string name) => ToastWindowTests.Save(Toast, name);

        public void Dispose()
        {
            _service.ToastJobs.Clear();
            _service.Jobs.Clear();
            Toast.Close();
        }
    }

    /// <summary>Runs the UI thread until <paramref name="done"/> holds.</summary>
    private static async Task Until(Func<bool> done, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Pump();
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task A_picture_made_by_the_ring_at_once_goes_on_a_card_too()
    {
        // RadialController shows such a job in the toast (HasPictureResult): its picture, to see, copy or open it.
        TestServices.EnsureInitialized("ko");
        using var dir = new TempFolder();
        var picture = await RunToTheEnd(dir.NoisyPicture("photo.png", 600, 400), "to-jpg");
        Assert.True(picture.HasPictureResult);

        var pdf = AppHost.Get<ConversionService>().Start([dir.NoisyPicture("page.png", 300, 200)],
            AppHost.Get<UserDataStore>().FindPreset("to-pdf")!, showInToast: false)!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (pdf.Job.State is JobState.Queued or JobState.Running && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            Pump();
        }
        Assert.Equal(JobState.Completed, pdf.Job.State);
        Assert.False(pdf.HasPictureResult); // no picture to show: the ring's ✓ says enough
    }

    /// <summary>Converts <paramref name="source"/> with a real job and waits until its card has the size.</summary>
    private static async Task<JobViewModel> RunToTheEnd(string source, string presetId, bool showInToast = false)
    {
        var vm = AppHost.Get<ConversionService>().Start([source], AppHost.Get<UserDataStore>().FindPreset(presetId)!, showInToast: showInToast)!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((vm.SizeChange is null || vm.Thumbnail is null) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            Pump();
        }
        Assert.NotNull(vm.SizeChange);
        return vm;
    }

    /// <summary>
    /// "Converts" text to PDF by writing <paramref name="length"/> bytes. With <paramref name="cancels"/> it then cancels that
    /// job: a job stopped after it wrote something.
    /// </summary>
    private sealed class TextToPdf(int length, ConversionJob? cancels = null) : IConverter
    {
        public string Id => "text-to-pdf";
        public string DisplayName => Id;
        public IReadOnlyList<ConversionEdge> Edges { get; } = [new("txt", "pdf")];
        public int MaxParallelism => 1; // one file after the other: the second finds the job cancelled
        public EngineStatus GetStatus() => EngineStatus.Available();

        public Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = step.Output.Allocate("pdf")!;
            File.WriteAllBytes(path, new byte[length]);
            cancels?.Cancel();
            return Task.FromResult<IReadOnlyList<string>>([path]);
        }
    }

    /// <summary>
    /// The size of the picture on the clipboard. On Windows the card puts it on the system's clipboard itself (as a DIB),
    /// not on Avalonia's, so it is read there.
    /// </summary>
    private static async Task<(int Width, int Height)> ClipboardPictureSize(IClipboard clipboard)
    {
        if (!OperatingSystem.IsWindows())
        {
            using var bitmap = await clipboard.TryGetBitmapAsync();
            return (bitmap!.PixelSize.Width, bitmap.PixelSize.Height);
        }
        // Clipboard listeners (Windows' clipboard history among them) open it to read the new picture: wait for them.
        var opened = false;
        for (var attempt = 0; attempt < 40 && !(opened = OpenClipboard(0)); attempt++)
            await Task.Delay(50);
        Assert.True(opened);
        try
        {
            var memory = GetClipboardData(8); // CF_DIB
            Assert.NotEqual(0, memory);
            var header = GlobalLock(memory);
            Assert.NotEqual(0, header);
            try
            {
                return (Marshal.ReadInt32(header, 4), Marshal.ReadInt32(header, 8));
            }
            finally
            {
                GlobalUnlock(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

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

        /// <summary>
        /// An animated PNG of <paramref name="frames"/> red frames whose later frames hold garbage instead of picture data, so
        /// only a reader that stops after the first one can show it. Written chunk by chunk: ImageMagick can't write one.
        /// </summary>
        public string AnimatedPngWithDamagedLaterFrames(string name, int frames)
        {
            using var picture = new MagickImage(MagickColors.Red, 120, 80);
            var png = picture.ToByteArray(MagickFormat.Png32);
            byte[]? header = null;
            var data = new MemoryStream();
            for (var at = 8; at + 12 <= png.Length;)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
                var chunk = png.AsSpan(at + 8, length);
                switch (System.Text.Encoding.ASCII.GetString(png, at + 4, 4))
                {
                    case "IHDR":
                        header = chunk.ToArray();
                        break;
                    case "IDAT":
                        data.Write(chunk);
                        break;
                }
                at += 12 + length;
            }

            using var file = new MemoryStream();
            file.Write(png.AsSpan(0, 8));
            WriteChunk(file, "IHDR", header!);
            WriteChunk(file, "acTL", [.. BigEndian((uint)frames), .. BigEndian(0)]);
            var sequence = 0u;
            for (var i = 0; i < frames; i++)
            {
                // sequence, size 120 x 80 at 0,0, delay 1/10 s, dispose none, blend source
                WriteChunk(file, "fcTL", [.. BigEndian(sequence++), .. BigEndian(120), .. BigEndian(80), .. BigEndian(0), .. BigEndian(0), 0, 1, 0, 10, 0, 0]);
                if (i == 0)
                    WriteChunk(file, "IDAT", data.ToArray());
                else
                    WriteChunk(file, "fdAT", [.. BigEndian(sequence++), .. Enumerable.Repeat((byte)0xFF, 32)]);
            }
            WriteChunk(file, "IEND", []);
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, file.ToArray());
            return path;

            static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

            static void WriteChunk(Stream stream, string type, byte[] body)
            {
                var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
                var crc = 0xFFFFFFFFu;
                foreach (var b in typed)
                {
                    crc ^= b;
                    for (var bit = 0; bit < 8; bit++)
                        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
                stream.Write(BigEndian((uint)body.Length));
                stream.Write(typed);
                stream.Write(BigEndian(~crc));
            }
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
