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
        Position = shot.Bounds.Position; // again, in case showing changed the DPI scale
        MoveLens(new Point((cursor.X - shot.Bounds.X) / shot.Scaling, (cursor.Y - shot.Bounds.Y) / shot.Scaling));
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
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && _shot is { } shot && shot.ColorAt(PixelAt(point.Position)) is { } color)
        {
            var at = shot.Bounds.Position + PixelAt(point.Position);
            Close();
            Picked?.Invoke(at, color);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
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

    /// <summary>The picture's pixel under a point of the window (DIPs), kept on the picture.</summary>
    private PixelPoint PixelAt(Point p)
    {
        var shot = _shot!;
        return new PixelPoint(
            Math.Clamp((int)Math.Floor(p.X * shot.Scaling), 0, shot.Bounds.Width - 1),
            Math.Clamp((int)Math.Floor(p.Y * shot.Scaling), 0, shot.Bounds.Height - 1));
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
