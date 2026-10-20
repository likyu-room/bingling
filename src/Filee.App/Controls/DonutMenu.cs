// The donut toolbar: a ring completely divided into equal slices, one per preset.
// Used both as the live toolbar (RadialWindow) and as the editor in Settings → Donut toolbar (IsEditMode).
//
// Drawn by hand (no child controls) so it stays fast on low-end PCs and so every slice can be hit-tested by
// its exact wedge shape. Animations are tiny springs advanced on each rendered frame; with
// Motion.Enabled == false every value jumps straight to its target.
//
// After a drop the donut turns into a progress ring: see DonutMenu.Progress.cs. Group slices, which fan out a ring
// of presets, and the keyboard: DonutMenu.Groups.cs. The Peach Glass backdrop: DonutMenu.Glass.cs.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>One slice of the donut.</summary>
public sealed class DonutItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>Small second line, e.g. the target format. Hidden when equal to <see cref="Label"/>.</summary>
    public string? Caption { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Tooltip shown for disabled slices.</summary>
    public string? DisabledReason { get; init; }

    /// <summary>An image tool (crop ...): drawn with a tint over the ordinary slice colour, so tools stand out from conversions.</summary>
    public bool IsAccent { get; init; }

    /// <summary>
    /// Small picture above the name, a path in a 24×24 box (see <see cref="DonutIcons"/>), drawn in the name's colour. Left
    /// out when the slice has no room for it, so the name alone is always there.
    /// </summary>
    public Geometry? Icon { get; init; }

    /// <summary>
    /// A group slice: converts nothing itself; hovering it fans these out in a small ring outside the donut, where they
    /// are clicked or dropped on like ordinary slices.
    /// </summary>
    public IReadOnlyList<DonutItem>? Children { get; init; }

    /// <summary>A group slice (<see cref="Children"/> set), even one left with nothing to fan out.</summary>
    public bool IsGroup => Children is not null;

    /// <summary>A group with something to fan out.</summary>
    public bool CanExpand => Children is { Count: > 0 };
}

