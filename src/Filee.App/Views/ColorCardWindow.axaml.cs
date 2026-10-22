using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Filee.App.Services;

namespace Filee.App.Views;

/// <summary>The eyedropper's colour card (<see cref="ViewModels.ColorCardViewModel"/>), shown beside the picked point.</summary>
public partial class ColorCardWindow : Window
{
    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(6);

    private readonly DispatcherTimer _timer;
    private PixelPoint _at;

    public ColorCardWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        _timer = new DispatcherTimer(ShownFor, DispatcherPriority.Normal, (_, _) => Close());
        SizeChanged += (_, _) => Place();
        Deactivated += (_, _) => Close(); // a click outside the card
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Puts <paramref name="text"/> on this window's clipboard.</summary>
    public Task SetTextAsync(string text) =>
        Clipboard?.SetTextAsync(text) ?? throw new InvalidOperationException("The card window has no clipboard");

    /// <summary>Shows the card beside <paramref name="at"/> (the picked pixel, physical) and takes the focus, for Esc.</summary>
    public void ShowNear(PixelPoint at)
    {
        _at = at;
        Position = at;
        Show();
        Place();
        Activate();
        _timer.Start();
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

    private void Place()
    {
        if (!IsVisible || (Screens.ScreenFromPoint(_at) ?? Screens.Primary) is not { } screen)
            return;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * screen.Scaling), (int)Math.Ceiling(Bounds.Height * screen.Scaling));
        Position = Place(_at, size, screen.WorkingArea, screen.Scaling);
    }

    /// <summary>
    /// The top-left corner of a <paramref name="card"/> for a point picked <paramref name="at"/> (physical pixels): below and
    /// to the right of it, above or left of it where the working <paramref name="area"/> ends, and never off the area.
    /// </summary>
    internal static PixelPoint Place(PixelPoint at, PixelSize card, PixelRect area, double scaling)
    {
        var gap = (int)Math.Round(16 * scaling);
        var x = at.X + gap + card.Width <= area.Right ? at.X + gap : at.X - gap - card.Width;
        var y = at.Y + gap + card.Height <= area.Bottom ? at.Y + gap : at.Y - gap - card.Height;
        return new PixelPoint(
            Math.Clamp(x, area.X, Math.Max(area.X, area.Right - card.Width)),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - card.Height)));
    }
}
