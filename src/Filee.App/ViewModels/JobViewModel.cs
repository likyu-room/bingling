// One running / finished conversion as shown in the toast window.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Engines.Magick;
using ImageMagick;

namespace Filee.App.ViewModels;

public sealed partial class JobViewModel : ObservableObject
{
    /// <summary>Side of the card's thumbnail, in device-independent pixels (ToastWindow.axaml draws it 56 × 56).</summary>
    private const uint ThumbnailEdge = 56;

    private readonly ILocalizer _loc;
    private readonly IPlatformServices _platform;
    private readonly Action<JobViewModel> _dismiss;
    private readonly Action<JobViewModel>? _retry;
    private int _refreshPending;
    private bool _detailsRequested;

    public JobViewModel(ConversionJob job, ILocalizer loc, IPlatformServices platform, Action<JobViewModel> dismiss,
        Action<JobViewModel>? retry = null)
    {
        Job = job;
        _loc = loc;
        _platform = platform;
        _dismiss = dismiss;
        _retry = retry;
        var what = job.Sources.Count == 1 ? Path.GetFileName(job.Sources[0]) : loc.Format("home.files", job.Sources.Count);
        Title = loc.Format("toast.converting", what, job.PresetDisplayName);
        Refresh();
    }

    public ConversionJob Job { get; }

