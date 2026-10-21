// The redact tool in the image tool window: the boxes to hide and the way of hiding them (a black box or a mosaic), which
// box is picked, the B / M / Delete keys, and the edit it hands back. One set of boxes goes on every file: the boxes are
// fractions of the picture, so another file shows them in the same place, and every preview is the result. Save is off
// until there is a box.

using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels.Tools;

/// <summary>The redact tool: boxes and one way of hiding all of them, kept over every file.</summary>
public sealed partial class RedactToolViewModel : ImageToolViewModel
{
    private RedactEdit _redact; // as set: never changed in place, so an undo step can hold it
    private int _selected = -1;

    /// <summary>One undo step: the boxes and mode, and which box was picked, before a change.</summary>
    private readonly record struct Step(RedactEdit Redact, int Selected);

    /// <param name="files">The pictures; the boxes go on all of them.</param>
    /// <param name="store">Where the last way of hiding is remembered.</param>
    /// <param name="log">Where a choice that can't be remembered is reported.</param>
    internal RedactToolViewModel(IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
        : base(ImageToolKind.Redact, files, store, loc, log)
    {
        Modes = [new(RedactMode.Black, loc["tool.redact.black"]), new(RedactMode.Mosaic, loc["tool.redact.mosaic"])];
        _redact = new RedactEdit { Mode = store.Settings.ImageTools.RedactMode };
    }

    protected override string TitleKey => "tool.redact.title";

    /// <summary>The two ways of hiding, as the chips show them.</summary>
    public IReadOnlyList<Choice<RedactMode>> Modes { get; }

    /// <summary>How every box is hidden. One undo step.</summary>
    public Choice<RedactMode> Mode
    {
        get => Modes.FirstOrDefault(m => m.Value == _redact.Mode) ?? Modes[0];
        set
        {
            // Null: a list box letting go of its selection (Ctrl+click); the choice stays and shows again.
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            var next = _redact.Clone();
            next.Mode = value.Value;
            Change(next, _selected);
        }
    }

    /// <summary>
    /// The boxes and the mode, as fractions of the picture. Never changed in place: the canvas sets a changed copy, once per
    /// drag, and that is one undo step.
    /// </summary>
    public RedactEdit Redact
    {
        get => _redact;
        set => Change(value.Clone(), _selected);
    }

    /// <summary>The picked box, an index into <see cref="Redact"/>'s boxes; -1 for none. Picking is not an undo step.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var picked = Math.Clamp(value, -1, _redact.Boxes.Count - 1);
            if (picked == _selected)
                return;
            _selected = picked;
            OnPropertyChanged();
            NotifySelection();
        }
    }

    /// <summary>A box is picked: the "delete the picked one" button is on.</summary>
    public bool HasSelection => _selected >= 0;

    /// <summary>"3 boxes": what the panel says about the boxes.</summary>
    public string BoxCountText => Loc.Format("tool.redact.count", _redact.Boxes.Count);

    /// <summary>Nothing to hide yet: there is nothing to save.</summary>
    protected override bool CanSave => _redact.Boxes.Count > 0;

    /// <summary>Delete or Backspace removes the picked box; B picks the black box, M the mosaic.</summary>
    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None)
            return false;
        switch (key)
        {
            case Key.Delete or Key.Back:
                return DeleteSelected();
            case Key.B:
                Mode = Modes.First(m => m.Value == RedactMode.Black);
                return true;
            case Key.M:
                Mode = Modes.First(m => m.Value == RedactMode.Mosaic);
                return true;
            default:
                return false;
        }
    }

    /// <summary>No boxes. The way of hiding stays: it is a choice, not work to lose.</summary>
    protected override void ResetTool() => Change(new RedactEdit { Mode = _redact.Mode }, -1);

    protected override object Snapshot() => new Step(_redact, _selected);

    protected override void Restore(object snapshot)
    {
        var step = (Step)snapshot;
        Set(step.Redact, step.Selected);
    }

    /// <summary>The boxes as set, for every file; the way of hiding is remembered for next time.</summary>
    protected override ToolResult BuildResult()
    {
        Store.Settings.ImageTools.RedactMode = _redact.Mode;
        return new ToolResult(new ImageEdit { Redact = _redact.Clone() }, null);
    }

    /// <summary>Removes the picked box as one undo step; false when none is picked.</summary>
    private bool DeleteSelected()
    {
        if (_selected < 0 || _selected >= _redact.Boxes.Count)
            return false;
        var next = _redact.Clone();
        next.Boxes.RemoveAt(_selected);
        Change(next, -1);
        return true;
    }

    /// <summary>The "delete the picked one" button of the panel: the same as the Delete key.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelection() => DeleteSelected();

    /// <summary>Another box is picked, or none: the button that deletes it follows.</summary>
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A change by the user: one undo step, unless it changes nothing.</summary>
    private void Change(RedactEdit next, int selected)
    {
        if (Same(next, _redact))
        {
            OnPropertyChanged(nameof(Redact)); // a canvas that set this shows the one kept
            return;
        }
        RecordStep();
        Set(next, selected);
    }

    private static bool Same(RedactEdit a, RedactEdit b) =>
        a.Mode == b.Mode && a.Boxes.Count == b.Boxes.Count
        && a.Boxes.Zip(b.Boxes).All(p => Math.Abs(p.First.X - p.Second.X) < 1e-9 && Math.Abs(p.First.Y - p.Second.Y) < 1e-9
            && Math.Abs(p.First.Width - p.Second.Width) < 1e-9 && Math.Abs(p.First.Height - p.Second.Height) < 1e-9);

    private void Set(RedactEdit redact, int selected)
    {
        _redact = redact;
        _selected = Math.Clamp(selected, -1, redact.Boxes.Count - 1);
        OnPropertyChanged(nameof(Redact));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(SelectedIndex));
        NotifySelection();
        OnPropertyChanged(nameof(BoxCountText));
        NotifyCanSaveChanged();
    }
}
