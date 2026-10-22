// What every image tool window shares (crop, add background, redact, annotate): the files it previews one at a time and
// their preview, "1 of 3" and ‹ ›, undo and redo, Reset / Cancel / Copy / Save, and the result it hands back. A tool
// (ViewModels/Tools) adds its own state and panel and fills in what it saves, what Reset does and what an undo step holds.

using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.App.ViewModels.Tools;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.ViewModels;

/// <summary>What an image tool window hands back for its files to run with.</summary>
/// <param name="Shared">One edit for every file (crop, add background, redact).</param>
/// <param name="PerFile">Each file's own edit, by full path (annotate); a file without one is left out of the job.</param>
public sealed record ToolResult(ImageEdit? Shared, IReadOnlyDictionary<string, ImageEdit>? PerFile);

/// <summary>The image tool window's view model: the part every tool shares. <see cref="Create"/> makes the tool's own.</summary>
public abstract partial class ImageToolViewModel : ObservableObject, IDisposable
{
    /// <summary>Longest side of the preview: enough for a window on a 4K screen, far less than a camera's photo.</summary>
    internal const int PreviewEdge = 2048;

    // The undo and redo steps, by file for a tool with an edit per file (UndoPerFile), else all under 0.
    private readonly Dictionary<int, (Stack<object> Undo, Stack<object> Redo)> _history = [];
    private bool _disposed;
    private bool _asking;
    private bool _previewFailed; // the file shown could not be read as a picture

