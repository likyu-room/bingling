// Runs conversion jobs in the background: plans routes, limits engine concurrency,
// allocates output paths and reports progress. UI code only enqueues and listens to events.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Filee.Core.Formats;
using Filee.Core.Presets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filee.Core.Conversion;

/// <summary>Background queue that executes <see cref="ConversionJob"/>s one after another.</summary>
public sealed class JobQueue : IAsyncDisposable
{
    private readonly ConverterCatalog _catalog;
    private readonly IPdfMerger? _pdfMerger;
    private readonly IFileCombiner? _combiner;
    private readonly ITiffMerger? _tiffMerger;
    private readonly ILogger _log;
    private readonly Channel<ConversionJob> _channel = Channel.CreateUnbounded<ConversionJob>();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _engineGates = new();
    private readonly HashSet<string> _reservedOutputs = new(StringComparer.OrdinalIgnoreCase);
    // The folders each job's allocators made (by job id), so a skipped output can take its empty folder away again.
    // Guarded by the _reservedOutputs lock, like the set above.
    private readonly Dictionary<string, HashSet<string>> _madeFolders = [];
    private readonly Task _worker;
    private int _busy;

    /// <param name="catalog">Converters and their priority.</param>
    /// <param name="pdfMerger">Used by "Merge into one PDF" presets.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="combiner">Used by "Compress into one archive" presets (<see cref="ArchiveOptions.CombineIntoOne"/>).</param>
    /// <param name="tiffMerger">Used by multi-page TIFF presets (<see cref="ImageOptions.MultiPageTiff"/>).</param>
    public JobQueue(ConverterCatalog catalog, IPdfMerger? pdfMerger = null, ILogger<JobQueue>? log = null, IFileCombiner? combiner = null,
        ITiffMerger? tiffMerger = null)
    {
        _catalog = catalog;
        _pdfMerger = pdfMerger;
        _combiner = combiner;
        _tiffMerger = tiffMerger;
        _log = log ?? (ILogger)NullLogger.Instance;
        _worker = Task.Run(WorkLoopAsync);
    }

    /// <summary>Raised (on a background thread) whenever a job's state or progress changes.</summary>
    public event EventHandler<ConversionJob>? JobUpdated;

    /// <summary>Raised (on a background thread) once when a job reaches a final state.</summary>
    public event EventHandler<ConversionJob>? JobFinished;

    /// <summary>True while a job waits in the queue or runs (the app frees memory only when this is false).</summary>
    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    /// <summary>Adds a job to the queue and returns it immediately.</summary>
    /// <param name="editsByFile">The edit of each file, for a tool that makes one per picture (<see cref="ConversionJob.EditsByFile"/>).</param>
    public ConversionJob Enqueue(IReadOnlyList<string> sources, Preset preset, string presetDisplayName,
        IReadOnlyDictionary<string, ImageEdit>? editsByFile = null)
    {
        var job = new ConversionJob(sources, preset, presetDisplayName, editsByFile);
        Interlocked.Increment(ref _busy); // until the worker is done with it
        _channel.Writer.TryWrite(job);
        JobUpdated?.Invoke(this, job);
        return job;
    }