/// <summary>Donut-shaped menu control.</summary>
public sealed partial class DonutMenu : Control
{
    public static readonly StyledProperty<IReadOnlyList<DonutItem>?> ItemsProperty =
        AvaloniaProperty.Register<DonutMenu, IReadOnlyList<DonutItem>?>(nameof(Items));
    public static readonly StyledProperty<double> OuterRadiusProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(OuterRadius), 150);
    public static readonly StyledProperty<double> HoleRatioProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(HoleRatio), 0.42);
    public static readonly StyledProperty<double> SliceOpacityProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(SliceOpacity), 0.96);
    public static readonly StyledProperty<string?> CenterTitleProperty =
        AvaloniaProperty.Register<DonutMenu, string?>(nameof(CenterTitle));
    public static readonly StyledProperty<string?> CenterSubtitleProperty =
        AvaloniaProperty.Register<DonutMenu, string?>(nameof(CenterSubtitle));
    public static readonly StyledProperty<bool> IsEditModeProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(IsEditMode));
    public static readonly StyledProperty<bool> AllowContextEditProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(AllowContextEdit));
    public static readonly StyledProperty<bool> ReserveSubRingProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(ReserveSubRing));

    public static readonly StyledProperty<IBrush?> SliceBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(SliceBrush), Brushes.White);
    public static readonly StyledProperty<IBrush?> SliceBorderBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(SliceBorderBrush));
    public static readonly StyledProperty<IBrush?> CenterBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(CenterBrush), Brushes.White);
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(AccentBrush), Brushes.MediumPurple);
    /// <summary>Tint of the tool slices (<see cref="DonutItem.IsAccent"/>); null uses <see cref="AccentBrush"/>.</summary>
    public static readonly StyledProperty<IBrush?> ToolBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(ToolBrush));
    public static readonly StyledProperty<IBrush?> OnAccentBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(OnAccentBrush), Brushes.White);
    /// <summary>Fill of the highlighted slice and the colour of its label; null uses <see cref="AccentBrush"/> / <see cref="OnAccentBrush"/>.</summary>
    public static readonly StyledProperty<IBrush?> HotBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(HotBrush));
    public static readonly StyledProperty<IBrush?> OnHotBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(OnHotBrush));
    /// <summary>The Peach Glass look: selects the style in Soft.axaml that colours the donut peach, and shows <see cref="GlassBackdrop"/> through it.</summary>
    public static readonly StyledProperty<bool> IsGlassProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(IsGlass));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(Foreground), Brushes.Black);
    public static readonly StyledProperty<IBrush?> SecondaryForegroundProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(SecondaryForeground), Brushes.Gray);
    public static readonly StyledProperty<IBrush?> DangerBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(DangerBrush), Brushes.IndianRed);

    /// <summary>Gap between slices in pixels (keeps the donut "full" while separating slices).</summary>
    private const double GapPixels = 3;

    /// <summary>
    /// How far a lit slice pops out. It is also the room the window keeps round the donut (<see cref="SideFor"/>) and the
    /// slack of the hit test, so a popped-out slice stays under the pointer.
    /// </summary>
    internal const double PopOut = 9;

    /// <summary>How much a lit slice grows (a fraction of its size).</summary>
    private const double HoverGrow = 0.035;

    /// <summary>How long one slice takes to open; slice i starts <c>i * OpenStagger</c> after the first.</summary>
    private const double OpenDuration = 0.34;
    private const double OpenStagger = 0.022;

    /// <summary>The centre circle opens first: it starts at once, from <see cref="CenterStartScale"/>, and the slices after this long.</summary>
    private const double CenterLead = 0.06;
    private const double CenterStartScale = 0.85;

    /// <summary>The whole donut shrinks to <see cref="CloseScale"/> and fades out over <see cref="CloseDuration"/> when it closes.</summary>
    private const double CloseDuration = 0.18;
    private const double CloseScale = 0.92;
    private const double DragStartDistance = 5;

    // Pencil (Material "edit") path in a 24×24 box.
    private static readonly Geometry PencilIcon = Geometry.Parse(
        "M3,17.25V21h3.75L17.81,9.94l-3.75-3.75L3,17.25z M20.71,7.04c0.39-0.39,0.39-1.02,0-1.41l-2.34-2.34c-0.39-0.39-1.02-0.39-1.41,0l-1.83,1.83l3.75,3.75L20.71,7.04z");

    private Spring[] _hover = [];
    private double[] _open = [];
    private Spring _centerPulse;
    private double _centerOpen = 1;   // 0 to 1 (and a little past it): the centre circle's own opening
    private double _closeAmount;      // 0 open to 1 gone: how far the closing donut has shrunk and faded
    private Matrix _outer = Matrix.Identity; // the closing donut's transform while it is rendered (the glass undoes it)
    private double _openClock = double.MaxValue;
    private TimeSpan? _lastFrame;
    private bool _animating;
    private Action? _closeCallback;
    private bool _closing;

    private int _hoverIndex = -1;
    private int _externalHighlight = -1;
    private bool _externalCenterHot;
    private int _dimmedIndex = -1;
    private int _dropTargetIndex = -1;

    // Edit mode drop preview: the slices make room where a dragged preset would land and a translucent slice shows
    // it there. Every slice's centre angle and width is a spring, so the others slide aside instead of jumping.
    private Spring[] _mid = [];
    private Spring[] _span = [];
    private Spring _previewMid;
    private Spring _previewSpan;
    private int _previewSlot = -1;
    private int _previewRemoved = -1;
    private string? _previewLabel;
    private (int Index, Point Start, PointerPressedEventArgs Args)? _press;

    static DonutMenu()
    {
        AffectsMeasure<DonutMenu>(OuterRadiusProperty, ReserveSubRingProperty);
        AffectsRender<DonutMenu>(HoleRatioProperty, SliceOpacityProperty, CenterTitleProperty, CenterSubtitleProperty,
            IsEditModeProperty, SliceBrushProperty, SliceBorderBrushProperty, CenterBrushProperty, AccentBrushProperty,
            ToolBrushProperty, OnAccentBrushProperty, HotBrushProperty, OnHotBrushProperty, ForegroundProperty, SecondaryForegroundProperty, SuccessBrushProperty,
            IsGlassProperty, GlassBackdropProperty);
        ItemsProperty.Changed.AddClassHandler<DonutMenu>((d, _) => d.OnItemsChanged());
    }

    public DonutMenu()
    {
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<DonutItem>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public double OuterRadius { get => GetValue(OuterRadiusProperty); set => SetValue(OuterRadiusProperty, value); }
    public double HoleRatio { get => GetValue(HoleRatioProperty); set => SetValue(HoleRatioProperty, value); }
    public double SliceOpacity { get => GetValue(SliceOpacityProperty); set => SetValue(SliceOpacityProperty, value); }
    public string? CenterTitle { get => GetValue(CenterTitleProperty); set => SetValue(CenterTitleProperty, value); }
    public string? CenterSubtitle { get => GetValue(CenterSubtitleProperty); set => SetValue(CenterSubtitleProperty, value); }

    /// <summary>Settings editor mode: shows ✎ on hover, raises drag and edit events instead of invoking slices.</summary>
    public bool IsEditMode { get => GetValue(IsEditModeProperty); set => SetValue(IsEditModeProperty, value); }

    /// <summary>Live toolbar in click mode: right-click on a slice raises <see cref="SegmentContextRequested"/>.</summary>
    public bool AllowContextEdit { get => GetValue(AllowContextEditProperty); set => SetValue(AllowContextEditProperty, value); }

    /// <summary>
    /// Leave room for a group's ring even before the slices are known (the drag window is placed before files arrive),
    /// so the window doesn't grow, and the donut move, when they do.
    /// </summary>
    public bool ReserveSubRing { get => GetValue(ReserveSubRingProperty); set => SetValue(ReserveSubRingProperty, value); }

    public IBrush? SliceBrush { get => GetValue(SliceBrushProperty); set => SetValue(SliceBrushProperty, value); }
    public IBrush? SliceBorderBrush { get => GetValue(SliceBorderBrushProperty); set => SetValue(SliceBorderBrushProperty, value); }
    public IBrush? CenterBrush { get => GetValue(CenterBrushProperty); set => SetValue(CenterBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? ToolBrush { get => GetValue(ToolBrushProperty); set => SetValue(ToolBrushProperty, value); }
    public IBrush? OnAccentBrush { get => GetValue(OnAccentBrushProperty); set => SetValue(OnAccentBrushProperty, value); }
    public IBrush? HotBrush { get => GetValue(HotBrushProperty); set => SetValue(HotBrushProperty, value); }
    public IBrush? OnHotBrush { get => GetValue(OnHotBrushProperty); set => SetValue(OnHotBrushProperty, value); }
    public bool IsGlass { get => GetValue(IsGlassProperty); set => SetValue(IsGlassProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? SecondaryForeground { get => GetValue(SecondaryForegroundProperty); set => SetValue(SecondaryForegroundProperty, value); }
    public IBrush? DangerBrush { get => GetValue(DangerBrushProperty); set => SetValue(DangerBrushProperty, value); }

    // The highlight: its own colours when the style gives them (Peach Glass: pale peach and dark text, where white on
    // the orange accent would be hard to read), the accent otherwise. The accent stays for the progress ring and the like.
    private IBrush? HotFill => HotBrush ?? AccentBrush;
    private IBrush? HotText => OnHotBrush ?? OnAccentBrush;

    /// <summary>True while an external drag hovers the hole (drop = cancel).</summary>
    public bool IsCenterHot => _externalCenterHot;

    /// <summary>Currently emphasised slice (pointer hover or external highlight), or -1.</summary>
    public int ActiveIndex => _externalHighlight >= 0 ? _externalHighlight : _hoverIndex;

    /// <summary>A slice was clicked (live toolbar, click mode).</summary>
    public event EventHandler<int>? SegmentInvoked;

    /// <summary>A slice of a group's ring was clicked: (group, slice in the group).</summary>
    public event EventHandler<(int Group, int Sub)>? SubSegmentInvoked;

    /// <summary>The hovered slice of a group's ring, or -1. <see cref="ActiveIndexChanged"/> is raised when it changes.</summary>
    public int ActiveSubIndex => _subHover;

    /// <summary>Right-click or ✎ on a slice.</summary>
    public event EventHandler<int>? SegmentContextRequested;

    /// <summary>Right-click on a slice of a group's ring (live toolbar, click mode): (group, slice in the group).</summary>
    public event EventHandler<(int Group, int Sub)>? SubSegmentContextRequested;

    /// <summary>The hole was clicked.</summary>
    public event EventHandler? CenterInvoked;

    /// <summary>Edit mode: the user started dragging slice <c>Index</c>.</summary>
    public event EventHandler<(int Index, PointerEventArgs Args)>? SegmentDragStarted;

    /// <summary>The emphasised slice changed (-1 = none). Useful to update the centre text.</summary>
    public event EventHandler<int>? ActiveIndexChanged;

    private int Count => Items?.Count ?? 0;
    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double InnerRadius => OuterRadius * Math.Clamp(HoleRatio, 0.2, 0.75);

    // ───────────────────────── Public API ─────────────────────────

    /// <summary>Plays the "pop out from the centre" animation.</summary>
    public void PlayOpenAnimation()
    {
        ResetProgress();
        _closing = false;
        _closeCallback = null;
        _closeAmount = 0;
        if (!Motion.Enabled)
        {
            Array.Fill(_open, 1);
            _centerOpen = 1;
            InvalidateVisual();
            return;
        }
        Array.Fill(_open, 0);
        _centerOpen = 0;
        _openClock = 0;
        StartAnimation();
    }

    /// <summary>Shrinks the donut a little and fades it out, then calls <paramref name="done"/>.</summary>
    public void PlayCloseAnimation(Action done)
    {
        if (!Motion.Enabled || Count == 0)
        {
            done();
            return;
        }
        _closing = true;
        _closeCallback = done;
        _closeAmount = 0;
        _openClock = 0;
        StartAnimation();
    }

    /// <summary>Short "boing" of the centre, e.g. after a drop.</summary>
    public void PulseCenter()
    {
        if (!Motion.Enabled)
            return;
        _centerPulse.Velocity = 6;
        StartAnimation();
    }

    /// <summary>Highlights a slice from outside (drag-over in the live toolbar). -1 clears.</summary>
    public void SetExternalHighlight(int index, bool centerHot = false)
    {
        if (_externalHighlight == index && _externalCenterHot == centerHot)
            return;
        _externalHighlight = index;
        _externalCenterHot = centerHot;
        ActiveIndexChanged?.Invoke(this, ActiveIndex);
        StartAnimation();
    }

    /// <summary>How far slice <paramref name="index"/> is drawn lit up right now (0 to 1), for the drag log.</summary>
    public double LitAmount(int index) => index >= 0 && index < _hover.Length ? _hover[index].Value : 0;

    /// <summary>How far slice <paramref name="index"/> has opened (0 to 1, a little past it while it bounces), for tests.</summary>
    internal double OpenAmount(int index) => _open[index];

    /// <summary>How far the centre circle has opened (0 to 1, a little past it while it bounces), for tests.</summary>
    internal double CenterOpen => _centerOpen;

    /// <summary>How far the closing donut has shrunk and faded: 0 open to 1 gone, for tests.</summary>
    internal double CloseAmount => _closeAmount;

    /// <summary>Edit mode: the slice being dragged is drawn faded.</summary>
    public void SetDimmedIndex(int index)
    {
        _dimmedIndex = index;
        InvalidateVisual();
    }

    /// <summary>Edit mode: highlights where a dragged item would land (-1 none, Count = empty ring).</summary>
    public void SetDropTarget(int index)
    {
        if (_dropTargetIndex == index)
            return;
        _dropTargetIndex = index;
        InvalidateVisual();
    }

    /// <summary>
    /// Edit mode: opens a gap at <paramref name="slot"/> (the position in the list after the drop) and shows a
    /// translucent slice labelled <paramref name="label"/> there. <paramref name="removed"/> is the slice being moved,
    /// which leaves its place. -1 closes the gap.
    /// </summary>
    public void SetDropPreview(int slot, string? label = null, int removed = -1)
    {
        if (_previewSlot == slot && _previewRemoved == removed && _previewLabel == label)
            return;
        if (_previewSlot < 0 && slot >= 0)
        {
            // The new slice grows out of nothing where it appears.
            _previewMid = new Spring { Value = DonutGeometry.MidAngle(slot, SlotCount(removed)) };
            _previewSpan = default;
        }
        _previewSlot = slot;
        _previewRemoved = removed;
        _previewLabel = label;
        StartAnimation();
    }

    /// <summary>The slot under <paramref name="p"/> when the donut has <paramref name="slots"/> slices; -1 off the ring.</summary>
    public int SlotAt(Point p, int slots)
    {
        var hit = DonutGeometry.HitTest(Center, p, InnerRadius, OuterRadius, Math.Max(slots, 1), PopOut);
        return hit.Kind == DonutHitKind.Segment ? hit.Index : -1;
    }

    /// <summary>Slices while previewing: the items, minus the one being moved, plus the preview.</summary>
    private int SlotCount(int removed) => Count + 1 - (removed >= 0 ? 1 : 0);

    /// <summary>Where slice <paramref name="i"/> belongs: its usual place, or shifted to make room for the preview.</summary>
    private (double Mid, double Span) LayoutTarget(int i)
    {
        var n = Count;
        if (_previewSlot < 0)
            return (DonutGeometry.MidAngle(i, n), DonutGeometry.Span(n));
        if (i == _previewRemoved)
            return (i < _mid.Length ? _mid[i].Value : DonutGeometry.MidAngle(i, n), 0); // it becomes the preview
        var slot = _previewRemoved >= 0 && i > _previewRemoved ? i - 1 : i;
        if (slot >= _previewSlot)
            slot++;
        var m = SlotCount(_previewRemoved);
        return (DonutGeometry.MidAngle(slot, m), DonutGeometry.Span(m));
    }

    /// <summary>Moves the slices and the preview towards their places. Returns true while they are still moving.</summary>
    private bool StepLayout(double dt, bool snap = false)
    {
        snap |= !Motion.Enabled;
        var active = false;
        for (var i = 0; i < _mid.Length; i++)
        {
            var (mid, span) = LayoutTarget(i);
            if (snap)
            {
                _mid[i] = new Spring { Value = mid };
                _span[i] = new Spring { Value = span };
                continue;
            }
            active |= _mid[i].Step(mid, dt, 420, 36);
            active |= _span[i].Step(span, dt, 420, 36);
        }
        var m = SlotCount(_previewRemoved);
        var previewMid = _previewSlot >= 0 ? DonutGeometry.MidAngle(_previewSlot, m) : _previewMid.Value;
        var previewSpan = _previewSlot >= 0 ? DonutGeometry.Span(m) : 0;
        if (snap)
        {
            _previewMid = new Spring { Value = previewMid };
            _previewSpan = new Spring { Value = previewSpan };
            return false;
        }
        active |= _previewMid.Step(previewMid, dt, 420, 36);
        active |= _previewSpan.Step(previewSpan, dt, 420, 36);
        return active;
    }

    /// <summary>Hit-tests a point in this control's coordinates.</summary>
    public DonutHit HitTest(Point p)
    {
        if (_progressMode)
            return HitTestProgress(p);
        if (IsEditMode && _hoverIndex >= 0 && _hoverIndex < Count &&
            Distance(p, EditButtonCenter(_hoverIndex)) <= 13)
            return new DonutHit(DonutHitKind.EditButton, _hoverIndex);
        var hit = DonutGeometry.HitTest(Center, p, InnerRadius, OuterRadius, Count, PopOut);
        if (ExpandedChildren is { } children)
        {
            var (inner, outer) = SubRadii;
            var parentMid = DonutGeometry.MidAngle(_expanded, Count);
            var subSpan = SubSpanOf(children.Count);
            var sub = DonutGeometry.SubIndexAt(Center, p, inner, outer, parentMid, children.Count, subSpan);
            if (sub >= 0)
                return new DonutHit(DonutHitKind.SubSegment, _expanded, sub);
            // On the way out to the ring the pointer crosses the gap before it: there it still belongs to the group, or
            // the ring would fold away (or the hint and the drag cursor flicker) just before it is reached. A neighbour's
            // outer half stays that neighbour, so a click or a drop there converts with it; TrackGroups keeps the ring
            // open over it (OnWayToRing).
            if (hit.Kind == DonutHitKind.Outside && Distance(p, Center) < inner && InFan(p, _expanded))
                return new DonutHit(DonutHitKind.Segment, _expanded);
        }
        return hit;
    }

    // ───────────────────────── Layout ─────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = SideFor(OuterRadius, ReserveSubRing || (!IsEditMode && Items?.Any(i => i.IsGroup) == true));
        return new Size(side, side);
    }

    private void OnItemsChanged()
    {
        var n = Count;
        _hover = new Spring[n];
        _open = Enumerable.Repeat(1.0, n).ToArray();
        _hoverIndex = -1;
        _externalHighlight = -1;
        _dimmedIndex = -1;
        _dropTargetIndex = -1;
        // A drop changes the items into exactly what the preview showed: start there, without a jump.
        _previewSlot = -1;
        _previewRemoved = -1;
        _previewSpan = default;
        _mid = Enumerable.Range(0, n).Select(i => new Spring { Value = DonutGeometry.MidAngle(i, n) }).ToArray();
        _span = Enumerable.Range(0, n).Select(_ => new Spring { Value = DonutGeometry.Span(n) }).ToArray();
        _expanded = _shownExpanded = _subHover = -1;
        ClearPending();
        _subOpen = [];
        _centerOpen = 1;
        _closeAmount = 0;
        // The window is reused: a keyboard hold from the last time it was open must not swallow the first moves now.
        _keyboardHold = _ringFromKeys = false;
        _keyAnchor = _lastPointer = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ───────────────────────── Input ─────────────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_progressMode)
        {
            OnProgressPointerMoved(p);
            return;
        }

        if (_press is { } press && IsEditMode && Distance(p, press.Start) > DragStartDistance && press.Index >= 0)
        {
            _press = null;
            SegmentDragStarted?.Invoke(this, (press.Index, e));
            return;
        }

        if (_keyboardHold)
        {
            if (_keyAnchor is not { } anchor)
                _keyAnchor = anchor = p;
            if (Distance(p, anchor) < KeyboardJitter)
                return;
            _keyboardHold = false;
        }
        _lastPointer = p;

        var hit = HitTest(p);
        TrackGroups(hit, p);
        SetHover(hit.Kind switch
        {
            DonutHitKind.Segment or DonutHitKind.EditButton or DonutHitKind.SubSegment => hit.Index,
            _ => -1,
        });
        // On a group's ring, why that slice (not the group) is disabled.
        var hovered = hit.Kind == DonutHitKind.SubSegment ? Items![hit.Index].Children![hit.Sub]
            : _hoverIndex >= 0 ? Items![_hoverIndex] : null;
        ToolTip.SetTip(this, hovered is { IsEnabled: false } ? hovered.DisabledReason : null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_progressMode)
        {
            OnProgressPointerMoved(new Point(double.MaxValue / 4, 0));
            return;
        }
        if (_press is null && !LeftThroughSeam(e.GetPosition(this)))
        {
            CollapseGroup();
            SetHover(-1);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var hit = HitTest(point.Position);

        if (_progressMode)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                _press = (-1, point.Position, e);
                e.Handled = true;
            }
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (hit.Kind is DonutHitKind.Segment or DonutHitKind.EditButton && (IsEditMode || AllowContextEdit))
            {
                SegmentContextRequested?.Invoke(this, hit.Index);
                e.Handled = true;
            }
            else if (hit.Kind == DonutHitKind.SubSegment && AllowContextEdit)
            {
                SubSegmentContextRequested?.Invoke(this, (hit.Index, hit.Sub));
                e.Handled = true;
            }
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _press = (hit.Kind == DonutHitKind.Segment ? hit.Index : -1, point.Position, e);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is null)
            return;
        _press = null;

        if (_progressMode)
        {
            OnProgressPointerReleased(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        var hit = HitTest(e.GetPosition(this));
        switch (hit.Kind)
        {
            case DonutHitKind.EditButton:
                SegmentContextRequested?.Invoke(this, hit.Index);
                break;
            case DonutHitKind.Segment when !IsEditMode && Items![hit.Index] is { IsEnabled: true, IsGroup: false }:
                SegmentInvoked?.Invoke(this, hit.Index);
                break;
            case DonutHitKind.Segment when !IsEditMode && Items![hit.Index].Children is { Count: > 0 }:
                Expand(hit.Index); // no waiting when clicked; a tap has no hover to rest on at all
                break;
            case DonutHitKind.Outside when !IsEditMode && _expanded >= 0:
                CollapseGroup(); // clicked past the ring
                break;
            case DonutHitKind.SubSegment when !IsEditMode && Items![hit.Index].Children![hit.Sub].IsEnabled:
                SubSegmentInvoked?.Invoke(this, (hit.Index, hit.Sub));
                break;
            case DonutHitKind.Center when !IsEditMode:
                CenterInvoked?.Invoke(this, EventArgs.Empty);
                break;
        }
        e.Handled = true;
    }

    private void SetHover(int index)
    {
        if (_hoverIndex == index)
            return;
        _hoverIndex = index;
        ActiveIndexChanged?.Invoke(this, ActiveIndex);
        StartAnimation();
    }

    // ───────────────────────── Animation ─────────────────────────

    /// <summary>Damping at which a spring of stiffness 320 settles without overshooting (2·√320).</summary>
    private const double CriticalDamping = 36;

    /// <summary>Underdamped spring: gives the soft "boing" when a slice pops out.</summary>
    private struct Spring
    {
        public double Value;
        public double Velocity;

        /// <summary>
        /// Longest time one integration step covers. With these stiffnesses a single step of a late frame (50 ms)
        /// overshoots more each frame instead of settling: slices stayed lit and flew apart on a busy machine.
        /// </summary>
        private const double MaxStep = 1 / 120.0;

        public bool Step(double target, double dt, double stiffness = 320, double damping = 20)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                var force = stiffness * (target - Value) - damping * Velocity;
                Velocity += force * h;
                Value += Velocity * h;
            }
            var settled = Math.Abs(target - Value) < 0.001 && Math.Abs(Velocity) < 0.01;
            if (settled)
            {
                Value = target;
                Velocity = 0;
            }
            return !settled;
        }
    }

    /// <summary>
    /// A frame loop that got no frame for this long is treated as stopped: a frame request can be lost (the window
    /// hidden in between, for one), and every later animation would wait for it, so the slice under a dragged file
    /// would never light up.
    /// </summary>
    internal const long StalledLoopMs = 250;

    private int _loop;           // the current frame loop; frames of an older one are ignored
    private long _lastTick;      // Environment.TickCount64 of the last frame or frame request

    /// <summary>Frame requests asked again before the animation is finished at once (see <see cref="CheckLoop"/>).</summary>
    internal const int FrameRetries = 2;

    private int _stalls; // times the current animation's frames stopped coming

    private void StartAnimation()
    {
        InvalidateVisual();
        if (_animating && Environment.TickCount64 - _lastTick < StalledLoopMs)
            return;
        _stalls = 0;
        StartLoop();
    }

    private void StartLoop()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            _animating = false;
            return;
        }
        _animating = true;
        _lastFrame = null;
        _lastTick = Environment.TickCount64;
        var loop = ++_loop;
        top.RequestAnimationFrame(time => OnFrame(time, loop));
        Watch(loop);
    }

    private void Watch(int loop) =>
        DispatcherTimer.RunOnce(() =>
        {
            if (loop == _loop)
                CheckLoop();
        }, TimeSpan.FromMilliseconds(StalledLoopMs + 50));

    /// <summary>
    /// Watches the frame loop without waiting for another animation call. When its frames stop (a request lost
    /// while the window was hidden, or the renderer waking up after a long idle), the frame is asked for again; after
    /// <see cref="FrameRetries"/> stalls the animations jump to their end, so an opening donut always appears. Stalls
    /// are counted per animation: frames that trickle in once per retry would otherwise open the donut over seconds.
    /// </summary>
    internal void CheckLoop()
    {
        if (!_animating)
            return;
        if (Environment.TickCount64 - _lastTick < StalledLoopMs)
        {
            Watch(_loop); // frames are coming: keep watching until the loop ends
            return;
        }
        if (++_stalls <= FrameRetries)
        {
            StartLoop();
            return;
        }
        _animating = false;
        _loop++;
        FinishAnimations();
    }

    /// <summary>
    /// Sets the opening, the highlights and the slice layout to where they end (a closing donut closes). The progress
    /// ring keeps its own timers.
    /// </summary>
    internal void FinishAnimations()
    {
        for (var i = 0; i < _hover.Length; i++)
            _hover[i] = new Spring { Value = i == ActiveIndex ? 1 : 0 };
        _centerPulse = default;
        SnapSubRing();
        StepLayout(0, snap: true);
        if (_openClock < double.MaxValue)
        {
            _openClock = double.MaxValue;
            Array.Fill(_open, 1);
            _centerOpen = 1;
            if (_closing)
            {
                _closeAmount = 1;
                FinishClose();
            }
        }
        InvalidateVisual();
    }

    private void OnFrame(TimeSpan time, int loop)
    {
        if (loop != _loop)
            return; // a loop that was given up on as stalled, replaced by a newer one
        _lastTick = Environment.TickCount64;
        var dt = _lastFrame is { } last ? Math.Clamp((time - last).TotalSeconds, 0, 0.05) : 1 / 60.0;
        _lastFrame = time;
        var active = Advance(dt);

        InvalidateVisual();
        if (active && TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(next => OnFrame(next, loop));
        else
            _animating = false;
    }

    /// <summary>
    /// Advances every animation by <paramref name="dt"/> seconds. Returns true while more frames are needed.
    /// Internal so tests can step time deterministically.
    /// </summary>
    internal bool Advance(double dt)
    {
        var active = false;

        if (!Motion.Enabled)
        {
            for (var i = 0; i < _hover.Length; i++)
            {
                _hover[i].Value = i == ActiveIndex ? 1 : 0;
                _hover[i].Velocity = 0;
            }
            Array.Fill(_open, 1);
            _centerOpen = 1;
            _centerPulse = default;
            SnapSubRing();
            StepLayout(dt);
            if (_closing)
            {
                _closeAmount = 1;
                FinishClose();
            }
            active |= StepProgress(dt); // hold timers still run without animation
        }
        else
        {
            // Popping out bounces; letting go settles without a bounce: one below zero would make the faded accent
            // flash once more on its way back.
            for (var i = 0; i < _hover.Length; i++)
                active |= i == ActiveIndex ? _hover[i].Step(1, dt) : _hover[i].Step(0, dt, 320, CriticalDamping);
            active |= _centerPulse.Step(0, dt, 260, 12);
            active |= StepSubRing(dt);
            active |= StepProgress(dt);
            active |= StepLayout(dt);

            if (_openClock < double.MaxValue)
            {
                _openClock += dt;
                bool done;
                if (_closing)
                {
                    var t = Math.Clamp(_openClock / CloseDuration, 0, 1);
                    _closeAmount = EaseInCubic(t);
                    done = t >= 1;
                }
                else
                {
                    // The centre circle goes first; each slice follows a little after the one before it.
                    var centre = Math.Clamp(_openClock / OpenDuration, 0, 1);
                    _centerOpen = EaseOutBack(centre);
                    done = centre >= 1;
                    for (var i = 0; i < _open.Length; i++)
                    {
                        var t = Math.Clamp((_openClock - CenterLead - i * OpenStagger) / OpenDuration, 0, 1);
                        _open[i] = EaseOutBack(t);
                        done &= t >= 1;
                    }
                }
                if (done)
                {
                    _openClock = double.MaxValue;
                    if (_closing)
                        FinishClose();
                }
                else
                {
                    active = true;
                }
            }
        }

        return active;
    }

    private void FinishClose()
    {
        _closing = false;
        var callback = _closeCallback;
        _closeCallback = null;
        callback?.Invoke();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.25;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static double EaseInCubic(double t) => t * t * t;

    // ───────────────────────── Rendering ─────────────────────────

    public override void Render(DrawingContext context)
    {
        var center = Center;
        var r2 = OuterRadius;
        var r1 = InnerRadius;
        var typeface = new Typeface(GetValue(TextElement.FontFamilyProperty), FontStyle.Normal, FontWeight.SemiBold);
        var typefaceRegular = new Typeface(GetValue(TextElement.FontFamilyProperty));

        if (_progressMode)
        {
            RenderProgress(context, center, r1, r2, typeface, typefaceRegular);
            return;
        }

        // Closing: all of it together shrinks and fades. The glass draws through the inverse of this (DrawGlass), so the
        // screen behind stays where it is. Not closing, nothing is added around the drawing.
        if (_closeAmount <= 0)
        {
            _outer = Matrix.Identity;
            RenderDonut(context, center, r1, r2, typeface, typefaceRegular);
            return;
        }
        var shrink = 1 - (1 - CloseScale) * _closeAmount;
        _outer = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(shrink, shrink) *
                 Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(_outer))
        using (context.PushOpacity(1 - _closeAmount))
            RenderDonut(context, center, r1, r2, typeface, typefaceRegular);
    }

    private void RenderDonut(DrawingContext context, Point center, double r1, double r2, Typeface typeface, Typeface typefaceRegular)
    {
        var n = Count;
        DrawSoftShadow(context, center, r2);

        if (n == 0)
        {
            var ring = CreateRing(center, r1, r2);
            DrawGlass(context, ring, Matrix.Identity);
            context.DrawGeometry(WithOpacity(SliceBrush, SliceOpacity), BorderPen(), ring);
            if (_dropTargetIndex >= 0)
                context.DrawGeometry(WithOpacity(AccentBrush, 0.35), null, ring);
            DrawGlassEdge(context, ring);
        }

        for (var i = 0; i < n; i++)
        {
            var item = Items![i];
            var open = i < _open.Length ? _open[i] : 1;
            if (open <= 0.001)
                continue;

            var previewing = IsEditMode && i < _mid.Length;
            var hover = i < _hover.Length && _previewSlot < 0 ? _hover[i].Value : 0;
            var mid = previewing ? _mid[i].Value : DonutGeometry.MidAngle(i, n);
            var span = previewing ? _span[i].Value : DonutGeometry.Span(n);
            if (span < 0.01)
                continue;
            var offset = DonutGeometry.PointAt(new Point(0, 0), hover * PopOut, mid);

            // Open animation: scale up from the centre with a small twist.
            var scale = 0.55 + 0.45 * open;
            var rotate = (1 - Math.Min(open, 1)) * -0.35;
            var transform =
                Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateRotation(rotate) *
                Matrix.CreateScale(scale * (1 + hover * HoverGrow), scale * (1 + hover * HoverGrow)) *
                Matrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y);

            using (context.PushTransform(transform))
            using (context.PushOpacity(Math.Clamp(open, 0, 1) * (i == _dimmedIndex ? 0.3 : 1)))
            {
                var wedge = CreateWedge(center, r1, r2, mid, span);
                DrawGlass(context, wedge, transform);
                context.DrawGeometry(WithOpacity(SliceBrush, SliceOpacity * (item.IsEnabled ? 1 : 0.55)), BorderPen(), wedge);
                DrawToolTint(context, item, wedge);

                // The accent fades in and out with the highlight (no floor: a floor made it vanish with a jump).
                var emphasis = i == _dropTargetIndex ? 1.0 : hover;
                if (emphasis > 0.01 && item.IsEnabled)
                    context.DrawGeometry(WithOpacity(HotFill, 0.82 * Math.Clamp(emphasis, 0, 1)), null, wedge);
                DrawGlassEdge(context, wedge);

                var hot = (i == ActiveIndex || i == _dropTargetIndex) && item.IsEnabled;
                DrawLabel(context, item, center, (r1 + r2) / 2, mid, span * (r1 + r2) / 2 - 10,
                    r2 - r1 - 16, r2 - r1, typeface, typefaceRegular, hot, Math.Max(0, emphasis));
            }
        }

        if (IsEditMode && _previewSpan.Value > 0.01)
            DrawPreview(context, center, r1, r2, typeface, typefaceRegular);

        var reaching = _expanded >= 0 ? _expanded : _pendingGroup;
        if (!IsEditMode && reaching >= 0 && reaching < n && Items![reaching].Children is { Count: > 0 } reachable)
            DrawHitBackdrop(context, center, reaching, reachable.Count);
        if (!IsEditMode && _shownExpanded >= 0 && _shownExpanded < n && SubRingVisible)
            DrawSubRing(context, center, typeface, typefaceRegular);

        DrawCenter(context, center, r1, typeface, typefaceRegular);

        if (IsEditMode && _hoverIndex >= 0 && _hoverIndex < n && _dimmedIndex < 0)
            DrawEditButton(context, EditButtonCenter(_hoverIndex));
    }

    private void DrawSoftShadow(DrawingContext context, Point center, double r2)
    {
        // A radial gradient ring fakes a soft drop shadow without blur effects (cheap on any GPU).
        var shadowRadius = r2 + 14;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), (r2 - 18) / shadowRadius),
                new GradientStop(Color.FromArgb(46, 0, 0, 0), (r2 - 2) / shadowRadius),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            },
        };
        var opening = _open.Length == 0 ? 1 : Math.Clamp(_open.Average(), 0, 1);
        using (context.PushOpacity(opening))
            context.DrawEllipse(brush, null, new Point(center.X, center.Y + 4), shadowRadius, shadowRadius);
    }

    private void DrawCenter(DrawingContext context, Point center, double r1, Typeface bold, Typeface regular)
    {
        var pulse = 1 + _centerPulse.Value * 0.06;
        var radius = (r1 - 6) * pulse;
        var cancelHot = _externalCenterHot;
        var disc = new EllipseGeometry(new Rect(center.X - radius, center.Y - radius, 2 * radius, 2 * radius));

        // While the donut opens the centre grows from CenterStartScale, ahead of the slices.
        var grow = CenterStartScale + (1 - CenterStartScale) * _centerOpen;
        var transform = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(grow, grow) *
                        Matrix.CreateTranslation(center.X, center.Y);
        using var _ = context.PushTransform(transform);
        DrawGlass(context, disc, transform);
        context.DrawEllipse(CenterBrush, BorderPen(), center, radius, radius);
        if (cancelHot)
            context.DrawEllipse(WithOpacity(DangerBrush, 0.18), null, center, radius, radius);
        DrawGlassEdge(context, disc);

        var maxWidth = radius * 1.6;
        var title = MakeText(CenterTitle ?? "", bold, 14, Foreground, maxWidth, maxLines: 2);
        var subtitle = MakeText(CenterSubtitle ?? "", regular, 11.5, cancelHot ? DangerBrush : SecondaryForeground, maxWidth, maxLines: 2);
        var total = title.Height + (string.IsNullOrEmpty(CenterSubtitle) ? 0 : subtitle.Height + 2);
        var y = center.Y - total / 2;
        DrawCentered(context, title, center.X, y);
        if (!string.IsNullOrEmpty(CenterSubtitle))
            DrawCentered(context, subtitle, center.X, y + title.Height + 2);
    }

    // maxWidth / maxHeight: room for the name across the slice and along the ring (the ring's thickness less a margin).
    // thickness: the ring's thickness itself, which the icon above the name has to fit in too (LabelLayout).
    // lit: how far the slice is lit up (0 to 1, a slice's hover spring), which the icon grows with; by default all or nothing, as hot.
    private void DrawLabel(DrawingContext context, DonutItem item, Point center, double radius, double angle,
        double maxWidth, double maxHeight, double thickness, Typeface bold, Typeface regular, bool hot, double? lit = null)
    {
        var foreground = hot ? HotText : item.IsEnabled ? Foreground : SecondaryForeground;
        var width = Math.Clamp(maxWidth, 30, 120);
        var label = MakeText(item.Label, bold, 13, foreground, width, maxLines: 2);
        var showCaption = !string.IsNullOrEmpty(item.Caption) &&
                          !string.Equals(item.Caption, item.Label, StringComparison.OrdinalIgnoreCase) &&
                          label.Height + 14 < maxHeight;
        var caption = showCaption ? MakeText(item.Caption!, regular, 10.5, hot ? HotText : SecondaryForeground, width) : null;

        var anchor = DonutGeometry.PointAt(center, radius, angle);
        var layout = LabelLayout(item.Icon is not null, center, radius, angle, maxWidth, thickness,
            width, label.Height + (caption?.Height ?? 0), IconScale(lit ?? (hot ? 1 : 0)));
        DrawCentered(context, label, anchor.X, layout.Text.Top);
        if (caption is not null)
            DrawCentered(context, caption, anchor.X, layout.Text.Top + label.Height);

        if (layout.IconShown)
        {
            // Same brush as the name, so the Peach Glass text colours and the greyed-out look carry over.
            var scale = layout.Icon.Width / 24;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(layout.Icon.X, layout.Icon.Y)))
                context.DrawGeometry(foreground, null, item.Icon!);
        }
    }

    /// <summary>Edge of the icon's box and the gap between it and the name.</summary>
    private const double IconSize = 18, IconGap = 3;

    /// <summary>The ring's thickness left for an icon beside the name: its box and gap, and 3 px to spare.</summary>
    private const double IconRoom = IconSize + IconGap + 3;

    /// <summary>The slice must be at least this wide for an icon (it is 18 px, and a slice narrows towards the hole).</summary>
    private const double IconMinSliceWidth = 28;

    /// <summary>How much bigger the icon of a fully lit slice is (a fraction of its size).</summary>
    private const double HotIconGrow = 0.12;

    /// <summary>The icon's scale when its slice is lit <paramref name="lit"/> (0 to 1): it follows the slice's hover spring.</summary>
    internal static double IconScale(double lit) => 1 + HotIconGrow * lit;

    /// <summary>What the callers of <see cref="DrawLabel"/> leave out of a slice's arc, as <c>maxWidth</c>, for the name.</summary>
    private const double SliceMargin = 10;

    /// <summary>
    /// Where a slice's name and icon go. With an icon and room for it (<see cref="IconMinSliceWidth"/> across,
    /// <see cref="IconRoom"/> of the ring left beside the name, and the icon's column down through the name inside the
    /// slice's wedge, which a tall name on a slice at the side of the donut would not leave) the icon sits above the name
    /// and the two together are centred on the slice; otherwise the name alone is, as if there were no icon: never an icon without
    /// its name. The icon grows about its own centre by <paramref name="iconScale"/>, which changes nothing else, so a
    /// slice does not lose its icon while it is highlighted.
    /// </summary>
    /// <param name="hasIcon">The slice has an icon.</param>
    /// <param name="center">The middle of the donut.</param>
    /// <param name="radius">Distance of the slice's middle (where the name is centred) from the middle of the donut.</param>
    /// <param name="angle">Direction of the slice's middle, clockwise from the top.</param>
    /// <param name="sliceWidth">How wide the slice is across at <paramref name="radius"/>, less a margin.</param>
    /// <param name="ringThickness">How thick the ring is.</param>
    /// <param name="textWidth">The width of the box the name is centred in.</param>
    /// <param name="textHeight">The height of the name and, if shown, its caption.</param>
    internal static (bool IconShown, Rect Icon, Rect Text) LabelLayout(bool hasIcon, Point center, double radius, double angle,
        double sliceWidth, double ringThickness, double textWidth, double textHeight, double iconScale = 1)
    {
        var anchor = DonutGeometry.PointAt(center, radius, angle);
        var withIcon = textHeight + IconSize + IconGap;
        var shown = hasIcon && sliceWidth >= IconMinSliceWidth && ringThickness - textHeight >= IconRoom &&
                    InsideSlice(new Rect(anchor.X - IconSize / 2, anchor.Y - withIcon / 2, IconSize, withIcon),
                        center, radius, angle, sliceWidth, ringThickness);
        var top = anchor.Y - (shown ? withIcon : textHeight) / 2;
        var icon = shown ? new Rect(anchor.X - IconSize / 2, top, IconSize, IconSize).Inflate(IconSize * (iconScale - 1) / 2) : default;
        var text = new Rect(anchor.X - textWidth / 2, shown ? top + IconSize + IconGap : top, textWidth, textHeight);
        return (shown, icon, text);
    }

    /// <summary>
    /// True when all four corners of <paramref name="column"/> are inside the slice: 1 px in from the ring's edges and
    /// within the slice's width (which narrows towards the hole). The name itself may be wider than the slice, as it always was.
    /// </summary>
    private static bool InsideSlice(Rect column, Point center, double radius, double angle, double sliceWidth, double ringThickness)
    {
        // The callers took SliceMargin off the arc for the name; a narrow column may use it, less the gap between slices.
        var halfAngle = (sliceWidth + SliceMargin - GapPixels) / (2 * radius);
        bool Inside(Point corner) =>
            Math.Abs(Distance(corner, center) - radius) <= ringThickness / 2 - 1 &&
            Math.Abs(Math.IEEERemainder(DonutGeometry.AngleOf(center, corner) - angle, 2 * Math.PI)) <= halfAngle;
        // No array of the corners: this runs for every label on every frame.
        return Inside(column.TopLeft) && Inside(column.TopRight) && Inside(column.BottomLeft) && Inside(column.BottomRight);
    }

    private Point EditButtonCenter(int index)
    {
        var mid = DonutGeometry.MidAngle(index, Count);
        return DonutGeometry.PointAt(Center, OuterRadius - 14 + PopOut, mid);
    }

    private void DrawEditButton(DrawingContext context, Point at)
    {
        context.DrawEllipse(AccentBrush, new Pen(CenterBrush, 2), at, 12, 12);
        var bounds = PencilIcon.Bounds;
        var s = 13 / Math.Max(bounds.Width, bounds.Height);
        var matrix = Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(at.X, at.Y);
        using (context.PushTransform(matrix))
            context.DrawGeometry(OnAccentBrush, null, PencilIcon);
    }

    /// <summary>
    /// Draws text horizontally centred on <paramref name="centerX"/>.
    /// The text is laid out with <see cref="TextAlignment.Center"/> inside a box of <see cref="FormattedText.MaxTextWidth"/>,
    /// so the box (not the measured <see cref="FormattedText.Width"/>) must be centred — otherwise short labels
    /// drift right by (box − text) / 2.
    /// </summary>
    internal static void DrawCentered(DrawingContext context, FormattedText text, double centerX, double top) =>
        context.DrawText(text, new Point(centerX - text.MaxTextWidth / 2, top));

    private Pen? BorderPen() => SliceBorderBrush is null ? null : new Pen(SliceBorderBrush, 1);

    private static FormattedText MakeText(string text, Typeface typeface, double size, IBrush? brush, double maxWidth, int maxLines = 1) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

    private static IBrush? WithOpacity(IBrush? brush, double opacity) => brush switch
    {
        ISolidColorBrush solid => new SolidColorBrush(solid.Color, solid.Opacity * opacity),
        _ => brush,
    };

    /// <summary>One more layer over the slice colour for a tool slice (the group's ring and the donut alike).</summary>
    private void DrawToolTint(DrawingContext context, DonutItem item, Geometry wedge)
    {
        if (item.IsAccent)
            context.DrawGeometry(WithOpacity(ToolBrush ?? AccentBrush, 0.30), null, wedge);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>Wedge-shaped slice with a constant-width gap to its neighbours.</summary>
    /// <summary>The translucent slice where a dragged preset would land, with its name.</summary>
    private void DrawPreview(DrawingContext context, Point center, double r1, double r2, Typeface bold, Typeface regular)
    {
        var mid = _previewMid.Value;
        var span = _previewSpan.Value;
        var wedge = CreateWedge(center, r1, r2, mid, span);
        var outline = new Pen(WithOpacity(AccentBrush, 0.9), 1.5, new DashStyle([4, 3], 0));
        context.DrawGeometry(WithOpacity(AccentBrush, 0.22), outline, wedge);
        if (_previewLabel is null || span < 0.25)
            return;
        using (context.PushOpacity(Math.Clamp((span - 0.25) / 0.3, 0, 1) * 0.9))
            DrawLabel(context, new DonutItem { Id = "", Label = _previewLabel }, center, (r1 + r2) / 2, mid,
                span * (r1 + r2) / 2 - 10, r2 - r1 - 16, r2 - r1, bold, regular, hot: false);
    }

    private static StreamGeometry CreateWedge(Point c, double r1, double r2, int index, int count) =>
        count == 1 ? CreateRing(c, r1, r2) : CreateWedge(c, r1, r2, DonutGeometry.MidAngle(index, count), DonutGeometry.Span(count));

    /// <summary>A slice of <paramref name="span"/> radians centred on <paramref name="mid"/> (a ring when it is all of it).</summary>
    private static StreamGeometry CreateWedge(Point c, double r1, double r2, double mid, double span)
    {
        if (span >= 2 * Math.PI - 0.001)
            return CreateRing(c, r1, r2);

        var outerHalf = Math.Max(0.001, span / 2 - GapPixels / 2 / r2);
        var innerHalf = Math.Max(0.001, span / 2 - GapPixels / 2 / r1);

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(DonutGeometry.PointAt(c, r2, mid - outerHalf), true);
        ctx.ArcTo(DonutGeometry.PointAt(c, r2, mid + outerHalf), new Size(r2, r2), 0, outerHalf * 2 > Math.PI, SweepDirection.Clockwise);
        ctx.LineTo(DonutGeometry.PointAt(c, r1, mid + innerHalf));
        ctx.ArcTo(DonutGeometry.PointAt(c, r1, mid - innerHalf), new Size(r1, r1), 0, innerHalf * 2 > Math.PI, SweepDirection.CounterClockwise);
        ctx.EndFigure(true);
        return geometry;
    }

    /// <summary>Full ring (used for a single slice and for the empty donut).</summary>
    private static StreamGeometry CreateRing(Point c, double r1, double r2)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.SetFillRule(FillRule.EvenOdd);
        foreach (var r in new[] { r2, r1 })
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), true);
            ctx.ArcTo(new Point(c.X, c.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.ArcTo(new Point(c.X, c.Y - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(true);
        }
        return geometry;
    }
}
