// Where a tool preset (crop) is and isn't offered: the preset list and watch folders pick conversions only, the donut
// right-click has nothing to edit, and it doesn't run before its tool has made an edit.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Pages;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class ToolPresetTests
{
    private static UserDataStore Store()
    {
        TestServices.EnsureInitialized("en");
        return AppHost.Get<UserDataStore>();
    }

    [AvaloniaFact]
    public void The_preset_list_and_watch_folders_offer_conversions_but_no_group_or_tool()
    {
        var store = Store();
        var presets = new PresetsPageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>());
        Assert.Contains(presets.Presets, p => p.Preset.Id == "compress-image");
        Assert.DoesNotContain(presets.Presets, p => p.Preset.Id is "crop" or "add-background" or "redact" or "annotate" or "tools");

        using var watch = new WatchFoldersPageViewModel(store, AppHost.Get<WatchFolderService>(), AppHost.Get<ILocalizer>());
        Assert.Contains(watch.Presets, p => p.Value == "strip-metadata");
        Assert.DoesNotContain(watch.Presets, p => p.Value is "crop" or "add-background" or "redact" or "annotate" or "tools");
    }

    [AvaloniaFact]
    public async Task A_tool_preset_has_no_editor_and_does_not_run_without_its_edit()
    {
        var store = Store();
        var crop = store.FindPreset("crop")!;

        // The donut's right-click asks to edit it: nothing opens and nothing was saved.
        Assert.False(await AppHost.Get<WindowService>().EditPresetAsync(crop, null));

        var conversions = AppHost.Get<ConversionService>();
        var file = Path.Combine(Path.GetTempPath(), "filee-tests", "crop-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        Assert.Null(conversions.Start([file], crop, showInToast: false));
        Assert.Null(conversions.Start([file], store.FindPreset("tools")!, showInToast: false));

        // With the box its tool made, it runs.
        var cropped = crop.Clone();
        cropped.Image.Edit = new ImageEdit { Crop = new CropRect { Width = .5, Height = .5 } };
        var job = conversions.Start([file], cropped, showInToast: false);
        Assert.NotNull(job);
        job.DismissCommand.Execute(null);
    }

    [AvaloniaFact]
    public void A_tool_preset_runs_with_one_edit_per_file_and_leaves_out_the_files_without_one()
    {
        var store = Store();
        var conversions = AppHost.Get<ConversionService>();
        var annotate = store.FindPreset("annotate")!;
        var folder = Path.Combine(Path.GetTempPath(), "filee-tests");
        var drawn = Path.Combine(folder, "drawn-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        var untouched = Path.Combine(folder, "untouched-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        var edit = new ImageEdit { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] };

        // Neither a preset-wide edit nor any file's own: nothing to run.
        Assert.Null(conversions.Start([drawn], annotate, showInToast: false));
        Assert.Null(conversions.Start([drawn], annotate, new Dictionary<string, ImageEdit>(), showInToast: false));
        Assert.Null(conversions.Start([untouched], annotate, new Dictionary<string, ImageEdit> { [drawn] = edit }, showInToast: false));

        // One card for the job; the file nothing was drawn on is not in it. Keys are matched without regard to case.
        var job = conversions.Start([drawn, untouched], annotate, new Dictionary<string, ImageEdit> { [drawn.ToUpperInvariant()] = edit }, showInToast: false);
        Assert.NotNull(job);
        Assert.Equal([drawn], job.Job.Sources);
        job.DismissCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Retrying_a_job_with_one_edit_per_file_runs_the_failed_files_with_their_own_edits()
    {
        var store = Store();
        var conversions = AppHost.Get<ConversionService>();
        var folder = Directory.CreateTempSubdirectory("filee-retry-edits-").FullName;
        try
        {
            // Not pictures, only named like them: every file fails, and is still there to be tried again.
            var arrow = Path.Combine(folder, "arrow.png");
            var number = Path.Combine(folder, "number.png");
            File.WriteAllBytes(arrow, [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllBytes(number, [0x89, 0x50, 0x4E, 0x47]);
            var edits = new Dictionary<string, ImageEdit>
            {
                [arrow] = new() { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] },
                [number] = new() { Annotations = [new Annotation { Kind = AnnotationKind.Number, Number = 1 }] },
            };
            var failed = conversions.Start([arrow, number], store.FindPreset("annotate")!, edits)!;
            WaitUntilDone(failed);
            Assert.True(failed.CanRetry);

            failed.RetryCommand.Execute(null);

            // A new card in its place. Without the files' own edits the tool preset would not run at all.
            var retried = Assert.Single(conversions.Jobs, j => j.Job.Sources.Contains(arrow));
            Assert.NotSame(failed, retried);
            Assert.Equal([arrow, number], retried.Job.Sources);
            Assert.Equal(AnnotationKind.Arrow, Assert.Single(retried.Job.EditsByFile![arrow].Annotations!).Kind);
            Assert.Equal(AnnotationKind.Number, Assert.Single(retried.Job.EditsByFile[number].Annotations!).Kind);
            WaitUntilDone(retried); // ends inside the test: a job finishing after it would post to a dispatcher that is gone
            retried.DismissCommand.Execute(null);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void History_retries_a_tool_job_only_when_its_preset_kept_the_edit()
    {
        var store = Store();
        var conversions = AppHost.Get<ConversionService>();
        var folder = Directory.CreateTempSubdirectory("filee-history-tools-").FullName;
        try
        {
            // Not pictures: both jobs fail, and the history keeps the preset each ran with.
            var cut = Path.Combine(folder, "cut.png");
            var drawn = Path.Combine(folder, "drawn.png");
            File.WriteAllBytes(cut, [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllBytes(drawn, [0x89, 0x50, 0x4E, 0x47]);
            var crop = store.FindPreset("crop")!.Clone();
            crop.Image.Edit = new ImageEdit { Crop = new CropRect { Width = .5, Height = .5 } };
            var cropJob = conversions.Start([cut], crop)!;
            var annotateJob = conversions.Start([drawn], store.FindPreset("annotate")!,
                new Dictionary<string, ImageEdit> { [drawn] = new() { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] } })!;
            WaitUntilDone(cropJob);
            WaitUntilDone(annotateJob);
            // Recorded as each job ends, on its worker thread (read from a copy: the list grows there).
            Assert.True(SpinWait.SpinUntil(() => new[] { cropJob, annotateJob }
                .All(j => store.History.ToArray().Any(h => h.JobId == j.Job.Id)), 10_000));

            // Read back from disk, as the Home page shows it after a restart.
            var saved = new UserDataStore(store.Directory);
            saved.Load();
            HistoryItemViewModel Item(JobViewModel job) => new(saved.History.Single(h => h.JobId == job.Job.Id),
                AppHost.Get<ILocalizer>(), AppHost.Get<Filee.Core.Platform.IPlatformServices>(), conversions);

            // Each file's own drawing stays with its job, not in the history: no button that would run nothing.
            Assert.False(Item(annotateJob).CanRetry);

            // A crop's box is the preset's own, so it is kept and runs again.
            var cropItem = Item(cropJob);
            Assert.True(cropItem.CanRetry);
            cropItem.RetryCommand.Execute(null);
            var retried = Assert.Single(conversions.Jobs, j => j != cropJob && j.Job.Sources.Contains(cut));
            Assert.Equal(.5, retried.Job.Preset.Image.Edit!.Crop!.Width);
            WaitUntilDone(retried);
            foreach (var job in new[] { cropJob, annotateJob, retried })
                job.DismissCommand.Execute(null);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>Runs the UI thread until <paramref name="job"/> has finished and its card shows it.</summary>
    private static void WaitUntilDone(JobViewModel job)
    {
        for (var i = 0; i < 400 && (job.Job.State is JobState.Queued or JobState.Running || job.IsRunning); i++)
        {
            Thread.Sleep(25);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(job.IsRunning);
    }
}