    /// <summary>Runs a job to completion on the calling task. Used by tests and by <see cref="Enqueue"/>'s worker.</summary>
    public async Task RunAsync(ConversionJob job)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            await RunJobAsync(job);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }
    }

    private async Task RunJobAsync(ConversionJob job)
    {
        job.State = JobState.Running;
        Notify(job);

        var workDir = Path.Combine(Path.GetTempPath(), AppIdentity.Folder, job.Id);
        Directory.CreateDirectory(workDir);
        try
        {
            var planner = _catalog.CreatePlanner();
            var merge = job.Preset.Pdf.MergeIntoSingle
                        && job.Preset.TargetFormat == "pdf"
                        && job.Files.Count > 1
                        && _pdfMerger is not null;
            // Any files, one of them or many, archives or not: they go into the archive as they are.
            var combine = job.Preset.Archive.CombineIntoOne
                          && _combiner is not null
                          && FormatRegistry.FindById(job.Preset.TargetFormat) is { Category: FormatCategory.Archive } archive
                          && archive.Id != FormatRegistry.Folder;

            // One file is enough: a 10-page PDF becomes one 10-page TIFF.
            var multiPageTiff = job.Preset.Image.MultiPageTiff && job.Preset.TargetFormat == "tiff" && _tiffMerger is not null;

            if (merge)
                await RunMergedAsync(job, planner, workDir);
            else if (multiPageTiff)
                await RunMultiPageTiffAsync(job, planner, workDir);
            else if (combine)
                await RunCombinedAsync(job, workDir);
            else
                await Parallel.ForEachAsync(
                    job.Files.Select((f, i) => (File: f, Index: i + 1)),
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = CancellationToken.None },
                    async (item, _) => await RunFileAsync(job, item.File, item.Index, planner, workDir, finalOutput: true));

            job.State = job.CancellationToken.IsCancellationRequested ? JobState.Cancelled
                : job.Files.All(f => f.State is FileState.Done or FileState.Skipped) ? JobState.Completed
                : job.Files.Any(f => f.State == FileState.Done) ? JobState.CompletedWithErrors
                : JobState.Failed;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Job {Job} failed", job.Id);
            job.State = JobState.Failed;
        }
        finally
        {
            lock (_reservedOutputs)
            {
                foreach (var output in job.Outputs)
                    _reservedOutputs.Remove(output);
                _madeFolders.Remove(job.Id);
            }
            TryDelete(workDir);
        }

        Notify(job);
        JobFinished?.Invoke(this, job);
    }

    /// <summary>Converts every file to PDF in the work directory, then merges them into one output.</summary>
    private async Task RunMergedAsync(ConversionJob job, RoutePlanner planner, string workDir)
    {
        var parts = new string?[job.Files.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, job.Files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, _) =>
            {
                var file = job.Files[i];
                // An empty PDF goes the long way, where it fails as empty instead of breaking the whole merge.
                if (FormatRegistry.Detect(file.SourcePath)?.Id == "pdf" && !IsEmptyFile(file.SourcePath))
                {
                    // Already a PDF, merged as it is: its pages can't be turned (only images going into it can).
                    if (job.Preset.Image.TurnsOrMirrors)
                        FailUnturned(job, file, "pdf → pdf");
                    else
                        parts[i] = file.SourcePath;
                    return;
                }
                var produced = await RunFileAsync(job, file, i + 1, planner, workDir, finalOutput: false);
                parts[i] = produced.FirstOrDefault();
            });

        var inputs = parts.OfType<string>().ToList();
        if (inputs.Count == 0 || job.CancellationToken.IsCancellationRequested)
            return;

        var first = job.Files[0];
        var allocator = CreateFinalAllocator(job, first.SourcePath, 1);
        var output = allocator.Allocate("pdf");
        if (output is null)
        {
            foreach (var f in job.Files) f.State = FileState.Skipped;
            return;
        }

        await _pdfMerger!.MergeAsync(inputs, output, job.CancellationToken);
        first.Outputs.Add(output);
        foreach (var f in job.Files.Where(f => f.State != FileState.Failed))
        {
            f.State = FileState.Done;
            f.Progress = 1;
        }
        Notify(job);
    }

    /// <summary>
    /// Converts every file to TIFF pages in the work directory (a TIFF is taken as it is), then writes them all as one
    /// multi-page TIFF named after the first file.
    /// </summary>
    private async Task RunMultiPageTiffAsync(ConversionJob job, RoutePlanner planner, string workDir)
    {
        var parts = new IReadOnlyList<string>[job.Files.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, job.Files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, _) =>
            {
                var file = job.Files[i];
                // A TIFF joins as it is, unless it is to be turned: then it goes through the engine like the rest.
                parts[i] = FormatRegistry.Detect(file.SourcePath)?.Id == "tiff" && !job.Preset.Image.TurnsOrMirrors
                           && !IsEmptyFile(file.SourcePath)
                    ? [file.SourcePath]
                    : await RunFileAsync(job, file, i + 1, planner, workDir, finalOutput: false, intermediate: "tiff");
            });

        var inputs = parts.SelectMany(p => p ?? []).ToList();
        if (inputs.Count == 0 || job.CancellationToken.IsCancellationRequested)
            return;

        var first = job.Files[0];
        var output = CreateFinalAllocator(job, first.SourcePath, 1).Allocate("tiff");
        if (output is null)
        {
            foreach (var f in job.Files) f.State = FileState.Skipped;
            return;
        }

        await _tiffMerger!.MergeAsync(inputs, output, job.Preset.Image, job.CancellationToken);
        first.Outputs.Add(output);
        foreach (var f in job.Files.Where(f => f.State != FileState.Failed))
        {
            f.State = FileState.Done;
            f.Progress = 1;
        }
        Notify(job);
    }

    /// <summary>A file that would be used as it is although the preset turns or mirrors it.</summary>
    private void FailUnturned(ConversionJob job, FileResult file, string detail)
    {
        file.State = FileState.Failed;
        file.ErrorKey = "error.cannot_turn";
        file.ErrorDetail = detail;
        file.Progress = 1;
        Notify(job);
    }

    /// <summary>Packs every source file, unchanged, into one archive named after the first file.</summary>
    private Task RunCombinedAsync(ConversionJob job, string workDir) => PackAsync(job, job.Files, 1, workDir);

    /// <summary>
    /// Packs <paramref name="files"/> unchanged into one archive of the preset's format named after the first of them:
    /// every file of a "compress into one" job, or one folder on the folders donut. A folder goes in with its content.
    /// </summary>
    private async Task PackAsync(ConversionJob job, IReadOnlyList<FileResult> files, int index, string workDir)
    {
        var format = FormatRegistry.Get(job.Preset.TargetFormat);
        var inputs = new List<FileResult>();
        foreach (var file in files)
        {
            if (File.Exists(file.SourcePath) || Directory.Exists(file.SourcePath))
                inputs.Add(file);
            else
                MarkFailed(file, "The file no longer exists.");
        }
        if (inputs.Count == 0)
        {
            Notify(job);
            return;
        }

        var output = CreateFinalAllocator(job, inputs[0].SourcePath, index).Allocate(format.PrimaryExtension);
        if (output is null)
        {
            foreach (var f in inputs)
            {
                f.State = FileState.Skipped;
                f.Progress = 1;
            }
            Notify(job);
            return;
        }

        foreach (var f in inputs)
            f.State = FileState.Running;
        Notify(job);
        var progress = new Progress<double>(p =>
        {
            // Reports arrive on the thread pool, possibly out of order: progress only moves forward.
            var value = Math.Clamp(p, 0, 1);
            if (value <= inputs[0].Progress)
                return;
            foreach (var f in inputs)
                f.Progress = value;
            Notify(job);
        });

        try
        {
            await _combiner!.CombineAsync(inputs.Select(f => f.SourcePath).ToList(), output, format.Id, job.Preset,
                workDir, progress, job.CancellationToken);
            inputs[0].Outputs.Add(output);
            foreach (var f in inputs)
            {
                f.State = FileState.Done;
                f.Progress = 1;
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var f in inputs)
                f.State = FileState.Cancelled;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Packing {Count} file(s) into {Output} failed", inputs.Count, output);
            foreach (var f in inputs)
                MarkFailed(f, ex.Message);
        }
        Notify(job);

        static void MarkFailed(FileResult f, string detail)
        {
            f.State = FileState.Failed;
            f.ErrorKey = "error.conversion_failed";
            f.ErrorDetail = detail;
            f.Progress = 1;
        }
    }

    /// <summary>A file (not a folder) with nothing in it. Combining still packs such a file as it is.</summary>
    private static bool IsEmptyFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Gives the outputs the creation and modification dates of the source (<see cref="OutputRule.KeepDates"/>).</summary>
    private void CopyDates(string source, IEnumerable<string> outputs)
    {
        try
        {
            var created = File.GetCreationTimeUtc(source);
            var modified = File.GetLastWriteTimeUtc(source);
            foreach (var output in outputs)
            {
                if (File.Exists(output))
                {
                    File.SetCreationTimeUtc(output, created);
                    File.SetLastWriteTimeUtc(output, modified);
                }
                else if (Directory.Exists(output))
                {
                    Directory.SetCreationTimeUtc(output, created);
                    Directory.SetLastWriteTimeUtc(output, modified);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The conversion itself worked: keep the files, only the dates are new.
            _log.LogWarning(ex, "Could not copy the dates of {Source}", source);
        }
    }

    /// <summary>
    /// Converts one source file along its planned route.
    /// With <paramref name="finalOutput"/> false, results stay in the work directory as <paramref name="intermediate"/>
    /// files (PDF for merging, TIFF pages for a multi-page TIFF).
    /// </summary>
    private async Task<IReadOnlyList<string>> RunFileAsync(
        ConversionJob job, FileResult file, int index, RoutePlanner planner, string workDir, bool finalOutput,
        string intermediate = "pdf")
    {
        var ct = job.CancellationToken;
        // The folders donut: a folder goes into an archive of its own, with its content.
        if (finalOutput && Directory.Exists(file.SourcePath) && _combiner is not null
            && FormatRegistry.FindById(job.Preset.TargetFormat) is { Category: FormatCategory.Archive } archive
            && archive.Id != FormatRegistry.Folder)
        {
            // As many at a time as the archive engine converts (7-Zip uses several threads of its own).
            var gate = _combiner is IConverter engine ? GateFor(engine) : null;
            try
            {
                if (gate is not null)
                    await gate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                file.State = FileState.Cancelled;
                Notify(job);
                return [];
            }
            try
            {
                await PackAsync(job, [file], index, workDir);
            }
            finally
            {
                gate?.Release();
            }
            if (job.Preset.Output.KeepDates && file.State == FileState.Done)
                CopyDates(file.SourcePath, file.Outputs);
            return file.Outputs;
        }
        var source = FormatRegistry.Detect(file.SourcePath);
        if (source is null)
            return Fail(file, "error.unsupported_source", "." + FormatRegistry.ExtensionOf(file.SourcePath));
        // A file just made with "New" in the file manager stays 0 bytes until its app saves it: no engine can read it,
        // and their errors ("Central Directory corrupt") don't say why.
        if (IsEmptyFile(file.SourcePath))
            return Fail(file, "error.empty_file", null);

        var target = !finalOutput ? intermediate
            : job.Preset.TargetFormat == BuiltInData.SameAsSource ? source.Id
            : job.Preset.TargetFormat;

        // "Save every frame": a video becomes a folder of pictures in one step of its own. Never for an intermediate
        // file, and never on the way to another format (video → JPG is otherwise one still frame).
        var route = planner.Plan(source.Id, target, frames: finalOutput && job.Preset.SavesFramesOf(source.Id));
        if (route is null)
            return Fail(file, "error.no_route", $"{source.Id} → {target}");

        // Rotate / flip: only an engine that turns images does it. Without one the file would come out unturned
        // (a PDF or a video re-encoded "as rotated"); with two in the route (EPS → PNG → JPG) it would turn twice.
        // A job with an edit per file runs each file with its own (the preset itself carries none).
        var filePreset = job.EditsByFile is { } edits && edits.TryGetValue(file.SourcePath, out var edit) ? WithEdit(job.Preset, edit) : job.Preset;
        var turnAt = filePreset.Image.TurnsOrMirrors ? route.FirstTransformStep() : -1;
        if (filePreset.Image.TurnsOrMirrors && turnAt < 0)
            return Fail(file, "error.cannot_turn", $"{source.Id} → {target}");
        var unturned = turnAt < 0 ? filePreset : Unturned(filePreset);

        file.State = FileState.Running;
        Notify(job);

        try
        {
            IReadOnlyList<string> inputs = [file.SourcePath];
            FinalAllocator? finalAllocator = null;
            for (var s = 0; s < route.Steps.Count; s++)
            {
                var step = route.Steps[s];
                var isLast = s == route.Steps.Count - 1;
                IOutputAllocator allocator;
                if (isLast && finalOutput)
                    allocator = finalAllocator = CreateFinalAllocator(job, file.SourcePath, index);
                else
                    allocator = new TempAllocator(workDir, FormatRegistry.NameWithoutExtension(file.SourcePath));

                var outputs = new List<string>();
                foreach (var input in inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    var stepIndex = s;
                    var progress = new Progress<double>(p =>
                    {
                        // Reports arrive on the thread pool, possibly out of order: progress only moves forward.
                        var value = (stepIndex + Math.Clamp(p, 0, 1)) / route.Steps.Count;
                        if (value <= file.Progress)
                            return;
                        file.Progress = value;
                        Notify(job);
                    });

                    var gate = GateFor(step.Converter);
                    await gate.WaitAsync(ct);
                    try
                    {
                        // Turned once, in step turnAt: every other step gets the preset without rotation and flip,
                        // also one before it whose engine applies image options but doesn't say it turns.
                        var preset = s == turnAt ? filePreset : unturned;
                        // Playback speed changes once, in the step that writes the output: an animation passed from one
                        // engine to another would otherwise change speed in both.
                        if (preset.Image.ChangesSpeed && !(isLast && finalOutput))
                            preset = AtOriginalSpeed(preset);
                        var stepInfo = new ConversionStep(input, step.From, step.To, preset, allocator, workDir, step.FrameSequence,
                            key => file.NoticeKey = key);
                        outputs.AddRange(await step.Converter.ConvertAsync(stepInfo, progress, ct));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
                inputs = outputs;
            }

            // "Compress" never hands back a bigger file: one that isn't smaller is deleted, and the original stays the result.
            if (finalOutput && job.Preset.Image.OnlyIfSmaller && inputs.Count > 0 && !IsSmaller(inputs, file.SourcePath)
                && TryDiscard(inputs, finalAllocator!))
            {
                file.NoticeKey = FileResult.NotSmallerNotice;
                file.State = FileState.Skipped;
                file.Progress = 1;
                Notify(job);
                return [];
            }
            if (finalOutput)
            {
                file.Outputs.AddRange(inputs);
                if (job.Preset.Output.KeepDates)
                    CopyDates(file.SourcePath, inputs);
            }
            file.State = inputs.Count == 0 && finalOutput ? FileState.Skipped : FileState.Done;
            file.Progress = 1;
            Notify(job);
            return inputs;
        }
        catch (OperationCanceledException)
        {
            file.State = FileState.Cancelled;
            Notify(job);
            return [];
        }
        catch (ConversionRefusedException ex)
        {
            _log.LogInformation("Converting {File} was refused: {Reason}", file.SourcePath, ex.Message);
            return Fail(file, ex.ErrorKey, ex.Detail);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Converting {File} failed", file.SourcePath);
            return Fail(file, "error.conversion_failed", ex.Message);
        }

        static Preset WithEdit(Preset preset, ImageEdit edit)
        {
            var copy = preset.Clone();
            copy.Image.Edit = edit; // the job's own copy: engines only read it
            return copy;
        }

        static Preset Unturned(Preset preset)
        {
            var copy = preset.Clone();
            copy.Image.Rotation = ImageRotation.None;
            copy.Image.Flip = ImageFlip.None;
            // The edit too (crop, hidden boxes, drawings, backdrop): applied once, in the step that transforms, not again
            // by an engine before or after it.
            copy.Image.Edit = null;
            return copy;
        }

        static Preset AtOriginalSpeed(Preset preset)
        {
            var copy = preset.Clone();
            copy.Image.AnimationSpeed = 1;
            return copy;
        }

        IReadOnlyList<string> Fail(FileResult f, string key, string? detail)
        {
            f.State = FileState.Failed;
            f.ErrorKey = key;
            f.ErrorDetail = detail;
            f.Progress = 1;
            Notify(job);
            return [];
        }
    }

    /// <summary>
    /// True when <paramref name="outputs"/> are smaller than <paramref name="source"/> together, and also when either
    /// can't be measured (a folder, a file gone) or an output is the source itself: then the output is kept as it would
    /// be without the check, and the source is never deleted as an output.
    /// </summary>
    private static bool IsSmaller(IReadOnlyList<string> outputs, string source)
    {
        try
        {
            var sourcePath = Path.GetFullPath(source);
            if (outputs.Any(o => string.Equals(Path.GetFullPath(o), sourcePath, StringComparison.OrdinalIgnoreCase)))
                return true;
            return outputs.Sum(o => new FileInfo(o).Length) < new FileInfo(source).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Deletes outputs that are of no use, lets go of their names and removes the folders made for them that are empty now.
    /// False (and everything kept) when an output replaced a file that was there before (the Overwrite policy: that file is
    /// gone, so the output is all there is) or when one can't be deleted.
    /// </summary>
    private bool TryDiscard(IReadOnlyList<string> outputs, FinalAllocator allocator)
    {
        if (allocator.ReplacedExisting)
            return false;
        try
        {
            foreach (var output in outputs)
                File.Delete(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not delete an output that is not smaller than its source; it is kept");
            return false;
        }
        lock (_reservedOutputs)
        {
            foreach (var output in outputs)
                _reservedOutputs.Remove(output);
            foreach (var output in outputs)
                RemoveMadeFolders(allocator.MadeFolders, Path.GetDirectoryName(output));
        }
        return true;
    }

    /// <summary>
    /// Removes <paramref name="folder"/> and the folders above it that the job made, as far up as they are empty. Not
    /// recursive: a folder with anything in it, or one that another output of the job is about to be written to, stays.
    /// Call with the <c>_reservedOutputs</c> lock held.
    /// </summary>
    private void RemoveMadeFolders(HashSet<string> made, string? folder)
    {
        while (folder is not null && made.Contains(folder)
               && !_reservedOutputs.Any(o => string.Equals(Path.GetDirectoryName(o), folder, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(folder); // not recursive: fails while anything is in it
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            made.Remove(folder);
            folder = Path.GetDirectoryName(folder);
        }
    }

    private SemaphoreSlim GateFor(IConverter converter) =>
        _engineGates.GetOrAdd(converter.Id, _ =>
        {
            var max = converter.MaxParallelism <= 0 ? Environment.ProcessorCount : converter.MaxParallelism;
            return new SemaphoreSlim(max, max);
        });

    private FinalAllocator CreateFinalAllocator(ConversionJob job, string sourcePath, int index)
    {
        HashSet<string> madeFolders;
        lock (_reservedOutputs)
        {
            if (!_madeFolders.TryGetValue(job.Id, out madeFolders!))
                _madeFolders[job.Id] = madeFolders = new(StringComparer.OrdinalIgnoreCase);
        }
        return new(this, job.Preset.Output, new OutputPathResolver.Tokens(sourcePath, job.PresetDisplayName, index, job.CreatedAt), madeFolders);
    }

    private void Notify(ConversionJob job)
    {
        try
        {
            JobUpdated?.Invoke(this, job);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "JobUpdated handler threw");
        }
    }

    private async Task WorkLoopAsync()
    {
        await foreach (var job in _channel.Reader.ReadAllAsync())
        {
            try
            {
                if (job.CancellationToken.IsCancellationRequested)
                {
                    job.State = JobState.Cancelled;
                    Notify(job);
                    JobFinished?.Invoke(this, job);
                    continue;
                }
                await RunAsync(job);
            }
            finally
            {
                Interlocked.Decrement(ref _busy);
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* a viewer may still hold a temp file; the OS cleans temp eventually */ }
        catch (UnauthorizedAccessException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _worker;
    }

    /// <summary>Allocates final output paths according to the preset's <see cref="OutputRule"/>.</summary>
    private sealed class FinalAllocator(JobQueue queue, OutputRule rule, OutputPathResolver.Tokens tokens, HashSet<string> madeFolders)
        : IOutputAllocator
    {
        /// <summary>True once a path went to a file that was there already (the Overwrite policy): the output replaces it.</summary>
        public bool ReplacedExisting { get; private set; }

        /// <summary>The folders the job's allocators made, shared between them. Read and changed under the queue's lock.</summary>
        public HashSet<string> MadeFolders => madeFolders;

        public string? Allocate(string extension, string? suffix = null)
        {
            // Reserve under a lock so two parallel files never pick the same name.
            lock (queue._reservedOutputs)
            {
                var path = OutputPathResolver.Resolve(rule, tokens, extension,
                    p => File.Exists(p) || Directory.Exists(p) || queue._reservedOutputs.Contains(p), suffix);
                if (path is null)
                    return null;

                ReplacedExisting |= File.Exists(path) || Directory.Exists(path);
                queue._reservedOutputs.Add(path);
                CreateFolder(Path.GetDirectoryName(path)!);
                // An empty extension asks for a folder (archive extraction): the converter creates it.
                return path;
            }
        }

        /// <summary>Creates <paramref name="folder"/>, remembering which of it and the folders above it did not exist yet.</summary>
        private void CreateFolder(string folder)
        {
            var missing = new List<string>();
            for (var f = folder; !string.IsNullOrEmpty(f) && !Directory.Exists(f); f = Path.GetDirectoryName(f))
                missing.Add(f);
            Directory.CreateDirectory(folder);
            madeFolders.UnionWith(missing);
        }
    }

    /// <summary>Allocates unique paths inside the job's work directory for intermediate files.</summary>
    private sealed class TempAllocator(string workDir, string baseName) : IOutputAllocator
    {
        public string? Allocate(string extension, string? suffix = null)
        {
            var dir = Path.Combine(workDir, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var ext = extension.TrimStart('.');
            return Path.Combine(dir, ext.Length == 0 ? $"{baseName}{suffix}" : $"{baseName}{suffix}.{ext}");
        }
    }
}
