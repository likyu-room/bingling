// One running / finished conversion as shown in the toast window.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class JobViewModel : ObservableObject
{
    /// <summary>Side of the card's thumbnail, in device-independent pixels (ToastWindow.axaml draws it 56 × 56).</summary>
    private const uint ThumbnailEdge = 56;

    /// <summary>
    /// The thumbnail's long side is at most this many times its short side: the card shows only the middle square, and a
    /// 20000 × 100 panorama would otherwise come out 11200 × 56.
    /// </summary>
    private const uint ThumbnailMaxAspect = 4;

    /// <summary>How long the Copy button says "Copied".</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.6);

    private readonly ILocalizer _loc;
    private readonly IPlatformServices _platform;
    private readonly Action<JobViewModel> _dismiss;
    private readonly Action<JobViewModel>? _retry;
    private readonly Action<string> _open;
    private readonly ILogger? _log;
    private int _refreshPending;
    private int _copiedRound;
    private bool _copying;
    private bool _detailsRequested;

    /// <summary>
    /// The size of each source file (as <see cref="ConversionJob.Sources"/>) when the job was handed over: a watch folder
    /// may move the originals away once it is done, before the card compares the sizes.
    /// </summary>
    private readonly long[] _sourceSizes;

    /// <param name="open">Opens a file in the app the system has for it; null for the system's own way (tests see what was opened).</param>
    /// <param name="log">Where a result that could not be opened or copied is reported (the card itself stays silent about it).</param>
    public JobViewModel(ConversionJob job, ILocalizer loc, IPlatformServices platform, Action<JobViewModel> dismiss,
        Action<JobViewModel>? retry = null, Action<string>? open = null, ILogger? log = null)
    {
        Job = job;
        _loc = loc;
        _platform = platform;
        _dismiss = dismiss;
        _retry = retry;
        _open = open ?? OpenWithSystemApp;
        _log = log;
        _sourceSizes = [.. job.Sources.Select(SizeOf)];
        var what = job.Sources.Count == 1 ? Path.GetFileName(job.Sources[0]) : loc.Format("home.files", job.Sources.Count);
        Title = loc.Format("toast.converting", what, job.PresetDisplayName);
        Refresh();
    }

    public ConversionJob Job { get; }

    public string Title { get; }

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    [NotifyPropertyChangedFor(nameof(ShowsStatusBeside))]
    private bool _isRunning = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    [NotifyPropertyChangedFor(nameof(ShowsStatusBeside))]
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private bool _isSucceeded;

    /// <summary>
    /// A small picture of the first output (upright, the card's thumbnail size), once the job is over and that output is a
    /// still image Filee can read; null otherwise and the card shows <see cref="FormatIcon"/> in a circle instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private Bitmap? _thumbnail;

    /// <summary>
    /// The card offers Copy: the job went well and made a picture. A card that failed in part shows Retry and Show beside its
    /// status already, and a third button does not fit there in every language.
    /// </summary>
    public bool CanCopy => IsSucceeded && Thumbnail is not null;

    /// <summary>
    /// Done without a problem and its first result is a picture: a job the donut finished at once still goes on a card
    /// (RadialController), so the picture can be seen, copied or opened without looking for the file.
    /// </summary>
    public bool HasPictureResult => Job.State == JobState.Completed && Job.Outputs.FirstOrDefault() is { } first
                                    && FormatRegistry.Detect(first)?.Category == FormatCategory.Image;

    /// <summary>True for a moment after the results were copied: the Copy button says "Copied" and then goes back.</summary>
    [ObservableProperty] private bool _justCopied;

    /// <summary>"1.2 MB → 340.5 KB · 72% smaller" once the job is over; null when the sizes can't be told.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    [NotifyPropertyChangedFor(nameof(ShowsStatusBeside))]
    private string? _sizeChange;

    /// <summary>
    /// The line under the title is <see cref="Status"/> unless the sizes replace it. Failures keep it (they say how many),
    /// and so does a cancelled job that wrote some files before it stopped (it says so).
    /// </summary>
    public bool ShowsStatus => SizeChange is null || HasErrors || Job.State == JobState.Cancelled;

    /// <summary>
    /// The status is shown beside the buttons, as "37%" or "Cancelled" is. A failure's ("1 of 2 failed" is long) is not: the card
    /// gives it a line of its own (<see cref="HasErrors"/>), as Retry and Show leave it too little room.
    /// </summary>
    public bool ShowsStatusBeside => ShowsStatus && !HasErrors;

    /// <summary>
    /// The icon of what the job makes (the first output's format, before there is one the preset's); a plain file for a
    /// format without one of its own (CAD, fonts), so the circle is never empty.
    /// </summary>
    public Geometry? FormatIcon => DonutIcons.ForFormat(
        FormatRegistry.Detect(Job.Outputs.FirstOrDefault() ?? "")?.Id ?? Job.Preset.TargetFormat) ?? DonutIcons.AnyFile;

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

        // Every file left out for the same reason ("not smaller"): that reason is the status, not "the file exists".
        var allSkipped = Job.Files.All(f => f.State == FileState.Skipped);
        var skippedFor = allSkipped && Job.Files.Select(f => f.NoticeKey).Distinct().ToList() is [{ } key] ? key : null;
        var noted = Job.Files.Where(f => f.NoticeKey is not null
            && (f.State == FileState.Done || (f.State == FileState.Skipped && skippedFor is null))).ToList();
        HasNotices = noted.Count > 0;
        Notices = noted.Count == 0 ? null : string.Join(Environment.NewLine,
            noted.Select(f => $"{Path.GetFileName(f.SourcePath)} — {_loc[f.NoticeKey!]}"));

        Status = Job.State switch
        {
            JobState.Completed when allSkipped => _loc[skippedFor ?? "toast.skipped"],
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
        var before = Job.Files.Select((f, i) => f.State is FileState.Failed or FileState.Skipped or FileState.Cancelled ? 0 : _sourceSizes[i]).Sum();
        var (thumbnail, sizeChange) = await Task.Run(() => (ReadThumbnail(outputs.FirstOrDefault(), scaling), FormatSizeChange(before, outputs)));
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
    /// The first frame of the image at <paramref name="path"/>, upright, shrunk until its short side is the thumbnail's (the
    /// card cuts the long side to its square, and a panorama is cut to <see cref="ThumbnailMaxAspect"/> before), as a bitmap
    /// (through PNG in memory). Null for anything else or when it can't be read.
    /// </summary>
    private static Bitmap? ReadThumbnail(string? path, double scaling)
    {
        if (path is null || FormatRegistry.Detect(path) is not { Category: FormatCategory.Image } format)
            return null;
        try
        {
            using var frames = ImageReader.Read(path, format.Id, new Preset(), firstFrameOnly: true);
            var image = frames[0];
            image.AutoOrient();
            var (shortSide, longSide) = (Math.Min(image.Width, image.Height), Math.Max(image.Width, image.Height));
            if (longSide > ThumbnailMaxAspect * shortSide)
            {
                // The middle of the long side: the part of it the card shows.
                var keep = ThumbnailMaxAspect * shortSide;
                image.Crop(new MagickGeometry(image.Width > image.Height ? keep : image.Width, image.Width > image.Height ? image.Height : keep),
                    Gravity.Center);
                image.ResetPage();
            }
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
    /// "before → after · N% smaller" (or larger): <paramref name="before"/> is the size the converted files had when the job
    /// was handed over, after the size of the outputs as they are now. Null when either side has nothing to measure. A
    /// change that rounds to 0% reads as "0% smaller" whichever way it went.
    /// </summary>
    private string? FormatSizeChange(long before, IEnumerable<string> outputs)
    {
        try
        {
            var after = outputs.Sum(SizeOf);
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

    /// <summary>The size of a file; 0 for a folder, a file that is gone or one that can't be looked at.</summary>
    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true } info ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var first = Job.Outputs.FirstOrDefault();
        if (first is not null)
            _platform.RevealInFileManager(first);
    }

    /// <summary>Opens the first result in the app the system has for it: the card's thumbnail is the button.</summary>
    [RelayCommand]
    private void OpenResult()
    {
        if (Job.Outputs.FirstOrDefault() is not { } first)
            return;
        try
        {
            _open(first);
        }
        catch (Exception ex)
        {
            // The file is gone, or no app takes it: the card stays as it is (and "Show" still leads to the folder); the log has why.
            _log?.LogWarning(ex, "Could not open the result {Path}", first);
        }
    }

    private static void OpenWithSystemApp(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>
    /// Copies the results to <paramref name="clipboard"/> (<see cref="ResultClipboard"/>) and, when something was copied, says so for
    /// a moment (<see cref="JustCopied"/>). A press while an earlier copy is still being made does nothing: a camera's photo takes
    /// a while, and a second press would read it all over again. UI thread only.
    /// </summary>
    /// <returns>True when something was copied; false when nothing was (a copy is under way, or the results are gone).</returns>
    public async Task<bool> CopyResultAsync(IClipboard clipboard, IStorageProvider storage)
    {
        if (_copying)
            return false;
        _copying = true;
        try
        {
            if (!await ResultClipboard.CopyAsync(clipboard, storage, [.. Job.Outputs], _log))
                return false;
        }
        finally
        {
            _copying = false;
        }
        _ = ShowCopiedAsync(); // not awaited: pressing again while it says "Copied" is a new copy, not a wait
        return true;
    }

    /// <summary>
    /// True while a copy is being made (<see cref="CopyResultAsync"/>).
    /// </summary>
    public bool IsCopying => _copying;

    /// <summary>
    /// Makes <see cref="JustCopied"/> true for <paramref name="hold"/> (a moment by default). Copying again meanwhile gives
    /// the new copy its own full moment: the earlier one does not cut it short. UI thread only.
    /// </summary>
    public async Task ShowCopiedAsync(TimeSpan? hold = null)
    {
        var round = ++_copiedRound;
        JustCopied = true;
        await Task.Delay(hold ?? CopiedFor);
        if (round == _copiedRound)
            JustCopied = false;
    }

    [RelayCommand]
    private void Cancel() => Job.Cancel();

    [RelayCommand]
    private void Dismiss() => _dismiss(this);

    /// <summary>Converts the files that failed again (a new job in the toast in place of this one).</summary>
    [RelayCommand]
    private void Retry() => _retry?.Invoke(this);
}
