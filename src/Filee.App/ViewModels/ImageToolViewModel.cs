// The image tool window (the crop so far): the files it previews one at a time, the box and its ratio, the size the
// panel shows, undo and redo, and the edit it hands back. One box goes on every file: another file only shows it
// refitted there (CropMath.Refit), which is what the engine does with it when it cuts that file, so every preview is
// the result.

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class ImageToolViewModel : ObservableObject, IDisposable
{
    /// <summary>Longest side of the preview: enough for a window on a 4K screen, far less than a camera's photo.</summary>
    private const int PreviewEdge = 2048;

    private readonly IReadOnlyList<string> _files;
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly ILogger? _log;
    private readonly Stack<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private Choice<string> _aspect;
    private CropRect _box;  // the box as last set, on whichever file: the one that goes on every file
    private CropRect _crop; // _box on the file shown
    private bool _disposed;

    /// <summary>One undo step: the ratio and the box before a change (a drag, a ratio, a typed size).</summary>
    private readonly record struct Step(Choice<string> Aspect, CropRect Box);

    /// <param name="tool">The tool (only <see cref="ImageToolKind.Crop"/> so far).</param>
    /// <param name="files">The pictures; the edit goes on all of them.</param>
    /// <param name="store">Where the last ratio is remembered.</param>
    /// <param name="log">Where a ratio that can't be remembered is reported.</param>
    public ImageToolViewModel(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log = null)
    {
        Tool = tool;
        _files = files;
        _store = store;
        _loc = loc;
        _log = log;
        Aspects = CropMath.AspectIds.Select(id => new Choice<string>(id, id switch
        {
            "free" => loc["tool.crop.free"],
            "original" => loc["tool.crop.original"],
            _ => id,
        })).ToList();
        // A ratio from a newer Filee: free, as CropMath.RatioOf reads it.
        _aspect = Aspects.FirstOrDefault(a => a.Value == store.Settings.ImageTools.CropAspect) ?? Aspects[0];
        (ImageWidth, ImageHeight) = OrientedSize(files[0]);
        _box = CropMath.Initial(Ratio(_aspect), ImageWidth, ImageHeight);
        _crop = Fit(_box, _aspect);
        _ = LoadPreviewAsync(0);
    }

    public ImageToolKind Tool { get; }

    /// <summary>The file shown, from 0.</summary>
    public int Index { get; private set; }

    public int Count => _files.Count;

    /// <summary>More than one file: the window shows "1 of 3" and the ‹ › buttons.</summary>
    public bool HasMany => Count > 1;

    /// <summary>"Crop · photo.jpg".</summary>
    public string Title => $"{_loc["tool.crop.title"]} · {Path.GetFileName(_files[Index])}";

    /// <summary>"1 of 3".</summary>
    public string PositionText => _loc.Format("tool.position", Index + 1, Count);

    /// <summary>"Applies to all 3 files"; empty for one file.</summary>
    public string FooterText => HasMany ? _loc.Format("tool.applies_to_all", Count) : "";

    /// <summary>Size of the file shown, the way it stands (EXIF orientation applied); 0 when it can't be read.</summary>
    public int ImageWidth { get; private set; }

    /// <inheritdoc cref="ImageWidth"/>
    public int ImageHeight { get; private set; }

    /// <summary>The ratio chips, in the order of <see cref="CropMath.AspectIds"/>.</summary>
    public IReadOnlyList<Choice<string>> Aspects { get; }

    /// <summary>
    /// The ratio. A fixed one starts over with its largest box in the middle; free keeps the box as it is. One undo step.
    /// </summary>
    public Choice<string> Aspect
    {
        get => _aspect;
        set
        {
            // Null: a list box letting go of its selection (Ctrl+click); the choice stays and shows again.
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            if (value == _aspect)
                return;
            Change(value, value.Value == "free" ? _crop : CropMath.Initial(Ratio(value), ImageWidth, ImageHeight));
        }
    }

    /// <summary>The box on the file shown (fractions; refitted from the box set on any file). Setting it is one undo step.</summary>
    public CropRect Crop
    {
        get => _crop;
        set => Change(_aspect, value);
    }

    /// <summary>The box's width in pixels of the file shown. Typed: the box keeps its middle (a fixed ratio its shape).</summary>
    public int PixelWidth
    {
        get => Pixels.Width;
        set => SetPixels(Math.Max(1, value), null);
    }

    /// <inheritdoc cref="PixelWidth"/>
    public int PixelHeight
    {
        get => Pixels.Height;
        set => SetPixels(null, Math.Max(1, value));
    }

    /// <summary>The file shown, turned upright and made smaller; null while it is read (or when it can't be).</summary>
    [ObservableProperty] private Bitmap? _preview;

    /// <summary>The edit to run, set by Save; null after Cancel (or when the window was closed).</summary>
    public ImageEdit? Result { get; private set; }

    /// <summary>Save or Cancel: the window closes.</summary>
    public event EventHandler? Finished;

    private (int X, int Y, int Width, int Height) Pixels =>
        ImageWidth > 0 && ImageHeight > 0 ? CropMath.ToPixels(_crop, ImageWidth, ImageHeight) : default;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Show(Index + 1);

    private bool CanGoNext => Index < Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Show(Index - 1);

    private bool CanGoPrevious => Index > 0;

    /// <summary>The ratio's largest box in the middle again (the whole picture when free).</summary>
    [RelayCommand]
    private void Reset() => Change(_aspect, CropMath.Initial(Ratio(_aspect), ImageWidth, ImageHeight));

    [RelayCommand]
    private void Undo()
    {
        if (_undo.TryPop(out var step))
        {
            _redo.Push(new Step(_aspect, _box));
            Set(step);
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.TryPop(out var step))
        {
            _undo.Push(new Step(_aspect, _box));
            Set(step);
        }
    }

    /// <summary>Hands the box back for every file and remembers the ratio for next time.</summary>
    [RelayCommand]
    private void Save()
    {
        var crop = _box.Clone();
        // "Original" is each picture's own shape: the same fraction of either side, which a free box keeps on every
        // file. As a number it would give every file the shape of the one on screen.
        crop.Aspect = _aspect.Value == "original" ? null : Ratio(_aspect);
        Result = new ImageEdit { Crop = crop };
        _store.Settings.ImageTools.CropAspect = _aspect.Value;
        try
        {
            _store.SaveSettings();
        }
        catch (Exception ex)
        {
            // A full disk or a locked settings file: the crop still runs, only the ratio is not remembered on disk.
            _log?.LogWarning(ex, "Could not save the crop ratio");
        }
        Finished?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The ratio of a choice on the file shown (pixel width / height); null when free.</summary>
    private double? Ratio(Choice<string> aspect) => CropMath.RatioOf(aspect.Value, ImageWidth, ImageHeight);

    /// <summary><paramref name="box"/> with the ratio of <paramref name="aspect"/>, on the file shown.</summary>
    private CropRect Fit(CropRect box, Choice<string> aspect)
    {
        var fitted = box.Clone();
        fitted.Aspect = Ratio(aspect);
        return CropMath.Refit(fitted, ImageWidth, ImageHeight);
    }

    /// <summary>Typed width or height (pixels): the box keeps its middle; a fixed ratio takes the height as a width.</summary>
    private void SetPixels(double? width, double? height)
    {
        if (ImageWidth <= 0 || ImageHeight <= 0)
            return;
        if (_crop.Aspect is { } ratio && height is { } h)
            (width, height) = (h * ratio, null);
        var box = _crop.Clone();
        if (width is { } w)
        {
            var f = Math.Clamp(w / ImageWidth, 0, 1);
            box.X = Math.Clamp(box.X + box.Width / 2 - f / 2, 0, 1 - f);
            box.Width = f;
        }
        if (height is { } hh)
        {
            var f = Math.Clamp(hh / ImageHeight, 0, 1);
            box.Y = Math.Clamp(box.Y + box.Height / 2 - f / 2, 0, 1 - f);
            box.Height = f;
        }
        Change(_aspect, box);
    }

    /// <summary>A change by the user: one undo step, unless it changes nothing.</summary>
    private void Change(Choice<string> aspect, CropRect box)
    {
        var fitted = Fit(box, aspect);
        if (aspect == _aspect && Same(fitted, _crop))
        {
            OnPropertyChanged(nameof(Crop)); // a canvas that set this box shows the one kept
            return;
        }
        _undo.Push(new Step(_aspect, _box));
        _redo.Clear();
        Set(new Step(aspect, fitted));
    }

    private static bool Same(CropRect a, CropRect b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Width - b.Width) < 1e-9
        && Math.Abs(a.Height - b.Height) < 1e-9 && a.Aspect == b.Aspect;

    private void Set(Step step)
    {
        _aspect = step.Aspect;
        _box = step.Box;
        Refresh();
    }

    /// <summary>Shows the box (and what follows from it) on the file shown.</summary>
    private void Refresh()
    {
        _crop = Fit(_box, _aspect);
        OnPropertyChanged(nameof(Aspect));
        OnPropertyChanged(nameof(Crop));
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
    }

    /// <summary>Goes to another file: the same box, refitted there; its picture is read now.</summary>
    private void Show(int index)
    {
        if (index < 0 || index >= Count || index == Index)
            return;
        Index = index;
        (ImageWidth, ImageHeight) = OrientedSize(_files[index]);
        var old = Preview;
        Preview = null;
        old?.Dispose();
        Refresh();
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        _ = LoadPreviewAsync(index);
    }

    private async Task LoadPreviewAsync(int index)
    {
        Bitmap? bitmap = null;
        try
        {
            var path = _files[index];
            bitmap = await Task.Run(() => ReadPreview(path));
        }
        catch (Exception)
        {
            // Damaged, gone or locked since the donut looked at it: no picture, the box still goes on it.
        }
        // Another file was asked for meanwhile, or the window is gone.
        if (_disposed || index != Index)
        {
            bitmap?.Dispose();
            return;
        }
        var old = Preview;
        Preview = bitmap;
        old?.Dispose();
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
    /// <see cref="PreviewEdge"/> pixels on its long side, as a bitmap (through PNG in memory).
    /// </summary>
    private static Bitmap ReadPreview(string path)
    {
        using var frames = ImageReader.Read(path, FormatRegistry.Detect(path)?.Id ?? "", new Preset(), firstFrameOnly: true);
        var image = frames[0];
        image.AutoOrient();
        if (Math.Max(image.Width, image.Height) > PreviewEdge)
            image.Resize(new MagickGeometry(PreviewEdge, PreviewEdge)); // fits inside, keeping the shape
        image.Quality = 10; // zlib level 1, no filter: the PNG only lives until Avalonia has decoded it
        using var png = new MemoryStream();
        image.Write(png, MagickFormat.Png);
        png.Position = 0;
        return new Bitmap(png);
    }

    public void Dispose()
    {
        _disposed = true;
        var old = Preview;
        Preview = null;
        old?.Dispose();
    }
}
