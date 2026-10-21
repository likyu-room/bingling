// The crop tool in the image tool window: the box and its ratio, the size the panel shows, the 1 to 7 ratio keys, and
// the crop it hands back. One box goes on every file: another file only shows it refitted there (CropMath.Refit), which
// is what the engine does with it when it cuts that file, so every preview is the result.

using Avalonia.Input;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels.Tools;

/// <summary>The crop tool: one box, kept over every file.</summary>
public sealed class CropToolViewModel : ImageToolViewModel
{
    private Choice<string> _aspect;
    private CropRect _box;  // the box as last set, on whichever file: the one that goes on every file
    private CropRect _crop; // _box on the file shown

    /// <summary>One undo step: the ratio and the box before a change (a drag, a ratio, a typed size).</summary>
    private readonly record struct Step(Choice<string> Aspect, CropRect Box);

    /// <param name="files">The pictures; the box goes on all of them.</param>
    /// <param name="store">Where the last ratio is remembered.</param>
    /// <param name="log">Where a ratio that can't be remembered is reported.</param>
    internal CropToolViewModel(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
        : base(ImageToolKind.Crop, files, store, loc, log)
    {
        Aspects = CropMath.AspectIds.Select(id => new Choice<string>(id, id switch
        {
            "free" => loc["tool.crop.free"],
            "original" => loc["tool.crop.original"],
            _ => id,
        })).ToList();
        // A ratio from a newer Filee: free, as CropMath.RatioOf reads it.
        _aspect = Aspects.FirstOrDefault(a => a.Value == store.Settings.ImageTools.CropAspect) ?? Aspects[0];
        _box = CropMath.Initial(Ratio(_aspect), ImageWidth, ImageHeight);
        _crop = Fit(_box, _aspect);
    }

    protected override string TitleKey => "tool.crop.title";

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

    private (int X, int Y, int Width, int Height) Pixels =>
        ImageWidth > 0 && ImageHeight > 0 ? CropMath.ToPixels(_crop, ImageWidth, ImageHeight) : default;

    /// <summary>1 to 7 (top row or keypad, no modifier): the ratio chips in order.</summary>
    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        var chip = modifiers != KeyModifiers.None ? -1 : key switch
        {
            >= Key.D1 and <= Key.D7 => key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad7 => key - Key.NumPad1,
            _ => -1,
        };
        if (chip < 0)
            return false;
        Aspect = Aspects[chip];
        return true;
    }

    /// <summary>The ratio's largest box in the middle again (the whole picture when free).</summary>
    protected override void ResetTool() => Change(_aspect, CropMath.Initial(Ratio(_aspect), ImageWidth, ImageHeight));

    protected override object Snapshot() => new Step(_aspect, _box);

    protected override void Restore(object snapshot) => Set((Step)snapshot);

    /// <summary>The box as set, for every file; the ratio is remembered for next time.</summary>
    protected override ToolResult BuildResult()
    {
        var crop = _box.Clone();
        // "Original" is each picture's own shape: the same fraction of either side, which a free box keeps on every
        // file. As a number it would give every file the shape of the one on screen.
        crop.Aspect = _aspect.Value == "original" ? null : Ratio(_aspect);
        Store.Settings.ImageTools.CropAspect = _aspect.Value;
        return new ToolResult(new ImageEdit { Crop = crop }, null);
    }

    /// <summary>The same box, refitted on the file now shown.</summary>
    protected override void OnFileShown() => Refresh();

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
        RecordStep();
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
}
