// A window over one whole screen, with the picture of it taken before the window showed: the instant eyedropper's and the
// capture window's. It covers the screen's pixels exactly with that picture, tells which of the picture's pixels a point
// of it is on, picks a colour there, and closes on Esc or a right click.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Views;

public class ScreenCoverWindow : Window
{
    /// <summary>The screen's picture, from <see cref="Cover"/> on.</summary>
    protected ScreenShot? Shot { get; private set; }

    /// <summary>A colour was picked: the desktop's pixel (physical) and its colour. The window has closed by then.</summary>
    public event Action<PixelPoint, Color>? Picked;

    /// <summary>
    /// Shows the window over the screen of <paramref name="shot"/>, the picture in <paramref name="frozen"/> (the screen
    /// frozen: what shows is what a pick reads), and takes the focus (for Esc).
    /// </summary>
    protected void Cover(ScreenShot shot, Image frozen)
    {
        Shot = shot;
        var picture = ResultClipboard.ToBitmap(shot.Image);
        frozen.Source = picture;
        Closed += (_, _) =>
        {
            frozen.Source = null;
            picture.Dispose();
        };
        (Width, Height) = (shot.Bounds.Width / shot.Scaling, shot.Bounds.Height / shot.Scaling);
        Position = shot.Bounds.Position;
        Show();
        // Again at the scale the window really has there (a screen of another scale than the one it was made on): it
        // covers the screen's pixels exactly, and the pixels under the pointer come from where it really is.
        Position = shot.Bounds.Position;
        (Width, Height) = (shot.Bounds.Width / RenderScaling, shot.Bounds.Height / RenderScaling);
        Activate();
    }

    /// <summary>
    /// The picture's pixel under a point of the window (DIPs), kept on the picture: the screen pixel the window says is
    /// there, less the screen's corner, so it holds whatever scale the window has.
    /// </summary>
    protected PixelPoint PixelAt(Point p)
    {
        var shot = Shot!;
        var screen = this.PointToScreen(p);
        return new PixelPoint(
            Math.Clamp(screen.X - shot.Bounds.X, 0, shot.Bounds.Width - 1),
            Math.Clamp(screen.Y - shot.Bounds.Y, 0, shot.Bounds.Height - 1));
    }

    /// <summary>Picks the colour under <paramref name="p"/> (DIPs): the window closes, then <see cref="Picked"/> tells.</summary>
    protected void PickAt(Point p)
    {
        if (Shot is not { } shot || shot.ColorAt(PixelAt(p)) is not { } color)
            return;
        var at = shot.Bounds.Position + PixelAt(p);
        Close();
        Picked?.Invoke(at, color);
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
}
