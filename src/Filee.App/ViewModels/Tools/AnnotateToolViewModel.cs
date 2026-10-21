// The annotate tool ("그리기") in the image tool window: what is drawn on each file (every file its own, with its own undo
// steps), the tool, colour and thickness new drawings get, which drawing is picked, the tool keys, and the edits it hands
// back: one for each file drawn on, the others left out of the job. Save is off until something is drawn, and Cancel or
// Esc asks before drawings are thrown away.

using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels.Tools;

/// <summary>A colour chip of the annotate panel.</summary>
/// <param name="Hex">The colour, <c>#RRGGBB</c>.</param>
/// <param name="Label">Its name, for people who can't see it.</param>
/// <param name="Brush">What the chip is painted with.</param>
public sealed record ColorSwatch(string Hex, string Label, IBrush Brush);

/// <summary>The annotate tool: each file's own drawings, and the tool, colour and thickness they are drawn with.</summary>
public sealed partial class AnnotateToolViewModel : ImageToolViewModel
{
    /// <summary>The tool keys in the order of the panel's cells: V the select tool, then the <see cref="AnnotationKind"/>s in order.</summary>
    private static readonly Key[] ToolKeys = [Key.V, Key.P, Key.H, Key.A, Key.L, Key.R, Key.E, Key.T, Key.N];

    /// <summary>The names of <see cref="ImageEditRenderer.AnnotationColors"/>, in their order, as <c>tool.annotate.color.*</c> keys.</summary>
    private static readonly string[] ColorIds = ["red", "orange", "yellow", "green", "blue", "purple", "black", "white"];

    private readonly IReadOnlyList<Annotation>[] _drawings; // by file; never changed in place, so an undo step can hold one
    private int _selected = -1;
    private AnnotationKind? _tool;
    private AnnotationKind _lastKind; // what Save remembers: the select tool is no drawing tool to start with
    private string _color;
    private string _hex = "";
    private StrokeLevel _width;

    /// <summary>One undo step: the drawings of the file shown, and which one was picked, before a change.</summary>
    private readonly record struct Step(IReadOnlyList<Annotation> Drawings, int Selected);

