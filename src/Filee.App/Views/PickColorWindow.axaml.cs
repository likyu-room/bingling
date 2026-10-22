using Avalonia;
using Avalonia.Input;
using Filee.App.Services;

namespace Filee.App.Views;

/// <summary>The instant eyedropper's window over one screen, with its picture taken before the window showed.</summary>
public partial class PickColorWindow : ScreenCoverWindow
{
    public PickColorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Covers the screen of <paramref name="shot"/> and puts the magnifier at <paramref name="cursor"/> (physical pixels).</summary>
    public void Open(ScreenShot shot, PixelPoint cursor)
    {
        Lens.Shot = shot;
        Cover(shot);
        MoveLens(this.PointToClient(cursor));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MoveLens(e.GetPosition(this));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true; // a right press closes on its release (ScreenCoverWindow)
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            PickAt(point.Position);
    }

    private void MoveLens(Point cursor)
    {
        if (Shot is not null)
            Lens.Follow(cursor, PixelAt(cursor), new Size(Width, Height));
    }
}
