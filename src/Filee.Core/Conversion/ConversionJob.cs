// State of one "drop" — a batch of files converted with one preset.

using Filee.Core.Presets;

namespace Filee.Core.Conversion;

public enum JobState
{
    Queued,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled,
}

public enum FileState
{
    Pending,
    Running,
    Done,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>Result for one source file of a job.</summary>
public sealed class FileResult
{
    public required string SourcePath { get; init; }
    public FileState State { get; set; } = FileState.Pending;
    public double Progress { get; set; }
    public List<string> Outputs { get; } = [];

    /// <summary>Localization key of the error (e.g. <c>error.no_route</c>), if any.</summary>
    public string? ErrorKey { get; set; }

    /// <summary>Raw error detail for logs / tooltips.</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Localization key of a note on a file that converted (e.g. <c>notice.pdf_not_smaller</c>: compressing saved
    /// nothing, so the PDF was copied as it is), if any.
    /// </summary>
    public string? NoticeKey { get; set; }

    /// <summary>
    /// The <see cref="NoticeKey"/> of a file left out because its output was not smaller than the source
    /// (<see cref="ImageOptions.OnlyIfSmaller"/>): nothing was kept and the original stays the result.
    /// </summary>
    public const string NotSmallerNotice = "toast.skipped_not_smaller";

    /// <summary>
    /// True for a file that needs no second look: converted, or left out because its output was not smaller. A watch
    /// folder moves the originals of such files away; one left in the folder would be tried again at every start.
    /// </summary>
    public bool IsHandled => State == FileState.Done || (State == FileState.Skipped && NoticeKey == NotSmallerNotice);
}

/// <summary>A batch of files converted with one preset.</summary>
public sealed class ConversionJob
{
    private readonly CancellationTokenSource _cts = new();

    /// <param name="editsByFile">
    /// For a tool that makes one edit per picture (annotate): the edit of each source file, by its full path, any case.
    /// Files without one are not in <paramref name="sources"/>; <c>ConversionService.Start</c> leaves them out.
    /// </param>
    public ConversionJob(IReadOnlyList<string> sources, Preset preset, string presetDisplayName,
        IReadOnlyDictionary<string, ImageEdit>? editsByFile = null)
    {
        Sources = sources;
        Preset = preset.Clone(); // later edits of the preset must not affect a running job
        PresetDisplayName = presetDisplayName;
        Files = sources.Select(s => new FileResult { SourcePath = s }).ToList();
        if (editsByFile is not null)
        {
            // Copies, for the same reason as the preset's.
            var edits = new Dictionary<string, ImageEdit>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, edit) in editsByFile)
                edits[path] = edit.Clone();
            EditsByFile = edits;
        }
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..10];
    public DateTime CreatedAt { get; } = DateTime.Now;
    public IReadOnlyList<string> Sources { get; }
    public Preset Preset { get; }

    /// <summary>
    /// The edit each source file runs with, by its full path (any case), or null when every file runs with the
    /// <see cref="Preset"/>'s own <see cref="ImageOptions.Edit"/>. Each file takes its edit in place of that one.
    /// </summary>
    public IReadOnlyDictionary<string, ImageEdit>? EditsByFile { get; }

    public string PresetDisplayName { get; }
    public IReadOnlyList<FileResult> Files { get; }
    public JobState State { get; internal set; } = JobState.Queued;

    /// <summary>Overall progress 0..1.</summary>
    public double Progress => Files.Count == 0 ? 1 : Files.Average(f => f.Progress);

    /// <summary>Every file written by the job.</summary>
    public IEnumerable<string> Outputs => Files.SelectMany(f => f.Outputs);

    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>Requests cancellation. Running engines stop as soon as they can.</summary>
    public void Cancel() => _cts.Cancel();
}
