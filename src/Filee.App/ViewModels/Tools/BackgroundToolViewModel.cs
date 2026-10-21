// The add background tool in the image tool window: the backdrop (a gradient, a colour or a picture of the user's), the
// padding, corner and shadow sliders with their pixels on the file shown, and the shape of the result. One setting goes on
// every file, so each preview is the result. The user's picture is read on a worker and previewed from a smaller copy.

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.ViewModels.Tools;

/// <summary>A backdrop chip: a gradient or a colour to set the picture on.</summary>
/// <param name="Id">A gradient's id, or <c>solid-</c> and the name of a colour.</param>
/// <param name="Label">Its name, for people who can't see it.</param>
/// <param name="Brush">What the chip is painted with.</param>
/// <param name="Kind">What <paramref name="Value"/> means.</param>
/// <param name="Value">What goes in <see cref="BackgroundEdit.Value"/>.</param>
public sealed record BackgroundSwatch(string Id, string Label, IBrush Brush, BackgroundKind Kind, string Value);

/// <summary>The add background tool: one backdrop, padding, corner, shadow and shape, kept over every file.</summary>
public sealed partial class BackgroundToolViewModel : ImageToolViewModel
{
    /// <summary>The shapes of the result, in the order the chips show them (<see cref="BackgroundEdit.Aspect"/>).</summary>
    internal static readonly IReadOnlyList<string> AspectIds = ["auto", "1:1", "16:9", "9:16", "4:5", "4:3"];

    /// <summary>The colour chips after the gradients, <c>#RRGGBB</c>: white, black, grey, peach, sky, mint.</summary>
    internal static readonly IReadOnlyList<(string Id, string Hex)> Solids =
        [("solid-white", "#FFFFFF"), ("solid-black", "#000000"), ("solid-gray", "#8E8E93"), ("solid-peach", "#FFCBA4"),
            ("solid-sky", "#8EC5FC"), ("solid-mint", "#B5EAD7")];

    private readonly List<string> _copies = []; // preview copies of the user's picture, in the temp folder
    private readonly Dictionary<string, string> _previewOf = new(StringComparer.OrdinalIgnoreCase); // picture → what the preview draws
    private BackgroundEdit _edit; // the backdrop as set: never changed in place, so an undo step can hold it
    private ImageEdit _previewEdit;
    private string _hex = "";
    private bool _dragging;
    private bool _dragRecorded;
    private bool _disposed;