    /// <param name="files">The pictures; each gets its own drawings.</param>
    /// <param name="store">Where the last tool, colour and thickness are remembered.</param>
    /// <param name="log">Where choices that can't be remembered are reported.</param>
    internal AnnotateToolViewModel(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
        : base(ImageToolKind.Annotate, files, store, loc, log)
    {
        // A list of each file's own, never one shared empty array: the canvas tells files apart by their lists.
        _drawings = [.. files.Select(_ => (IReadOnlyList<Annotation>)new List<Annotation>())];
        var settings = store.Settings.ImageTools;
        _tool = _lastKind = settings.LastTool;
        _color = BackgroundToolViewModel.HexColor(settings.PenColor) ?? ImageEditRenderer.AnnotationColors[0];
        _width = settings.PenWidth;
        Colors =
        [
            .. ImageEditRenderer.AnnotationColors.Zip(ColorIds, (hex, id) =>
                new ColorSwatch(hex, loc[$"tool.annotate.color.{id}"], new SolidColorBrush(Avalonia.Media.Color.Parse(hex)))),
        ];
        Widths =
        [
            new(StrokeLevel.Thin, loc["tool.annotate.thin"]), new(StrokeLevel.Normal, loc["tool.annotate.normal"]),
            new(StrokeLevel.Thick, loc["tool.annotate.thick"]),
        ];
        if (IsCustomColor)
            _hex = _color;
    }

    protected override string TitleKey => "tool.annotate.title";

    protected override bool UndoPerFile => true;

    /// <summary>
    /// What is drawn on the file shown, in drawing order, as fractions of the picture. Never changed in place: the canvas
    /// sets a changed copy, once per drawing, drag or text, and that is one undo step of this file.
    /// </summary>
    public IReadOnlyList<Annotation> Annotations
    {
        get => _drawings[Index];
        set => Change(value, _selected);
    }

    /// <summary>The picked drawing, an index into <see cref="Annotations"/>; -1 for none. Picking is not an undo step.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var picked = Math.Clamp(value, -1, Annotations.Count - 1);
            if (picked == _selected)
                return;
            _selected = picked;
            OnPropertyChanged();
            NotifySelection();
        }
    }

    /// <summary>A drawing is picked: the "delete the picked one" button is on.</summary>
    public bool HasSelection => _selected >= 0;

    /// <summary>What a press on the picture draws; null for the select tool. Another tool lets go of the picked drawing.</summary>
    public AnnotationKind? DrawingTool
    {
        get => _tool;
        set
        {
            if (value == _tool)
                return;
            _tool = value;
            if (value is { } kind)
                _lastKind = kind;
            SelectedIndex = -1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToolIndex));
            OnPropertyChanged(nameof(ToolHint));
        }
    }

    /// <summary>
    /// One line on how the tool is used, which its cell's tip (name and key) does not say: Shift for straight lines and
    /// squares, Enter to finish a text, two clicks to retype one.
    /// </summary>
    public string ToolHint => Loc[_tool switch
    {
        null => "tool.annotate.hint.select",
        AnnotationKind.Pen or AnnotationKind.Highlighter => "tool.annotate.hint.draw",
        AnnotationKind.Arrow or AnnotationKind.Line => "tool.annotate.hint.line",
        AnnotationKind.Rectangle or AnnotationKind.Ellipse => "tool.annotate.hint.shape",
        AnnotationKind.Text => "tool.annotate.hint.text",
        _ => "tool.annotate.hint.number",
    }];

    /// <summary>The tool as the panel's cell: 0 the select tool, then the drawing tools in <see cref="AnnotationKind"/> order.</summary>
    public int ToolIndex
    {
        get => _tool is { } kind ? (int)kind + 1 : 0;
        set
        {
            // -1: a list box letting go of its selection (Ctrl+click); the tool stays and shows again.
            if (value < 0 || value >= ToolKeys.Length)
            {
                OnPropertyChanged();
                return;
            }
            DrawingTool = value == 0 ? null : (AnnotationKind)(value - 1);
        }
    }

    /// <summary>The colour chips, <see cref="ImageEditRenderer.AnnotationColors"/>.</summary>
    public IReadOnlyList<ColorSwatch> Colors { get; }

    /// <summary>
    /// The chip of the colour; null for one typed in. It is for the drawings to come, and also for the picked drawing, which
    /// takes it as one undo step: pressing a chip and seeing nothing change would look like a button that doesn't work.
    /// </summary>
    public ColorSwatch? SelectedColor
    {
        get => Colors.FirstOrDefault(c => c.Hex == _color);
        set
        {
            if (value is null)
            {
                OnPropertyChanged(); // a list box letting go of its selection
                return;
            }
            SetColor(value.Hex);
        }
    }

    /// <summary>The colour new drawings get, <c>#RRGGBB</c>.</summary>
    public string Color => _color;

    /// <summary>A colour that no chip has is the one drawn with.</summary>
    public bool IsCustomColor => SelectedColor is null;

    /// <summary>The colour typed in, <c>#RRGGBB</c>; it is drawn with once it is a whole code, not at every key.</summary>
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
            OnPropertyChanged(nameof(HasCustomColor));
            if (BackgroundToolViewModel.HexColor(_hex) is { } hex)
                SetColor(hex);
        }
    }

    /// <summary>The typed colour as a chip's paint; nothing while it is not a whole code.</summary>
    public IBrush CustomColorBrush =>
        BackgroundToolViewModel.HexColor(_hex) is { } hex ? new SolidColorBrush(Avalonia.Media.Color.Parse(hex)) : Brushes.Transparent;

    /// <summary>The typed text is a whole colour code: only then is there a colour to show beside the box (not an empty pill).</summary>
    public bool HasCustomColor => BackgroundToolViewModel.HexColor(_hex) is not null;

    /// <summary>The thickness chips: thin, normal, thick.</summary>
    public IReadOnlyList<Choice<StrokeLevel>> Widths { get; }

    /// <summary>The thickness chip. Like the colour, it is for the next drawings and for the picked one (one undo step).</summary>
    public Choice<StrokeLevel> SelectedWidth
    {
        get => Widths.FirstOrDefault(w => w.Value == _width) ?? Widths[0];
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            StrokeWidth = value.Value;
        }
    }

    /// <summary>How thick new drawings are (and how large a text or a number).</summary>
    public StrokeLevel StrokeWidth
    {
        get => _width;
        set
        {
            if (value == _width)
                return;
            _width = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWidth));
            RestylePicked(a => a.Width = value, a => a.Width != value);
        }
    }

    /// <summary>
    /// "Only the 2 drawn files are saved" once a file is drawn on, when there are more files than one (and nothing before
    /// that: the drawings are one file's each, not for all); for one file, that the original stays.
    /// </summary>
    public override string FooterText => !HasMany ? base.FooterText : Marked > 0 ? Loc.Format("tool.annotate.saved_count", Marked) : "";

    /// <summary>Nothing drawn on any file: there is nothing to save.</summary>
    protected override bool CanSave => Marked > 0;

    /// <summary>How many files have something drawn on them.</summary>
    private int Marked => _drawings.Count(d => d.Count > 0);

    /// <summary>V, P, H, A, L, R, E, T and N pick a tool; [ and ] change the thickness; Delete or Backspace removes the picked drawing.</summary>
    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None)
            return false;
        switch (key)
        {
            case Key.Delete or Key.Back:
                return DeleteSelected();
            case Key.OemOpenBrackets:
                StrokeWidth = (StrokeLevel)Math.Max((int)_width - 1, (int)StrokeLevel.Thin);
                return true;
            case Key.OemCloseBrackets:
                StrokeWidth = (StrokeLevel)Math.Min((int)_width + 1, (int)StrokeLevel.Thick);
                return true;
        }
        var cell = Array.IndexOf(ToolKeys, key);
        if (cell < 0)
            return false;
        ToolIndex = cell;
        return true;
    }

    /// <summary>Drawings were made: asks whether to throw them away.</summary>
    protected override Task<bool> ConfirmCloseAsync() =>
        Marked > 0 && Ask is { } ask ? ask(Loc["tool.annotate.discard_title"]) : Task.FromResult(true);

    /// <summary>Nothing drawn on the file shown, as one of its undo steps; the other files keep theirs.</summary>
    protected override void ResetTool()
    {
        if (Annotations.Count > 0)
            Change([], -1);
    }

    protected override object Snapshot() => new Step(Annotations, _selected);

    protected override void Restore(object snapshot)
    {
        var step = (Step)snapshot;
        Set(step.Drawings, step.Selected);
    }

    /// <summary>Another file, with its own drawings, none of them picked.</summary>
    protected override void OnFileShown()
    {
        _selected = -1;
        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(SelectedIndex));
        NotifySelection();
    }

    /// <summary>Each file drawn on with its drawings; the tool, colour and thickness are remembered for next time.</summary>
    protected override ToolResult BuildResult()
    {
        var settings = Store.Settings.ImageTools;
        (settings.PenColor, settings.PenWidth, settings.LastTool) = (_color, _width, _lastKind);
        var perFile = new Dictionary<string, ImageEdit>();
        for (var i = 0; i < Count; i++)
        {
            if (_drawings[i].Count > 0)
                perFile[Files[i]] = new ImageEdit { Annotations = [.. _drawings[i].Select(a => a.Clone())] };
        }
        return new ToolResult(null, perFile);
    }

    private void SetColor(string hex)
    {
        _color = hex;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(IsCustomColor));
        RestylePicked(a => a.Color = hex, a => !string.Equals(a.Color, hex, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The picked drawing, changed by <paramref name="apply"/> as one undo step, when <paramref name="needed"/> says it
    /// isn't so yet. Nothing when none is picked: a drawing still being drawn is never the picked one, so a colour chosen
    /// in the middle of drawing is for the next drawing only.
    /// </summary>
    private void RestylePicked(Action<Annotation> apply, Func<Annotation, bool> needed)
    {
        if (_selected < 0 || _selected >= Annotations.Count || !needed(Annotations[_selected]))
            return;
        var next = Annotations.Select(a => a.Clone()).ToList();
        apply(next[_selected]);
        Change(next, _selected);
    }

    /// <summary>Removes the picked drawing as one undo step; false when none is picked.</summary>
    private bool DeleteSelected()
    {
        if (_selected < 0 || _selected >= Annotations.Count)
            return false;
        Change([.. Annotations.Where((_, i) => i != _selected)], -1);
        return true;
    }

    /// <summary>The "delete the picked one" button of the panel: the same as the Delete key.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelection() => DeleteSelected();

    /// <summary>Another drawing is picked, or none: the button that deletes it follows.</summary>
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A change by the user on the file shown: one undo step of it.</summary>
    private void Change(IReadOnlyList<Annotation> next, int selected)
    {
        RecordStep();
        Set([.. next.Select(a => a.Clone())], selected);
    }

    private void Set(IReadOnlyList<Annotation> drawings, int selected)
    {
        _drawings[Index] = drawings;
        _selected = Math.Clamp(selected, -1, drawings.Count - 1);
        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(SelectedIndex));
        NotifySelection();
        OnPropertyChanged(nameof(FooterText));
        NotifyCanSaveChanged();
    }
}