    /// <param name="tool">The tool.</param>
    /// <param name="files">The pictures it edits.</param>
    /// <param name="store">Where the tool's last choices are remembered.</param>
    /// <param name="log">Where choices that can't be remembered are reported.</param>
    protected ImageToolViewModel(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
    {
        Tool = tool;
        Files = files;
        Store = store;
        Loc = loc;
        Log = log;
        (ImageWidth, ImageHeight) = OrientedSize(files[0]);
        _ = LoadPreviewAsync(0);
    }

    /// <summary>
    /// The view model of <paramref name="tool"/> on <paramref name="files"/>; null for a tool whose window is not built
    /// yet, which then opens none (never the crop window under another tool's name).
    /// </summary>
    public static ImageToolViewModel? Create(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc,
        ILogger? log = null) => tool switch
        {
            ImageToolKind.Crop => new CropToolViewModel(files, store, loc, log),
            ImageToolKind.AddBackground => new BackgroundToolViewModel(files, store, loc, log),
            ImageToolKind.Redact => new RedactToolViewModel(files, store, loc, log),
            ImageToolKind.Annotate => new AnnotateToolViewModel(files, store, loc, log),
            _ => null,
        };

    public ImageToolKind Tool { get; }

    /// <summary>The pictures, in the order ‹ › goes through them.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>The file shown, from 0.</summary>
    public int Index { get; private set; }

    public int Count => Files.Count;

    /// <summary>More than one file: the window shows "1 of 3" and the ‹ › buttons.</summary>
    public bool HasMany => Count > 1;

    /// <summary>"Crop · photo.jpg".</summary>
    public string Title => $"{Loc[TitleKey]} · {Path.GetFileName(Files[Index])}";

    /// <summary>"1 of 3".</summary>
    public string PositionText => Loc.Format("tool.position", Index + 1, Count);

    /// <summary>
    /// The note left of the buttons: "Applies to all 3 files", or for one file that the original stays and the result is a
    /// new file beside it (people hesitate to save when they fear the original is overwritten).
    /// </summary>
    public virtual string FooterText => HasMany ? Loc.Format("tool.applies_to_all", Count) : FooterNote ?? Loc["tool.original_kept"];

    /// <summary>
    /// What the footer says for one file instead of "the original stays", where the result goes elsewhere (a capture: the
    /// folder it is saved in). Set before the window opens.
    /// </summary>
    public string? FooterNote { get; set; }

    /// <summary>
    /// Asks the user a yes / no question, such as whether to throw drawings away (the window service sets it: a dialog over
    /// the window). Null: there is no one to ask, and the answer is yes.
    /// </summary>
    public Func<string, Task<bool>>? Ask { get; set; }

    /// <summary>
    /// Puts a file with an edit drawn on it on the clipboard; true when it did (the window service sets it: the window's
    /// clipboard). Null: there is no clipboard, and Copy does nothing.
    /// </summary>
    public Func<string, ImageEdit, Task<bool>>? CopyPicture { get; set; }

    /// <summary>How long Copy's button says "Copied", and so how long the window waits before it closes after a copy.</summary>
    internal TimeSpan CopiedHold { get; set; } = TimeSpan.FromSeconds(0.7);

    /// <summary>Copy's button: "Copy", or for a moment after a copy, "Copied".</summary>
    public string CopyText => Loc[JustCopied ? "toast.copied" : "toast.copy"];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CopyText))]
    private bool _justCopied;

    private int _copiedRound;

    /// <summary>
    /// True from Copy until the picture is on the clipboard, and then until the window closes after it: the window takes
    /// no input meanwhile, so nothing is edited that the copy would not hold or that closing would throw away, and a
    /// second Ctrl+C starts no second copy.
    /// </summary>
    [ObservableProperty] private bool _isCopying;

    /// <summary>Size of the file shown, the way it stands (EXIF orientation applied); 0 when it can't be read.</summary>
    public int ImageWidth { get; private set; }

    /// <inheritdoc cref="ImageWidth"/>
    public int ImageHeight { get; private set; }

    /// <summary>The short side of the file shown, full size: what the renderer sizes cells, pens and padding by.</summary>
    public int FullShortSide => Math.Min(ImageWidth, ImageHeight);

    /// <summary>The file shown, turned upright and made smaller; null while it is read (or when it can't be).</summary>
    [ObservableProperty] private Bitmap? _preview;

    /// <summary>
    /// What the middle of the window says while there is no picture: that it is being read, or that it can't be shown (a
    /// damaged file would otherwise leave a grey blank for ever). Empty once there is a picture.
    /// </summary>
    public string PreviewStatus => Preview is not null ? "" : _previewFailed ? Loc["tool.preview_failed"] : Loc["tool.preview_loading"];

    partial void OnPreviewChanged(Bitmap? value) => OnPropertyChanged(nameof(PreviewStatus));

    /// <summary>
    /// The same preview for Skia: what <see cref="EditPreviewDrawOperation"/> draws the tool's edit on. It comes and goes
    /// with <see cref="Preview"/>.
    /// </summary>
    [ObservableProperty] private SKImage? _previewImage;

    /// <summary>The result to run, set by Save; null after Cancel (or when the window was closed).</summary>
    public ToolResult? Result { get; private set; }

    /// <summary>Save or Cancel: the window closes.</summary>
    public event EventHandler? Finished;

    /// <summary>
    /// Another file is about to be shown (<see cref="Index"/> is still the one before): the window has the picture view
    /// end what it is in the middle of on this file (annotate: a text being typed goes on it, a drag is dropped).
    /// </summary>
    public event EventHandler? FileLeaving;

    /// <summary>The store the tool remembers its last choices in; Save writes it.</summary>
    protected UserDataStore Store { get; }

    protected ILocalizer Loc { get; }

    protected ILogger? Log { get; }

    /// <summary>The tool's name in the title bar, e.g. <c>tool.crop.title</c>.</summary>
    protected abstract string TitleKey { get; }

    /// <summary>What Save hands back, for every file; the tool also sets its choices in <see cref="Store"/> here.</summary>
    protected abstract ToolResult? BuildResult();

    /// <summary>The tool's starting state again (Reset), as one undo step.</summary>
    protected abstract void ResetTool();

    /// <summary>The tool's state now, as an undo step.</summary>
    protected abstract object Snapshot();

    /// <summary>Puts back a state <see cref="Snapshot"/> took.</summary>
    protected abstract void Restore(object snapshot);

    /// <summary>Another file is shown (<see cref="Index"/> and its size are already its own).</summary>
    protected virtual void OnFileShown()
    {
    }

    /// <summary>
    /// False while the tool has nothing to hand back (redact before its first box): Save is off, and Enter does nothing
    /// either. A tool whose answer changes calls <see cref="NotifyCanSaveChanged"/>.
    /// </summary>
    protected virtual bool CanSave => true;

    /// <summary>
    /// True for a tool whose files each have an edit of their own (annotate): each file keeps its own undo steps, and
    /// <see cref="Snapshot"/> and <see cref="Restore"/> are the state of the file shown. False: one history for all.
    /// </summary>
    protected virtual bool UndoPerFile => false;

    /// <summary>
    /// Whether Cancel (and Esc) may close the window now; a tool with work to lose asks through <see cref="Ask"/>.
    /// True by default: nothing is asked.
    /// </summary>
    protected virtual Task<bool> ConfirmCloseAsync() => Task.FromResult(true);

    /// <summary>A key the window doesn't use (1 to 7 for a ratio, say); true when the tool took it. Not called while typing.</summary>
    public virtual bool HandleKey(Key key, KeyModifiers modifiers) => false;

    /// <summary>Call when <see cref="CanSave"/> may have changed, so the Save and Copy buttons follow.</summary>
    protected void NotifyCanSaveChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Call before a change by the user: the state until now becomes one undo step.</summary>
    protected void RecordStep()
    {
        var (undo, redo) = History;
        undo.Push(Snapshot());
        redo.Clear();
        NotifyHistoryChanged();
    }

    /// <summary>The undo and redo buttons follow what the file shown has to undo and redo.</summary>
    private void NotifyHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The undo and redo steps of the file shown (of every file, unless <see cref="UndoPerFile"/>).</summary>
    private (Stack<object> Undo, Stack<object> Redo) History
    {
        get
        {
            var key = UndoPerFile ? Index : 0;
            if (!_history.TryGetValue(key, out var history))
                _history[key] = history = (new Stack<object>(), new Stack<object>());
            return history;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Show(Index + 1);

    private bool CanGoNext => Index < Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Show(Index - 1);

    private bool CanGoPrevious => Index > 0;

    [RelayCommand]
    private void Reset() => ResetTool();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var (undo, redo) = History;
        if (undo.TryPop(out var step))
        {
            redo.Push(Snapshot());
            Restore(step);
        }
        NotifyHistoryChanged();
    }

    private bool CanUndo => History.Undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var (undo, redo) = History;
        if (redo.TryPop(out var step))
        {
            undo.Push(Snapshot());
            Restore(step);
        }
        NotifyHistoryChanged();
    }

    private bool CanRedo => History.Redo.Count > 0;

    /// <summary>Hands the result back and remembers the tool's choices for next time.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // Enter and a double click call Execute, which does not ask CanExecute.
        if (!CanSave)
            return;
        Result = BuildResult();
        RememberChoices();
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts the file shown, edited as the window shows it, on the clipboard instead of saving a file, and remembers the
    /// tool's choices as Save does. Then a window of one file closes, unless the user turned that off
    /// (<see cref="ImageToolSettings.CloseAfterCopy"/>): it stays, to edit more or save a file too. A window of several
    /// files always stays. A copy that failed (the clipboard was busy) leaves it as it was.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Copy()
    {
        // Ctrl+C calls Execute, which does not ask CanExecute.
        if (!CanSave || CopyPicture is null || IsCopying)
            return;
        IsCopying = true;
        try
        {
            var result = BuildResult();
            var file = Files[Index];
            // Annotate: a file shown without drawings of its own goes as it is, as the window shows it.
            var edit = result?.Shared ?? result?.PerFile?.GetValueOrDefault(file) ?? new ImageEdit();
            RememberChoices();
            if (!await CopyPicture(file, edit))
                return;
            var round = ++_copiedRound;
            JustCopied = true;
            // Several files: the next can be copied too, and closing would throw away the other files' own edits (annotate).
            if (!Store.Settings.ImageTools.CloseAfterCopy || HasMany)
            {
                _ = ForgetCopiedAsync(round);
                return;
            }
            await Task.Delay(CopiedHold); // long enough to read "Copied"
            // Closed meanwhile (the title bar's X): there is nothing more to do.
            if (!_disposed && Result is null)
                Finished?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsCopying = false;
        }
    }

    /// <summary>"Copy" again a moment later; a copy made meanwhile gets its own full moment.</summary>
    private async Task ForgetCopiedAsync(int round)
    {
        await Task.Delay(CopiedHold);
        if (round == _copiedRound)
            JustCopied = false;
    }

    private void RememberChoices()
    {
        try
        {
            Store.SaveSettings();
        }
        catch (Exception ex)
        {
            // A full disk or a locked settings file: the edit still runs, only the choices are not remembered on disk.
            Log?.LogWarning(ex, "Could not save the {Tool} tool's settings", Tool);
        }
    }

    /// <summary>Closes with nothing to run, once the tool agrees (<see cref="ConfirmCloseAsync"/>).</summary>
    [RelayCommand]
    private async Task Cancel()
    {
        // Esc pressed again while the question is up asks nothing more.
        if (_asking)
            return;
        _asking = true;
        try
        {
            if (!await ConfirmCloseAsync())
                return;
        }
        finally
        {
            _asking = false;
        }
        Result = null;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Goes to another file; its picture is read now.</summary>
    private void Show(int index)
    {
        if (index < 0 || index >= Count || index == Index)
            return;
        FileLeaving?.Invoke(this, EventArgs.Empty);
        Index = index;
        (ImageWidth, ImageHeight) = OrientedSize(Files[index]);
        _previewFailed = false;
        SetPreview(null);
        OnFileShown();
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        OnPropertyChanged(nameof(FullShortSide));
        OnPropertyChanged(nameof(PreviewStatus)); // "reading", also when the file before could not be read
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NotifyHistoryChanged(); // the steps are the new file's own for a tool with an edit per file
        _ = LoadPreviewAsync(index);
    }

    private async Task LoadPreviewAsync(int index)
    {
        (Bitmap, SKImage)? read = null;
        try
        {
            var path = Files[index];
            read = await Task.Run(() => ReadPreview(path));
        }
        catch (Exception)
        {
            // Damaged, gone or locked since the donut looked at it: no picture, the edit still goes on it.
        }
        // Another file was asked for meanwhile, or the window is gone.
        if (_disposed || index != Index)
        {
            read?.Item1.Dispose();
            EditPreviewDrawOperation.Release(read?.Item2);
            return;
        }
        _previewFailed = read is null;
        SetPreview(read);
        OnPropertyChanged(nameof(PreviewStatus)); // also when there is still no picture: "cannot be shown"
    }

    /// <summary>Shows a new preview (or none) and lets go of the one before.</summary>
    private void SetPreview((Bitmap Bitmap, SKImage Image)? preview)
    {
        var (bitmap, image) = (Preview, PreviewImage);
        Preview = preview?.Bitmap;
        PreviewImage = preview?.Image;
        bitmap?.Dispose();
        EditPreviewDrawOperation.Release(image); // a frame being drawn finishes with it first
    }

    /// <summary>
    /// The size of <paramref name="path"/> the way it stands, from its header: orientations 5 to 8 turn it a quarter,
    /// so it shows as wide as it is stored tall. (0, 0) when it can't be read.
    /// </summary>
    internal static (int Width, int Height) OrientedSize(string path)
    {
        try
        {
            var info = new MagickImageInfo(path, ImageReader.Settings(FormatRegistry.Detect(path)?.Id ?? "", new Preset()));
            var (w, h) = ((int)info.Width, (int)info.Height);
            return info.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom
                or OrientationType.LeftBottom ? (h, w) : (w, h);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The picture read the way the engine reads it (its first frame only), turned upright, at most
    /// <see cref="PreviewEdge"/> pixels on its long side, handed over as saving hands it to the renderer (straight RGBA,
    /// see EditBridge). One decode gives both: the bitmap the crop box is drawn over and the picture the renderer draws on.
    /// </summary>
    private static (Bitmap, SKImage) ReadPreview(string path)
    {
        using var frames = ImageReader.Read(path, FormatRegistry.Detect(path)?.Id ?? "", new Preset(), firstFrameOnly: true);
        var image = frames[0];
        image.AutoOrient();
        if (Math.Max(image.Width, image.Height) > PreviewEdge)
            image.Resize(new MagickGeometry(PreviewEdge, PreviewEdge)); // fits inside, keeping the shape
        using var pixels = EditBridge.ToBitmap(image);
        var bitmap = new Bitmap(PixelFormats.Rgba8888, AlphaFormat.Unpremul, pixels.GetPixels(),
            new Avalonia.PixelSize(pixels.Width, pixels.Height), new Avalonia.Vector(96, 96), pixels.RowBytes);
        pixels.SetImmutable(); // so the picture shares its pixels instead of copying them
        return (bitmap, SKImage.FromBitmap(pixels));
    }

    /// <summary>Lets go of the preview; a tool with files of its own to clear away overrides this and calls it.</summary>
    public virtual void Dispose()
    {
        _disposed = true;
        SetPreview(null);
    }
}
