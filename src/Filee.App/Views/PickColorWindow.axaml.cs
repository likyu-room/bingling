using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Controls;
using Filee.App.Services;

namespace Filee.App.Views;

/// <summary>The instant eyedropper's window over one screen, with its picture taken before the window showed.</summary>
public partial class PickColorWindow : Window
{
    private ScreenShot? _shot;

    public PickColorWindow()
    {
        InitializeComponent();
    }

    /// <summary>A colour was picked: the desktop's pixel (physical) and its colour. The window has closed by then.</summary>
    public event Action<PixelPoint, Color>? Picked;

    /// <summary>Covers the screen of <paramref name="shot"/> and puts the magnifier at <paramref name="cursor"/> (physical pixels).</summary>
    public void Open(ScreenShot shot, PixelPoint cursor)
    {
        _shot = shot;
        Lens.Shot = shot;
        (Width, Height) = (shot.Bounds.Width / shot.Scaling, shot.Bounds.Height / shot.Scaling);
        Position = shot.Bounds.Position;
        Show();
        // Again at the scale the window really has there (a screen of another scale than the one it was made on): it
        // covers the screen's pixels exactly, and the pixels under the pointer come from where it really is.
        Position = shot.Bounds.Position;
        (Width, Height) = (shot.Bounds.Width / RenderScaling, shot.Bounds.Height / RenderScaling);
        MoveLens(this.PointToClient(cursor));
        Activate(); // for Esc
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MoveLens(e.GetPosition(this));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true; // a right press closes on its release (OnPointerReleased)
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && _shot is { } shot && shot.ColorAt(PixelAt(point.Position)) is { } color)
        {
            var at = shot.Bounds.Position + PixelAt(point.Position);
            Close();
            Picked?.Invoke(at, color);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // On the release, not the press: closed on the press, the release would go to the app below and open its menu.
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// The picture's pixel under a point of the window (DIPs), kept on the picture: the screen pixel the window says is
    /// there, less the screen's corner, so it holds whatever scale the window has.
    /// </summary>
    private PixelPoint PixelAt(Point p)
    {
        var shot = _shot!;
        var screen = this.PointToScreen(p);
        return new PixelPoint(
            Math.Clamp(screen.X - shot.Bounds.X, 0, shot.Bounds.Width - 1),
            Math.Clamp(screen.Y - shot.Bounds.Y, 0, shot.Bounds.Height - 1));
    }

    private void MoveLens(Point cursor)
    {
        if (_shot is null)
            return;
        Lens.Pixel = PixelAt(cursor);
        var at = Magnifier.PlaceBeside(cursor, new Size(Width, Height));
        Canvas.SetLeft(Lens, at.X);
        Canvas.SetTop(Lens, at.Y);
    }
}