    /// <param name="files">The pictures; the backdrop goes on all of them.</param>
    /// <param name="store">Where the last backdrop is remembered.</param>
    /// <param name="log">Where a backdrop picture that can't be read is reported.</param>
    internal BackgroundToolViewModel(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
        : base(ImageToolKind.AddBackground, files, store, loc, log)
    {
        Swatches =
        [
            .. ImageEditRenderer.Gradients.Select(g => new BackgroundSwatch(g.Id, loc[$"tool.background.swatch.{g.Id}"], Gradient(g.Start, g.End),
                BackgroundKind.Gradient, g.Id)),
            .. Solids.Select(s => new BackgroundSwatch(s.Id, loc[$"tool.background.swatch.{s.Id}"], new SolidColorBrush(Color.Parse(s.Hex)),
                BackgroundKind.Solid, s.Hex)),
        ];
        Aspects = AspectIds.Select(id => new Choice<string>(id, id == "auto" ? loc["tool.background.auto"] : id)).ToList();
        // A copy: the settings hand out the object they keep, which only Save may change.
        _edit = Normalised(store.Settings.ImageTools.Background);
        if (IsCustomColor)
            _hex = _edit.Value;
        _previewEdit = BuildPreview();
    }

    protected override string TitleKey => "tool.background.title";

    /// <summary>The gradient chips, then the colour chips.</summary>
    public IReadOnlyList<BackgroundSwatch> Swatches { get; }

    /// <summary>The shape chips, in the order of <see cref="AspectIds"/>.</summary>
    public IReadOnlyList<Choice<string>> Aspects { get; }

    /// <summary>The backdrop on every file, for the preview: the same edit that is saved, but with a smaller copy of the user's picture.</summary>
    public ImageEdit PreviewEdit => _previewEdit;

    /// <summary>The chip of the backdrop; null for a colour or a picture of the user's. Picking one is one undo step.</summary>
    public BackgroundSwatch? SelectedSwatch
    {
        get => Swatches.FirstOrDefault(s => s.Kind == _edit.Kind && string.Equals(s.Value, _edit.Value, StringComparison.OrdinalIgnoreCase));
        set
        {
            // Null: a list box letting go of its selection (Ctrl+click); the choice stays and shows again.
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            Change(e => (e.Kind, e.Value, e.ImagePath) = (value.Kind, value.Value, null));
        }
    }

    /// <summary>A colour that no chip has is the backdrop.</summary>
    public bool IsCustomColor => _edit.Kind == BackgroundKind.Solid && SelectedSwatch is null;

    /// <summary>A picture of the user's is the backdrop.</summary>
    public bool IsCustomImage => _edit.Kind == BackgroundKind.Image;

    /// <summary>The name of the user's picture, empty when it is not the backdrop.</summary>
    public string CustomImageName => IsCustomImage ? Path.GetFileName(_edit.ImagePath) ?? "" : "";

    /// <summary>
    /// The colour typed in, <c>#RRGGBB</c>. It becomes the backdrop once it is a whole code, not at every key; half a code
    /// leaves the backdrop as it was.
    /// </summary>
    public string CustomColorHex
    {
        get => _hex;
        set
        {
            if (value == _hex)
                return;
            _hex = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomColorBrush));
            if (HexColor(_hex) is { } hex)
                Change(e => (e.Kind, e.Value, e.ImagePath) = (BackgroundKind.Solid, hex, null));
        }
    }

    /// <summary>The typed colour as a chip's paint; nothing while it is not a whole code.</summary>
    public IBrush CustomColorBrush => HexColor(_hex) is { } hex ? new SolidColorBrush(Color.Parse(hex)) : Brushes.Transparent;

    /// <summary>Room around the picture in percent of its short side, 0 to 40. A slider drag is one undo step.</summary>
    public double Padding
    {
        get => Math.Round(_edit.Padding * 100);
        set => Change(e => e.Padding = Math.Clamp(Math.Round(value), 0, 40) / 100);
    }

    /// <summary>Corner radius in percent of the picture's short side, 0 to 25.</summary>
    public double Corner
    {
        get => Math.Round(_edit.Corner * 100);
        set => Change(e => e.Corner = Math.Clamp(Math.Round(value), 0, 25) / 100);
    }

    /// <summary>The shadow's strength, 0 to 100.</summary>
    public double Shadow
    {
        get => _edit.Shadow;
        set => Change(e => e.Shadow = (int)Math.Clamp(Math.Round(value), 0, 100));
    }

    /// <summary>The padding in pixels of the file shown, "64px": what the renderer lays out for it.</summary>
    public string PaddingText => Pixels(Math.Clamp(_edit.Padding, 0, 0.4));

    /// <summary>The corner radius in pixels of the file shown.</summary>
    public string CornerText => Pixels(Math.Clamp(_edit.Corner, 0, 0.25));

    /// <summary>The shadow's strength: a number, not pixels.</summary>
    public string ShadowText => _edit.Shadow.ToString(CultureInfo.InvariantCulture);

    /// <summary>The shape of the result. One undo step.</summary>
    public Choice<string> Aspect
    {
        get => Aspects.First(a => a.Value == _edit.Aspect);
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            Change(e => e.Aspect = value.Value);
        }
    }

    /// <summary>
    /// Call when the pointer goes down on a slider: until <see cref="EndAdjust"/>, what the slider changes is one undo step
    /// (taken at the first change, with the state from before it).
    /// </summary>
    public void BeginAdjust()
    {
        _dragging = true;
        _dragRecorded = false;
    }

    /// <summary>Call when the pointer is let go of a slider.</summary>
    public void EndAdjust() => _dragging = false;

    /// <summary>
    /// Makes the user's picture the backdrop. A large one is read on a worker, and the preview draws a smaller copy: the
    /// renderer reads a backdrop whole, and a camera's photo would stall the preview, as the window shares its thread with
    /// that read. Until then the backdrop stays as it was. A file that is not a picture changes nothing.
    /// </summary>
    public async Task UseImageAsync(string path)
    {
        string? shown;
        try
        {
            shown = await Task.Run(() => PreviewCopy(path));
        }
        catch (Exception ex)
        {
            Log?.LogWarning(ex, "Could not read {Path} as a backdrop", path);
            return;
        }
        if (shown is null)
            return;
        if (_disposed)
        {
            DeleteCopy(shown, path);
            return;
        }
        if (shown != path)
            _copies.Add(shown);
        _previewOf[path] = shown;
        Change(e => (e.Kind, e.ImagePath) = (BackgroundKind.Image, path));
    }

    /// <summary>The padding, corner and shadow of a new window, the first gradient: the defaults of the spec.</summary>
    protected override void ResetTool() => Set(Normalised(new BackgroundEdit()));

    protected override object Snapshot() => _edit;

    protected override void Restore(object snapshot)
    {
        _edit = (BackgroundEdit)snapshot;
        Refresh();
    }

    /// <summary>The backdrop as set, for every file; it is remembered for next time, without the picture's path.</summary>
    protected override ToolResult BuildResult()
    {
        // The settings keep a copy that never has the path (a picture of the user's may be gone the next time).
        Store.Settings.ImageTools.Background = _edit;
        return new ToolResult(new ImageEdit { Background = _edit.Clone() }, null);
    }

    /// <summary>The same percentages in the pixels of the file now shown.</summary>
    protected override void OnFileShown()
    {
        OnPropertyChanged(nameof(PaddingText));
        OnPropertyChanged(nameof(CornerText));
    }

    public override void Dispose()
    {
        _disposed = true;
        foreach (var copy in _copies)
            DeleteCopy(copy, null);
        base.Dispose();
    }

    /// <summary>A change by the user: one undo step, unless it changes nothing (or it goes on a slider drag already begun).</summary>
    private void Change(Action<BackgroundEdit> apply)
    {
        var next = _edit.Clone();
        apply(next);
        Set(next);
    }

    private void Set(BackgroundEdit next)
    {
        if (Same(next, _edit))
        {
            Refresh(); // a control that set a value this tool refused shows the one kept
            return;
        }
        if (!_dragging || !_dragRecorded)
        {
            RecordStep();
            _dragRecorded = _dragging;
        }
        _edit = next;
        Refresh();
    }

    private static bool Same(BackgroundEdit a, BackgroundEdit b) =>
        a.Kind == b.Kind && a.Value == b.Value && a.ImagePath == b.ImagePath && a.Aspect == b.Aspect && a.Shadow == b.Shadow
        && Math.Abs(a.Padding - b.Padding) < 1e-9 && Math.Abs(a.Corner - b.Corner) < 1e-9;

    /// <summary>Shows the backdrop (and what follows from it) in the preview and the panel.</summary>
    private void Refresh()
    {
        // A colour that came from an undo, or from the settings: the box shows it. Not the one being typed, which may
        // be the same colour in other letters.
        if (IsCustomColor && !string.Equals(HexColor(_hex), _edit.Value, StringComparison.OrdinalIgnoreCase))
        {
            _hex = _edit.Value;
            OnPropertyChanged(nameof(CustomColorHex));
            OnPropertyChanged(nameof(CustomColorBrush));
        }
        _previewEdit = BuildPreview();
        foreach (var name in new[]
        {
            nameof(PreviewEdit), nameof(SelectedSwatch), nameof(IsCustomColor), nameof(IsCustomImage), nameof(CustomImageName),
            nameof(Padding), nameof(Corner), nameof(Shadow), nameof(PaddingText), nameof(CornerText), nameof(ShadowText), nameof(Aspect),
        })
            OnPropertyChanged(name);
    }

    /// <summary>The edit the preview draws: <see cref="_edit"/>, but with the copy of the user's picture where it has one.</summary>
    private ImageEdit BuildPreview()
    {
        var background = _edit.Clone();
        if (background.Kind == BackgroundKind.Image)
            background.ImagePath = background.ImagePath is { } path && _previewOf.TryGetValue(path, out var shown) ? shown : null;
        return new ImageEdit { Background = background };
    }

    /// <summary>A percentage of the short side of the file shown, in its pixels: "64px".</summary>
    private string Pixels(double fraction) =>
        // Rounded as the renderer rounds its layout, so the number is what the saved picture gets.
        FullShortSide > 0 ? $"{Math.Round(fraction * FullShortSide, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}px" : "";

    /// <summary>The settings' backdrop as a working copy: values the sliders can show, and a shape the chips have.</summary>
    private static BackgroundEdit Normalised(BackgroundEdit settings)
    {
        var edit = settings.Clone();
        edit.Padding = Math.Round(Math.Clamp(edit.Padding, 0, 0.4) * 100) / 100;
        edit.Corner = Math.Round(Math.Clamp(edit.Corner, 0, 0.25) * 100) / 100;
        edit.Shadow = Math.Clamp(edit.Shadow, 0, 100);
        if (!AspectIds.Contains(edit.Aspect))
            edit.Aspect = "auto"; // a shape from a newer Filee
        return edit;
    }

    /// <summary>The picture of the user's at the preview's size: a copy in the temp folder, or the picture itself when it is not larger. Runs on a worker.</summary>
    /// <returns>The path to draw; null when the file is not a picture.</returns>
    private static string? PreviewCopy(string path)
    {
        var (width, height) = OrientedSize(path);
        if (width <= 0 || height <= 0)
            return null;
        if (Math.Max(width, height) <= PreviewEdge)
            return path;
        using var frames = ImageReader.Read(path, FormatRegistry.Detect(path)?.Id ?? "", new Preset(), firstFrameOnly: true);
        var image = frames[0];
        image.AutoOrient();
        image.Resize(new MagickGeometry(PreviewEdge, PreviewEdge)); // fits inside, keeping the shape
        var copy = Path.Combine(Path.GetTempPath(), $"filee-backdrop-{Guid.NewGuid():N}.png");
        image.Write(copy, MagickFormat.Png); // PNG keeps a see-through picture see-through
        return copy;
    }

    /// <summary>Removes a preview copy; <paramref name="original"/> is the picture it stands for, which is never removed.</summary>
    private static void DeleteCopy(string copy, string? original)
    {
        if (copy == original)
            return;
        try
        {
            File.Delete(copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the temp folder, which the system clears.
        }
    }

    /// <summary><paramref name="text"/> as <c>#RRGGBB</c> in capitals when it is six hex digits (the <c>#</c> optional); otherwise null.</summary>
    internal static string? HexColor(string text) =>
        HexPattern().Match(text.Trim()) is { Success: true } m ? "#" + m.Groups[1].Value.ToUpperInvariant() : null;

    [GeneratedRegex("^#?([0-9A-Fa-f]{6})$")]
    private static partial Regex HexPattern();

    /// <summary>The gradient as the renderer draws it: first colour at the top left corner, last at the bottom right.</summary>
    private static LinearGradientBrush Gradient(SKColor start, SKColor end) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromRgb(start.Red, start.Green, start.Blue), 0),
            new GradientStop(Color.FromRgb(end.Red, end.Green, end.Blue), 1),
        ],
    };
}
