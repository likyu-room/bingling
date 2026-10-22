using Filee.Core.Conversion;
using Filee.Core.Platform;
using Filee.Core.Presets;

namespace Filee.Core.Tests;

public class JobQueueTests
{
    private static async Task<ConversionJob> Run(JobQueue queue, IReadOnlyList<string> sources, Preset preset)
    {
        var job = new ConversionJob(sources, preset, "Test");
        await queue.RunAsync(job);
        return job;
    }

    [Fact]
    public async Task Converts_every_file()
    {
        using var dir = new TempDir();
        var a = dir.File("a.jpg");
        var b = dir.File("b.jpg");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [a, b], new Preset { TargetFormat = "png" });

        Assert.Equal(JobState.Completed, job.State);
        Assert.True(File.Exists(Path.Combine(dir.Path, "a.png")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "b.png")));
        Assert.Equal(1, job.Progress);
    }

    [Fact]
    public async Task An_output_that_is_not_smaller_is_deleted_when_the_preset_keeps_only_smaller_ones()
    {
        using var dir = new TempDir();
        var small = dir.File("small.jpg", "x");                       // the fake engine writes more than one byte
        var large = dir.File("large.jpg", new string('x', 1000));
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [small, large], new Preset { TargetFormat = "png", Image = { OnlyIfSmaller = true } });

        Assert.Equal(JobState.Completed, job.State);
        var skipped = job.Files[0];
        Assert.Equal((FileState.Skipped, "toast.skipped_not_smaller"), (skipped.State, skipped.NoticeKey));
        Assert.Empty(skipped.Outputs);
        Assert.False(File.Exists(Path.Combine(dir.Path, "small.png")));
        Assert.Equal("x", File.ReadAllText(small)); // the original stays as it was
        Assert.Equal(FileState.Done, job.Files[1].State);
        Assert.True(File.Exists(Path.Combine(dir.Path, "large.png")));

        // The name the deleted output had is free again: the next conversion writes small.png, not a renamed copy.
        var again = await Run(queue, [small], new Preset { TargetFormat = "png" });
        Assert.Equal(Path.Combine(dir.Path, "small.png"), again.Outputs.Single());
    }

    [Fact]
    public async Task Only_if_smaller_never_deletes_a_file_that_was_there_before()
    {
        // Overwrite replaced picture.png when the engine wrote it: that file is gone either way, so deleting the new one
        // would lose both. The result stays, as it did before the check existed.
        using var dir = new TempDir();
        var photo = dir.File("photo.jpg", "x");
        var existing = dir.File("photo.png", "an older picture");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [photo], new Preset
        {
            TargetFormat = "png",
            Output = { Conflict = ConflictPolicy.Overwrite },
            Image = { OnlyIfSmaller = true },
        });

        Assert.Equal(FileState.Done, job.Files.Single().State);
        Assert.Equal(existing, job.Outputs.Single());
        Assert.Equal("photo.jpg→png by magick", File.ReadAllText(existing));
    }

    [Fact]
    public async Task A_skipped_output_leaves_no_empty_folder()
    {
        using var dir = new TempDir();
        var photo = dir.File("photo.jpg", "x");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [photo], new Preset
        {
            TargetFormat = "png",
            Output = { Location = OutputLocation.Subfolder, SubfolderName = "compressed" },
            Image = { OnlyIfSmaller = true },
        });

        Assert.Equal(FileState.Skipped, job.Files.Single().State);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "compressed")));
        Assert.True(File.Exists(photo)); // the folder the original is in stays
    }

    [Fact]
    public async Task A_skipped_output_leaves_a_folder_that_has_other_files_or_was_there_before()
    {
        using var dir = new TempDir();
        var small = dir.File("small.jpg", "x");
        var large = dir.File("large.jpg", new string('x', 1000));
        var yours = Directory.CreateDirectory(Path.Combine(dir.Path, "mine")).FullName;
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        // One file is skipped, the other kept in the same new folder: the folder holds it.
        var shared = await Run(queue, [small, large], new Preset
        {
            TargetFormat = "png",
            Output = { Location = OutputLocation.Subfolder, SubfolderName = "compressed" },
            Image = { OnlyIfSmaller = true },
        });
        Assert.Equal([FileState.Skipped, FileState.Done], shared.Files.Select(f => f.State));
        Assert.True(File.Exists(Path.Combine(dir.Path, "compressed", "large.png")));

        // An empty folder the user made is not ours to remove.
        var kept = await Run(queue, [small], new Preset
        {
            TargetFormat = "png",
            Output = { Location = OutputLocation.Subfolder, SubfolderName = "mine" },
            Image = { OnlyIfSmaller = true },
        });
        Assert.Equal(FileState.Skipped, kept.Files.Single().State);
        Assert.True(Directory.Exists(yours));
    }

    [Fact]
    public async Task Folders_made_below_a_custom_folder_for_a_skipped_output_all_go()
    {
        // The watch folder's layout: Inbox\2025\a.jpg → <custom>\2025\a.png. Both new levels are taken away again.
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "inbox", "2025"));
        var photo = dir.File(Path.Combine("inbox", "2025", "a.jpg"), "x");
        var custom = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(custom);
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [photo], new Preset
        {
            TargetFormat = "png",
            Output = { Location = OutputLocation.CustomFolder, CustomFolder = Path.Combine(custom, "compressed"), KeepFoldersFrom = Path.Combine(dir.Path, "inbox") },
            Image = { OnlyIfSmaller = true },
        });

        Assert.Equal(FileState.Skipped, job.Files.Single().State);
        Assert.Empty(Directory.EnumerateFileSystemEntries(custom)); // "compressed\2025" and "compressed" are gone, "out" was there
    }

    [Fact]
    public async Task Is_busy_from_enqueueing_until_the_job_has_finished()
    {
        // The app frees memory only while the queue is idle: a waiting or running job must count.
        using var dir = new TempDir();
        var gate = new TaskCompletionSource();
        await using var queue = new JobQueue(new ConverterCatalog([new GatedConverter(gate.Task, new ConversionEdge("jpg", "png"))]));
        var busyWhenFinished = new TaskCompletionSource<bool>();
        queue.JobFinished += (_, _) => busyWhenFinished.TrySetResult(queue.IsBusy);
        Assert.False(queue.IsBusy);

        queue.Enqueue([dir.File("a.jpg")], new Preset { TargetFormat = "png" }, "Test");
        Assert.True(queue.IsBusy);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(queue.IsBusy);

        gate.SetResult();
        Assert.True(await busyWhenFinished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (queue.IsBusy && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public async Task The_only_if_smaller_check_never_deletes_the_source_an_engine_hands_back()
    {
        // An engine with nothing to do may hand back its input: that is the original, never an output to discard.
        using var dir = new TempDir();
        var photo = dir.File("photo.jpg", "original");
        await using var queue = new JobQueue(new ConverterCatalog([new HandsBackItsInput(new ConversionEdge("jpg", "jpg"))]));

        var job = await Run(queue, [photo], new Preset { TargetFormat = BuiltInData.SameAsSource, Image = { OnlyIfSmaller = true } });

        Assert.Equal(FileState.Done, job.Files.Single().State);
        Assert.Equal("original", File.ReadAllText(photo));
    }

    /// <summary>An engine that "converts" by handing back the file it was given.</summary>
    private sealed class HandsBackItsInput(params ConversionEdge[] edges) : IConverter
    {
        public string Id => "hands-back";
        public string DisplayName => Id;
        public IReadOnlyList<ConversionEdge> Edges => edges;
        public int MaxParallelism => 0;
        public EngineStatus GetStatus() => EngineStatus.Available();

        public Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([step.InputPath]);
    }

    /// <summary>Converts like <see cref="FakeConverter"/> once the gate opens.</summary>
    private sealed class GatedConverter(Task gate, params ConversionEdge[] edges) : IConverter
    {
        private readonly FakeConverter _inner = new("gated", edges);
        public string Id => "gated";
        public string DisplayName => "gated";
        public IReadOnlyList<ConversionEdge> Edges => edges;
        public int MaxParallelism => 0;
        public EngineStatus GetStatus() => EngineStatus.Available();

        public async Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            await gate;
            return await _inner.ConvertAsync(step, progress, cancellationToken);
        }
    }

    [Fact]
    public async Task Rotate_needs_an_engine_that_turns_images_and_turns_once_on_a_longer_route()
    {
        using var dir = new TempDir();
        var pdf = dir.File("doc.pdf");
        var eps = dir.File("art.eps");
        var turner = new FakeTurner("magick", new ConversionEdge("png", "jpg"));
        var renderer = new FakeTurner("ghostscript", new ConversionEdge("eps", "png"));
        await using var queue = new JobQueue(new ConverterCatalog(
            [new FakeConverter("pdfsharp", new ConversionEdge("pdf", "pdf")), turner, renderer]));

        // PDF → PDF only copies pages: not "rotated" without turning anything.
        var copied = await Run(queue, [pdf], new Preset { TargetFormat = BuiltInData.SameAsSource, Image = { Rotation = ImageRotation.Left90 } });
        Assert.Equal(JobState.Failed, copied.State);
        Assert.Equal("error.cannot_turn", copied.Files.Single().ErrorKey);
        Assert.False(File.Exists(Path.Combine(dir.Path, "doc (1).pdf")));

        // EPS → PNG → JPG: both engines turn images, only the first one is asked to (not 90° twice = 180°).
        var turned = await Run(queue, [eps], new Preset { TargetFormat = "jpg", Image = { Rotation = ImageRotation.Left90 } });
        Assert.Equal(JobState.Completed, turned.State);
        Assert.Equal([("png", ImageRotation.Left90)], renderer.Asked);
        Assert.Equal([("jpg", ImageRotation.None)], turner.Asked);

        // Without rotation nothing changes.
        var plain = await Run(queue, [pdf], new Preset { TargetFormat = BuiltInData.SameAsSource });
        Assert.Equal(JobState.Completed, plain.State);
    }

    [Fact]
    public async Task Job_with_per_file_edits_gives_each_file_its_own_edit()
    {
        using var dir = new TempDir();
        var a = dir.File("a.png");
        var b = dir.File("b.png");
        var turner = new FakeTurner("magick", new ConversionEdge("png", "png"));
        await using var queue = new JobQueue(new ConverterCatalog([turner]));
        var preset = new Preset { Kind = PresetKind.Tool, Tool = ImageToolKind.Annotate, TargetFormat = BuiltInData.SameAsSource };
        var arrow = new ImageEdit { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] };
        var black = new ImageEdit { Redact = new RedactEdit { Boxes = [new EditRect { X = .25, Width = .5, Height = .5 }] } };
        // Keys are the full paths of the sources, told apart as the file system does: in any case on Windows.
        var edits = new Dictionary<string, ImageEdit> { [OperatingSystem.IsWindows() ? a.ToUpperInvariant() : a] = arrow, [b] = black };

        var job = new ConversionJob([a, b], preset, "Test", edits);
        await queue.RunAsync(job);

        Assert.Equal(JobState.Completed, job.State);
        var seen = turner.EditsSeen.ToDictionary(s => s.Input, s => s.Edit, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AnnotationKind.Arrow, seen[a]!.Annotations!.Single().Kind);
        Assert.Null(seen[a]!.Redact);
        Assert.Equal(.25, seen[b]!.Redact!.Boxes.Single().X);
        Assert.Null(seen[b]!.Annotations);
        Assert.Null(job.Preset.Image.Edit); // the preset is the same for every file: only the file's own copy has the edit
        Assert.Null(preset.Image.Edit);

        // The job keeps its own copy: an editor that goes on changing its edit does not change a job that was started.
        arrow.Annotations![0].Kind = AnnotationKind.Line;
        Assert.Equal(AnnotationKind.Arrow, job.EditsByFile![a].Annotations!.Single().Kind);
    }

    [Fact]
    public void Per_file_edits_tell_paths_apart_as_the_file_system_does()
    {
        // On a case-sensitive volume photo.png and PHOTO.png are two pictures, each with its own drawings: compared in any
        // case, one would get the other's. That needs such a volume, so this checks the comparer the job keeps them by.
        var job = new ConversionJob(["photo.png"], new Preset(), "Test", new Dictionary<string, ImageEdit> { ["photo.png"] = new() });

        Assert.Same(FileSystemPaths.Comparer, Assert.IsType<Dictionary<string, ImageEdit>>(job.EditsByFile).Comparer);
    }

    [Fact]
    public async Task A_per_file_drawing_needs_an_engine_that_transforms_images()
    {
        using var dir = new TempDir();
        var a = dir.File("a.png");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("copier", new ConversionEdge("png", "png"))]));
        var preset = new Preset { Kind = PresetKind.Tool, Tool = ImageToolKind.Redact, TargetFormat = BuiltInData.SameAsSource };
        var edit = new ImageEdit { Redact = new RedactEdit { Boxes = [new EditRect { Width = .5, Height = .5 }] } };

        var job = new ConversionJob([a], preset, "Test", new Dictionary<string, ImageEdit> { [a] = edit });
        await queue.RunAsync(job);

        // Copied unredacted would look like success: it must fail instead.
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("error.cannot_turn", job.Files.Single().ErrorKey);
    }

    [Fact]
    public async Task A_step_before_the_turning_one_gets_the_preset_without_rotation()
    {
        // STL → PNG (an engine that renders with the image options but doesn't say it turns) → HEIC (one that turns):
        // only the HEIC step turns, or the picture would be turned twice.
        using var dir = new TempDir();
        var model = dir.File("part.stl");
        var renderer = new FakeRecorder("model3d", new ConversionEdge("stl", "png"));
        var heic = new FakeTurner("ffmpeg", new ConversionEdge("png", "heic"));
        await using var queue = new JobQueue(new ConverterCatalog([renderer, heic]));

        var job = await Run(queue, [model], new Preset { TargetFormat = "heic", Image = { Rotation = ImageRotation.Left90 } });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal([("png", ImageRotation.None)], renderer.Asked);
        Assert.Equal([("heic", ImageRotation.Left90)], heic.Asked);
    }

    [Fact]
    public async Task Playback_speed_changes_only_in_the_step_that_writes_the_output()
    {
        // MP4 → GIF → WebP: both engines keep the animation, only the last one may change its speed (not 2 × 2 = 4×).
        using var dir = new TempDir();
        var video = dir.File("clip.mp4");
        var ffmpeg = new FakeRecorder("ffmpeg", new ConversionEdge("mp4", "gif"));
        var magick = new FakeRecorder("magick", new ConversionEdge("gif", "webp"));
        await using var queue = new JobQueue(new ConverterCatalog([ffmpeg, magick]));
        var preset = new Preset { TargetFormat = "webp", Image = { AnimationSpeed = 2 } };

        var job = await Run(queue, [video], preset);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal([("gif", 1.0)], ffmpeg.Speeds);
        Assert.Equal([("webp", 2.0)], magick.Speeds);
        Assert.Equal(2, preset.Image.AnimationSpeed); // the job's preset itself is left alone
    }

    [Fact]
    public async Task Keep_dates_gives_outputs_the_dates_of_their_source()
    {
        using var dir = new TempDir();
        var taken = dir.File("taken.jpg");
        var plain = dir.File("plain.jpg");
        var created = new DateTime(2019, 7, 1, 9, 30, 0, DateTimeKind.Utc);
        var modified = new DateTime(2019, 7, 2, 18, 5, 0, DateTimeKind.Utc);
        foreach (var source in new[] { taken, plain })
        {
            File.SetCreationTimeUtc(source, created);
            File.SetLastWriteTimeUtc(source, modified);
        }
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var keep = new Preset { TargetFormat = "png", Output = { KeepDates = true } };
        await Run(queue, [taken], keep);
        await Run(queue, [plain], new Preset { TargetFormat = "png" });

        var output = Path.Combine(dir.Path, "taken.png");
        // Linux file systems keep no creation time a program can set: there only the modification time is kept.
        if (!OperatingSystem.IsLinux())
            Assert.Equal(created, File.GetCreationTimeUtc(output));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(output));
        // Without the option the output has the time it was made.
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(dir.Path, "plain.png")) > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Every_frame_is_saved_only_for_videos_and_the_frame_targets()
    {
        using var dir = new TempDir();
        var video = dir.File("clip.mp4");
        var photo = dir.File("photo.png");
        var ffmpeg = new FakeRecorder("ffmpeg", new ConversionEdge("mp4", "png"),
            new ConversionEdge("mp4", "png", FrameSequence: true), new ConversionEdge("mp4", "jpg", FrameSequence: true));
        var magick = new FakeTurner("magick", new ConversionEdge("png", "jpg"), new ConversionEdge("png", "webp"));
        await using var queue = new JobQueue(new ConverterCatalog([ffmpeg, magick]));

        // Without "save every frame" a video → JPG is a still frame the image engine turns, as always.
        var turned = await Run(queue, [video], new Preset { TargetFormat = "jpg", Image = { Rotation = ImageRotation.Left90 } });
        Assert.Equal(JobState.Completed, turned.State);
        Assert.Equal([("mp4", "png", false)], ffmpeg.Steps);
        Assert.Equal([("jpg", ImageRotation.Left90)], magick.Asked);

        // With it: one step that saves the frames.
        ffmpeg.Steps.Clear();
        var frames = await Run(queue, [video], new Preset { TargetFormat = "jpg", Media = { SaveEveryFrame = true } });
        Assert.Equal(JobState.Completed, frames.State);
        Assert.Equal([("mp4", "jpg", true)], ffmpeg.Steps);

        // A target frames can't be (left over from an earlier PNG target) or a picture: converted as usual.
        ffmpeg.Steps.Clear();
        await Run(queue, [video], new Preset { TargetFormat = "webp", Media = { SaveEveryFrame = true } });
        await Run(queue, [photo], new Preset { TargetFormat = "jpg", Media = { SaveEveryFrame = true } });
        Assert.Equal([("mp4", "png", false)], ffmpeg.Steps);
    }

    [Fact]
    public async Task A_refused_conversion_says_why_in_the_users_language()
    {
        using var dir = new TempDir();
        var video = dir.File("long.mp4");
        var ffmpeg = new FakeConverter("ffmpeg", new ConversionEdge("mp4", "png", FrameSequence: true))
        {
            Failure = new ConversionRefusedException("error.too_many_frames", "216,000 > 100,000"),
        };
        await using var queue = new JobQueue(new ConverterCatalog([ffmpeg]));

        var job = await Run(queue, [video], new Preset { TargetFormat = "png", Media = { SaveEveryFrame = true } });

        var file = job.Files.Single();
        Assert.Equal(FileState.Failed, file.State);
        Assert.Equal(("error.too_many_frames", "216,000 > 100,000"), (file.ErrorKey, file.ErrorDetail));
    }

    [Fact]
    public async Task Multi_step_route_keeps_intermediates_out_of_the_output_folder()
    {
        using var dir = new TempDir();
        var source = dir.File("doc.hwpx");
        var catalog = new ConverterCatalog([
            new FakeConverter("rhwp", new ConversionEdge("hwpx", "pdf")),
            new FakeConverter("pdfium", new ConversionEdge("pdf", "png")) { OutputsPerInput = 2 },
        ]);
        await using var queue = new JobQueue(catalog);

        var job = await Run(queue, [source], new Preset { TargetFormat = "png" });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(["doc.hwpx", "doc_p1.png", "doc_p2.png"], Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Failures_are_isolated_per_file()
    {
        using var dir = new TempDir();
        var ok = dir.File("a.jpg");
        var unsupported = dir.File("b.xyz");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));

        var job = await Run(queue, [ok, unsupported], new Preset { TargetFormat = "png" });

        Assert.Equal(JobState.CompletedWithErrors, job.State);
        Assert.Equal("error.unsupported_source", job.Files[1].ErrorKey);
    }

    [Fact]
    public async Task An_empty_file_fails_as_empty_before_any_engine_sees_it()
    {
        // A DOCX made with "New" in Explorer is 0 bytes until Word saves it (#157).
        using var dir = new TempDir();
        var empty = dir.File("new.docx", "");
        var ok = dir.File("a.jpg");
        var converter = new FakeConverter("docx-writer", new ConversionEdge("docx", "pdf"), new ConversionEdge("jpg", "pdf"));
        await using var queue = new JobQueue(new ConverterCatalog([converter]));

        var job = await Run(queue, [empty, ok], new Preset { TargetFormat = "pdf" });

        Assert.Equal(JobState.CompletedWithErrors, job.State);
        Assert.Equal(FileState.Failed, job.Files[0].State);
        Assert.Equal("error.empty_file", job.Files[0].ErrorKey);
        Assert.Null(job.Files[0].ErrorDetail);
        Assert.False(File.Exists(Path.Combine(dir.Path, "new.pdf")));
        Assert.Equal(FileState.Done, job.Files[1].State);
    }

    [Fact]
    public async Task An_empty_pdf_fails_as_empty_instead_of_breaking_the_merge()
    {
        using var dir = new TempDir();
        var photo = dir.File("1.jpg");
        var empty = dir.File("2.pdf", "");
        var merger = new FakeMerger();
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("pdfsharp", new ConversionEdge("jpg", "pdf"))]), merger);

        var job = await Run(queue, [photo, empty], new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true } });

        Assert.Equal("error.empty_file", job.Files[1].ErrorKey);
        Assert.Equal(["1.pdf"], merger.Inputs.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Combine_still_packs_empty_files_and_folders()
    {
        // Packing changes nothing: an empty file goes into the archive as it is.
        using var dir = new TempDir();
        var empty = dir.File("empty.txt", "");
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, "folder")).FullName;
        var combiner = new FakeCombiner();
        await using var queue = new JobQueue(new ConverterCatalog([]), combiner: combiner);

        var job = await Run(queue, [empty, folder], new Preset { TargetFormat = "zip", Archive = { CombineIntoOne = true } });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal([empty, folder], combiner.Inputs);
    }

    [Fact]
    public async Task A_folder_goes_into_an_archive_of_its_own_named_after_the_whole_folder()
    {
        // The folders donut: each folder its own archive next to it. A "v1.2" folder is no ".2" file.
        using var dir = new TempDir();
        var dotted = Directory.CreateDirectory(Path.Combine(dir.Path, "v1.2")).FullName;
        var photos = Directory.CreateDirectory(Path.Combine(dir.Path, "photos")).FullName;
        var combiner = new FakeCombiner();
        await using var queue = new JobQueue(new ConverterCatalog([]), combiner: combiner);

        var job = await Run(queue, [dotted, photos], new Preset { TargetFormat = "zip" });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(Path.Combine(dir.Path, "v1.2.zip"), Assert.Single(job.Files[0].Outputs));
        Assert.Equal(Path.Combine(dir.Path, "photos.zip"), Assert.Single(job.Files[1].Outputs));
        Assert.Equal(new[] { dotted, photos }.Order(), combiner.Inputs.Order());

        var all = await Run(queue, [dotted],
            new Preset { TargetFormat = "zip", Archive = { CombineIntoOne = true }, Output = { FileNamePattern = "{name}_files" } });
        Assert.Equal(Path.Combine(dir.Path, "v1.2_files.zip"), Assert.Single(all.Files[0].Outputs));
    }

    [Fact]
    public async Task A_folders_archive_keeps_the_folders_dates_when_asked()
    {
        using var dir = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, "old")).FullName;
        var then = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(folder, then);
        await using var queue = new JobQueue(new ConverterCatalog([]), combiner: new FakeCombiner());

        var job = await Run(queue, [folder], new Preset { TargetFormat = "zip", Output = { KeepDates = true } });

        Assert.Equal(then, File.GetLastWriteTimeUtc(Assert.Single(job.Files[0].Outputs)));
    }

    [Fact]
    public async Task No_route_is_reported()
    {
        using var dir = new TempDir();
        await using var queue = new JobQueue(new ConverterCatalog([]));

        var job = await Run(queue, [dir.File("a.docx")], new Preset { TargetFormat = "hwpx" });

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("error.no_route", job.Files[0].ErrorKey);
    }

    [Fact]
    public async Task Engine_exceptions_mark_the_file_failed()
    {
        using var dir = new TempDir();
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("x", new ConversionEdge("jpg", "png")) { Throw = true }]));

        var job = await Run(queue, [dir.File("a.jpg")], new Preset { TargetFormat = "png" });

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("error.conversion_failed", job.Files[0].ErrorKey);
    }

    [Fact]
    public async Task Same_as_source_target_reencodes_in_place_format()
    {
        using var dir = new TempDir();
        var source = dir.File("a.jpg");
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "jpg"))]));

        var job = await Run(queue, [source], new Preset { TargetFormat = BuiltInData.SameAsSource, Output = { FileNamePattern = "{name}_50" } });

        Assert.Equal(JobState.Completed, job.State);
        Assert.True(File.Exists(Path.Combine(dir.Path, "a_50.jpg")));
        Assert.Equal("x", File.ReadAllText(source)); // source untouched
    }

    [Fact]
    public async Task Merge_converts_to_pdf_then_merges_in_order()
    {
        using var dir = new TempDir();
        var first = dir.File("1.jpg");
        var second = dir.File("2.pdf");
        var third = dir.File("3.png");
        var merger = new FakeMerger();
        var catalog = new ConverterCatalog([new FakeConverter("pdfsharp", new ConversionEdge("jpg", "pdf"), new ConversionEdge("png", "pdf"))]);
        await using var queue = new JobQueue(catalog, merger);

        var job = await Run(queue, [first, second, third],
            new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true }, Output = { FileNamePattern = "{name}_merged" } });

        Assert.Equal(JobState.Completed, job.State);
        var merged = Path.Combine(dir.Path, "1_merged.pdf");
        Assert.Equal("1.pdf|2.pdf|3.pdf", File.ReadAllText(merged));
        Assert.Equal(second, merger.Inputs[1]); // existing PDFs are merged as-is
    }

    [Fact]
    public async Task Multi_page_tiff_converts_every_file_to_pages_then_writes_one_tiff()
    {
        using var dir = new TempDir();
        var scan = dir.File("scan.pdf");
        var photo = dir.File("photo.jpg");
        var fax = dir.File("fax.tiff");
        var merger = new FakeTiffMerger();
        var catalog = new ConverterCatalog([
            new FakeConverter("pdfium", new ConversionEdge("pdf", "tiff")) { OutputsPerInput = 2 },
            new FakeConverter("magick", new ConversionEdge("jpg", "tiff")),
        ]);
        await using var queue = new JobQueue(catalog, tiffMerger: merger);

        var job = await Run(queue, [scan, photo, fax], new Preset { TargetFormat = "tiff", Image = { MultiPageTiff = true } });

        Assert.Equal(JobState.Completed, job.State);
        var output = Assert.Single(job.Outputs);
        Assert.Equal(Path.Combine(dir.Path, "scan.tiff"), output);
        // Both PDF pages, then the photo, then the TIFF as it is.
        Assert.Equal(["scan_p1.tiff", "scan_p2.tiff", "photo.tiff", "fax.tiff"], merger.Inputs.Select(Path.GetFileName));
        Assert.Equal(fax, merger.Inputs[^1]);
    }

    [Fact]
    public async Task Merging_or_joining_pages_with_a_rotation_never_passes_a_file_through_unturned()
    {
        using var dir = new TempDir();
        var photo = dir.File("photo.jpg");
        var doc = dir.File("doc.pdf");
        var fax = dir.File("fax.tiff");
        var turner = new FakeTurner("magick", new ConversionEdge("jpg", "pdf"), new ConversionEdge("jpg", "tiff"), new ConversionEdge("tiff", "tiff"));
        var pdfMerger = new FakeMerger();
        var tiffMerger = new FakeTiffMerger();
        await using var queue = new JobQueue(new ConverterCatalog([turner]), pdfMerger, tiffMerger: tiffMerger);

        // A PDF can't be turned: merged unturned it would say "done"; it fails, the photo is still merged.
        var merged = await Run(queue, [photo, doc], new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true }, Image = { Rotation = ImageRotation.Right90 } });
        Assert.Equal("error.cannot_turn", merged.Files[1].ErrorKey);
        Assert.Equal(["photo.pdf"], pdfMerger.Inputs.Select(Path.GetFileName));

        // A TIFF is turned like the photo before the pages are joined, not added as it is.
        await Run(queue, [photo, fax], new Preset { TargetFormat = "tiff", Image = { MultiPageTiff = true, Rotation = ImageRotation.Right90 } });
        Assert.DoesNotContain(fax, tiffMerger.Inputs);
        Assert.Contains(("tiff", ImageRotation.Right90), turner.Asked);
    }

    [Fact]
    public async Task Combine_packs_every_file_as_is_even_one_or_of_unknown_type()
    {
        using var dir = new TempDir();
        var a = dir.File("report.docx");
        var b = dir.File("data.xyz");
        var combiner = new FakeCombiner();
        // No converter at all: combining must not need a route.
        await using var queue = new JobQueue(new ConverterCatalog([]), combiner: combiner);
        var preset = new Preset { TargetFormat = "zip", Archive = { CombineIntoOne = true }, Output = { FileNamePattern = "{name}_files" } };

        var job = await Run(queue, [a, b], preset);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal([a, b], combiner.Inputs);
        Assert.Equal(Path.Combine(dir.Path, "report_files.zip"), job.Outputs.Single());
        Assert.All(job.Files, f => Assert.Equal(FileState.Done, f.State));

        var single = await Run(queue, [b], preset);
        Assert.Equal(Path.Combine(dir.Path, "data_files.zip"), single.Outputs.Single());
    }

    [Fact]
    public async Task Combine_uses_the_two_part_extension_and_reports_failures_for_every_file()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        await using var queue = new JobQueue(new ConverterCatalog([]), combiner: new FakeCombiner());

        var job = await Run(queue, [a], new Preset { TargetFormat = "tgz", Archive = { CombineIntoOne = true } });
        Assert.Equal(Path.Combine(dir.Path, "a.tar.gz"), job.Outputs.Single());

        await using var failing = new JobQueue(new ConverterCatalog([]), combiner: new FakeCombiner { Throw = true });
        var failed = await Run(failing, [a, dir.File("b.txt")], new Preset { TargetFormat = "zip", Archive = { CombineIntoOne = true } });
        Assert.Equal(JobState.Failed, failed.State);
        Assert.All(failed.Files, f => Assert.Equal("boom", f.ErrorDetail));
    }

    [Fact]
    public async Task Without_combine_archive_targets_convert_file_by_file()
    {
        using var dir = new TempDir();
        var combiner = new FakeCombiner();
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("archive", new ConversionEdge("rar", "zip"))]), combiner: combiner);

        var job = await Run(queue, [dir.File("a.rar"), dir.File("b.rar")], new Preset { TargetFormat = "zip" });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(2, job.Outputs.Count());
        Assert.Empty(combiner.Inputs);
    }

    [Fact]
    public async Task Parallel_files_with_the_same_name_do_not_collide()
    {
        using var dir = new TempDir();
        var a = dir.File("same.jpg");
        var b = dir.File("same.png");
        var catalog = new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "webp"), new ConversionEdge("png", "webp"))]);
        await using var queue = new JobQueue(catalog);

        var job = await Run(queue, [a, b], new Preset { TargetFormat = "webp" });

        Assert.Equal(2, job.Outputs.Distinct().Count());
    }

    [Fact]
    public async Task Enqueue_runs_in_background_and_raises_finished()
    {
        using var dir = new TempDir();
        await using var queue = new JobQueue(new ConverterCatalog([new FakeConverter("magick", new ConversionEdge("jpg", "png"))]));
        var finished = new TaskCompletionSource<ConversionJob>();
        queue.JobFinished += (_, job) => finished.TrySetResult(job);

        queue.Enqueue([dir.File("a.jpg")], new Preset { TargetFormat = "png" }, "PNG");
        var done = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Completed, done.State);
    }
}
