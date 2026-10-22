using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Views;

/// <summary>The capture window over one screen (<see cref="CaptureViewModel"/>), with its picture taken before the window showed.</summary>
public partial class CaptureWindow : ScreenCoverWindow
{
    /// <summary>Between the area and its buttons, and between the size tag and the area (DIPs).</summary>
    private const double Gap = 8, TagGap = 4;

    private PixelPoint? _dragFrom;
    private Point _pointer;

    public CaptureWindow()
    {
        InitializeComponent();
    }

    internal CaptureViewModel ViewModel => (CaptureViewModel)DataContext!;

    /// <summary>Covers the screen of <paramref name="vm"/>'s picture with it, frozen; <paramref name="cursor"/> is where the pointer is (physical).</summary>
    public void Open(CaptureViewModel vm, PixelPoint cursor)
    {
        DataContext = vm;
        Lens.Shot = vm.Shot;
        vm.Done += OnDone;
        vm.PropertyChanged += OnChanged;
        Closed += (_, _) =>
        {
            vm.Done -= OnDone;
            vm.PropertyChanged -= OnChanged;
        };
        Cover(vm.Shot, Frozen);
        _pointer = this.PointToClient(cursor);
        ShowArea();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ReferenceEquals(e.Source, Frozen)) // the toolbar's, the buttons'
            return;
        e.Handled = true; // a right press closes on its release (ScreenCoverWindow)
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (ViewModel.PicksColor)
        {
            PickAt(point.Position);
            return;
        }
        var from = PixelAt(point.Position);
        _dragFrom = from;
        ViewModel.DragTo(from, from);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        if (_dragFrom is { } from)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                ViewModel.DragTo(from, PixelAt(_pointer));
            else
                EndDrag(); // its release went elsewhere: the area stays as it was
        }
        else if (ViewModel.PicksColor)
            Lens.Follow(_pointer, PixelAt(_pointer), new Size(Width, Height));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left)
            EndDrag();
    }

    private void EndDrag()
    {
        if (_dragFrom is null)
            return;
        _dragFrom = null;
        ViewModel.EndDrag();
    }

    private void OnDone(object? sender, EventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); // Esc closes
        var command = (e.Key, e.KeyModifiers) switch
        {
            (Key.Enter, KeyModifiers.None) or (Key.C, KeyModifiers.Control) => ViewModel.CopyCommand,
            (Key.S, KeyModifiers.Control) => ViewModel.SaveCommand,
            _ => null,
        };
        if (e.Handled || command is null)
            return;
        e.Handled = true;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    /// <summary>What the area looks like follows these; the rest are worked out from them.</summary>
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CaptureViewModel.Selection) or nameof(CaptureViewModel.Mode) or nameof(CaptureViewModel.ShowsActions))
            ShowArea();
    }

    /// <summary>
    /// The screen dimmed but for the area, framed, its size above its corner (inside it at the top of the screen) and its
    /// buttons below it; the eyedropper dims the screen a little only, and shows the magnifier at the pointer.
    /// </summary>
    private void ShowArea()
    {
        var vm = ViewModel;
        var screen = new Size(Width, Height);
        Dim.Opacity = vm.PicksColor ? .18 : .5;
        Lens.IsVisible = vm.PicksColor;
        if (vm.PicksColor)
            Lens.Follow(_pointer, PixelAt(_pointer), screen);
        Frame.IsVisible = SizeTag.IsVisible = vm.HasSelection;
        Actions.IsVisible = vm.ShowsActions;
        if (vm.Selection is not { } selection)
        {
            Dim.Data = new RectangleGeometry(new Rect(screen));
            return;
        }

        var area = ClientRect(selection);
        Dim.Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(new Rect(screen)), new RectangleGeometry(area) },
        };
        Place(Frame, area.Position);
        (Frame.Width, Frame.Height) = (area.Width, area.Height);
        SizeLabel.Text = vm.SizeText;
        Place(SizeTag, area.Y >= SizeTag.Height + TagGap
            ? new Point(area.X, area.Y - SizeTag.Height - TagGap)
            : new Point(area.X + TagGap, area.Y + TagGap));
        Actions.Measure(Size.Infinity);
        Place(Actions, PlaceActions(area, Actions.DesiredSize, screen));
    }

    /// <summary>The picture's <paramref name="area"/> (pixels) in the window (DIPs), from where the window says those pixels are.</summary>
    private Rect ClientRect(PixelRect area)
    {
        var origin = Shot!.Bounds.Position;
        return new Rect(this.PointToClient(origin + area.Position), this.PointToClient(origin + new PixelPoint(area.Right, area.Bottom)));
    }

    private static void Place(Control control, Point at)
    {
        Canvas.SetLeft(control, at.X);
        Canvas.SetTop(control, at.Y);
    }

    /// <summary>
    /// Where the Edit / Copy / Save <paramref name="buttons"/> of an <paramref name="area"/> go on a <paramref name="screen"/>
    /// (DIPs): under its left end; above it where the screen ends below it; inside its bottom when it is as high as the
    /// screen; and never off the screen.
    /// </summary>
    internal static Point PlaceActions(Rect area, Size buttons, Size screen)
    {
        var y = area.Bottom + Gap + buttons.Height <= screen.Height ? area.Bottom + Gap
            : area.Y - Gap - buttons.Height >= 0 ? area.Y - Gap - buttons.Height
            : area.Bottom - Gap - buttons.Height;
        return new Point(Math.Clamp(area.X, 0, Math.Max(0, screen.Width - buttons.Width)), Math.Max(0, y));
    }
}