    public string Title { get; }

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    private bool _hasErrors;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatIcon))]
    private bool _hasOutputs;
    [ObservableProperty] private string? _errorDetails;
    [ObservableProperty] private bool _canRetry;

    /// <summary>Notes on files that converted (e.g. a PDF that couldn't be made smaller and was copied), one per line.</summary>
    [ObservableProperty] private string? _notices;
    [ObservableProperty] private bool _hasNotices;
    /// <summary>Extensions of failed files Filee can't read (".ncm"), for "Request this format".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequestFormat))]
    private IReadOnlyList<string> _unknownExtensions = [];

    public bool CanRequestFormat => UnknownExtensions.Count > 0;

    /// <summary>True once the job is over and every file converted: the card then wears a check.</summary>
    [ObservableProperty] private bool _isSucceeded;

    /// <summary>
    /// A small picture of the first output (upright, the card's thumbnail size), once the job is over and that output is a
    /// still image Filee can read; null otherwise and the card shows <see cref="FormatIcon"/> in a circle instead.
    /// </summary>
    [ObservableProperty] private Bitmap? _thumbnail;

    /// <summary>"1.2 MB → 340.5 KB · 72% smaller" once the job is over; null when the sizes can't be told.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    private string? _sizeChange;

    /// <summary>The line under the title is <see cref="Status"/> unless the sizes replace it (failures keep it: they say how many).</summary>
    public bool ShowsStatus => SizeChange is null || HasErrors;

    /// <summary>The icon of what the job makes (the first output's format, before there is one the preset's), or null for none.</summary>
    public Geometry? FormatIcon => DonutIcons.ForFormat(
        FormatRegistry.Detect(Job.Outputs.FirstOrDefault() ?? "")?.Id ?? Job.Preset.TargetFormat);

    /// <summary>Source files that failed.</summary>
    public IEnumerable<string> FailedSources => Job.Files.Where(f => f.State == FileState.Failed).Select(f => f.SourcePath);

    /// <summary>Marks a refresh as pending; returns false if one is already queued (coalesces UI updates).</summary>
    internal bool TryMarkPending() => Interlocked.Exchange(ref _refreshPending, 1) == 0;

    /// <summary>Copies job state into bindable properties. UI thread only.</summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref _refreshPending, 0);
        Progress = Job.Progress * 100;
        IsRunning = Job.State is JobState.Queued or JobState.Running;
        HasOutputs = Job.Outputs.Any();
        IsSucceeded = Job.State == JobState.Completed && HasOutputs;
        // Once, when there is something to show and the sizes are final (a running multi-file job has only some outputs).
        if (HasOutputs && !IsRunning && !_detailsRequested)
        {
            _detailsRequested = true;
            _ = LoadFinishDetailsAsync();
        }

        var failed = Job.Files.Where(f => f.State == FileState.Failed).ToList();
        HasErrors = failed.Count > 0;
        ErrorDetails = failed.Count == 0 ? null : string.Join(Environment.NewLine,
            failed.Select(f => $"{Path.GetFileName(f.SourcePath)} — {FailureText.Reason(_loc, f.ErrorKey, f.ErrorDetail)}"));
        CanRetry = _retry is not null && !IsRunning && failed.Count > 0;
        IReadOnlyList<string> unknown = IsRunning ? [] : FormatRequest.UnknownExtensions(
            failed.Where(f => f.ErrorKey == "error.unsupported_source").Select(f => f.SourcePath));
        if (!unknown.SequenceEqual(UnknownExtensions))
            UnknownExtensions = unknown;

        var noted = Job.Files.Where(f => f.State == FileState.Done && f.NoticeKey is not null).ToList();
        HasNotices = noted.Count > 0;
        Notices = noted.Count == 0 ? null : string.Join(Environment.NewLine,
            noted.Select(f => $"{Path.GetFileName(f.SourcePath)} — {_loc[f.NoticeKey!]}"));

        Status = Job.State switch
        {
            JobState.Completed when Job.Files.All(f => f.State == FileState.Skipped) => _loc["toast.skipped"],
            JobState.Completed => _loc["toast.done"],
            JobState.CompletedWithErrors => _loc.Format("toast.done_errors", failed.Count, Job.Files.Count),
            JobState.Failed => _loc["toast.failed"],
            JobState.Cancelled => _loc["toast.cancelled"],
            _ => $"{Progress:0}%",
        };
    }

    /// <summary>
    /// Reads what the finished card shows besides the status: a thumbnail of the first output and how the size changed.
    /// The files are read on another thread and closed again before this returns, so the user may move or delete them at
    /// once. A file that can't be read (damaged, gone) just leaves that part out; the card stays as it was.
    /// </summary>
    internal async Task LoadFinishDetailsAsync()
    {
        // The screen's scale comes from a window: ask on the UI thread, before the work moves off it.
        var scaling = ScreenScaling();
        var outputs = Job.Outputs.ToList();
        var converted = Job.Files.Where(f => f.State is not (FileState.Failed or FileState.Skipped or FileState.Cancelled))
            .Select(f => f.SourcePath).ToList();
        var (thumbnail, sizeChange) = await Task.Run(() => (ReadThumbnail(outputs.FirstOrDefault(), scaling), FormatSizeChange(converted, outputs)));
        // ponytail: the bitmap lives until the garbage collector takes it (tens of KB per card); dispose it when the card goes if that adds up.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Thumbnail = thumbnail;
            SizeChange = sizeChange;
        });
    }

    /// <summary>The primary screen's scale (the toast lives there); 1 when no window is open to ask.</summary>
    private static double ScreenScaling() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .Select(w => w.Screens.Primary?.Scaling).FirstOrDefault(s => s is not null) ?? 1;

    /// <summary>
    /// The still image at <paramref name="path"/>, upright, shrunk until its short side is the thumbnail's (the card cuts the
    /// long side to its square), as a bitmap (through PNG in memory). Null for anything else or when it can't be read.
    /// </summary>
    private static Bitmap? ReadThumbnail(string? path, double scaling)
    {
        if (path is null || FormatRegistry.Detect(path) is not { Category: FormatCategory.Image } format)
            return null;
        try
        {
            using var frames = ImageReader.Read(path, format.Id, new Preset());
            var image = frames[0];
            image.AutoOrient();
            var edge = (uint)Math.Ceiling(ThumbnailEdge * scaling);
            image.Resize(new MagickGeometry(edge, edge) { FillArea = true }); // covers the square, keeping the shape
            image.Quality = 10; // zlib level 1, no filter: the PNG only lives until Avalonia has decoded it
            using var png = new MemoryStream();
            image.Write(png, MagickFormat.Png);
            png.Position = 0;
            return new Bitmap(png);
        }
        catch (Exception)
        {
            // Damaged, gone or locked: no picture, the card keeps its icon.
            return null;
        }
    }

    /// <summary>
    /// "before → after · N% smaller" (or larger) for the files that converted, with the sizes of the files as they are now.
    /// Null when either side has no file left to measure. A change that rounds to 0% reads as "0% smaller" whichever way it went.
    /// </summary>
    private string? FormatSizeChange(IEnumerable<string> sources, IEnumerable<string> outputs)
    {
        try
        {
            long before = Measure(sources), after = Measure(outputs);
            if (before <= 0 || after <= 0)
                return null;
            var percent = (int)Math.Round(Math.Abs(after - before) * 100.0 / before, MidpointRounding.AwayFromZero);
            return _loc.Format(after > before && percent > 0 ? "toast.size_larger" : "toast.size_change",
                ByteSize.Format(before), ByteSize.Format(after), percent);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Total size of the files that exist (folders and missing files count nothing).</summary>
    private static long Measure(IEnumerable<string> paths) => paths.Where(File.Exists).Sum(p => new FileInfo(p).Length);

    [RelayCommand]
    private void OpenFolder()
    {
        var first = Job.Outputs.FirstOrDefault();
        if (first is not null)
            _platform.RevealInFileManager(first);
    }

    [RelayCommand]
    private void Cancel() => Job.Cancel();

    [RelayCommand]
    private void Dismiss() => _dismiss(this);

    /// <summary>Converts the files that failed again (a new job in the toast in place of this one).</summary>
    [RelayCommand]
    private void Retry() => _retry?.Invoke(this);
}
