// The screen tools a shortcut or the tray starts (IScreenTools): the capture window, the instant eyedropper and the colour
// card an eyedropper leaves. Their public methods never throw: the tray calls them from async void handlers. Where the
// platform takes no picture of the screen (macOS, Linux) nothing opens; their shortcuts and tray items are Windows-only anyway.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public sealed class ScreenTools(IPlatformServices platform, ILogger<ScreenTools> log, CaptureOutput output, MemoryTrimmer? trimmer = null) : IScreenTools
{
    /// <summary>How long a colour card still up gets to leave the screen before the next picture of it is taken.</summary>
    private static readonly TimeSpan CardGoneAfter = TimeSpan.FromMilliseconds(150);

    // UI thread only.
    private ScreenCoverWindow? _cover; // the capture window or the eyedropper: one at a time
    private ColorCardWindow? _card;
    private bool _starting; // waiting for a card to go: a shortcut again does nothing meanwhile

    /// <summary>The eyedropper's window while it is open.</summary>
    internal PickColorWindow? Picker => _cover as PickColorWindow;

    /// <summary>The capture window while it is open.</summary>
    internal CaptureWindow? Capture => _cover as CaptureWindow;

    /// <summary>The colour card while it is open; one at a time.</summary>
    internal ColorCardWindow? Card => _card;

    /// <summary>
    /// Takes a picture of the screen under <paramref name="cursor"/> and covers it with the capture window, the picture
    /// frozen: a drag picks an area to edit, redact, copy or save, and its eyedropper picks a colour as the instant one does.
    /// </summary>
    public void StartCapture(PixelPoint cursor) => Start<CaptureWindow>(cursor, "screen capture", (window, shot) =>
        window.Open(new CaptureViewModel(shot, output)
        {
            CopyPicture = picture => window.Clipboard is { } clipboard ? ResultClipboard.CopyPictureAsync(clipboard, picture, log) : Task.FromResult(false),
        }, cursor));

    /// <summary>
    /// Takes a picture of the screen under <paramref name="cursor"/> and covers it with the eyedropper; a click there copies
    /// that pixel's HEX code and shows the colour card.
    /// </summary>
    public void StartColorPick(PixelPoint cursor) => Start<PickColorWindow>(cursor, "colour picker", (window, shot) => window.Open(shot, cursor));

    /// <summary>
    /// Takes the picture and opens <typeparamref name="TWindow"/> over it (<paramref name="open"/>); the picture is let go of
    /// when the window closes. A card still up closes first, a moment before the picture. While a screen tool is open (or
    /// waiting for the card), starting one again does nothing; when the screen can't be taken (or is black) nothing opens
    /// and the log says so. Once the window has closed the memory is given back (the trimmer, as after the editor windows): a
    /// 4K screen is about a hundred MB of pictures.
    /// </summary>
    private async void Start<TWindow>(PixelPoint cursor, string tool, Action<TWindow, ScreenShot> open) where TWindow : ScreenCoverWindow, new()
    {
        if (_cover is not null || _starting)
            return;
        _starting = true;
        TWindow? window = null;
        ScreenShot? shot = null;
        try
        {
            // A card still up would be in the picture, and its colours picked where the screen no longer shows it: it goes
            // first, and the compositor gets a moment to take it off the screen (as the tray waits for its menu).
            if (_card is { } card)
            {
                card.Close();
                await Task.Delay(CardGoneAfter);
            }
            window = new TWindow();
            shot = ScreenGrab.Grab(platform, window.Screens, cursor);
            if (shot is null)
            {
                log.LogInformation("The {Tool} does not open: the screen at {Cursor} could not be taken, or came out black", tool, cursor);
                window.Close();
                return;
            }
            var taken = shot;
            _cover = window;
            window.Closed += (_, _) =>
            {
                _cover = null;
                taken.Dispose();
                trimmer?.Request();
            };
            window.Picked += ShowCard;
            open(window, shot);
            RaiseTopmost(window);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Could not start the {Tool} at {Cursor}", tool, cursor);
            _cover = null;
            window?.Close();
            shot?.Dispose();
        }
        finally
        {
            _starting = false;
        }
    }

    /// <summary>The colour card for a colour picked <paramref name="at"/>; it puts the HEX code on the clipboard as it opens.</summary>
    private async void ShowCard(PixelPoint at, Color color)
    {
        try
        {
            _card?.Close();
            var card = new ColorCardWindow();
            var vm = new ColorCardViewModel(color, card.SetTextAsync, log);
            card.DataContext = vm;
            _card = card;
            card.Closed += (_, _) =>
            {
                if (_card == card)
                    _card = null;
            };
            card.ShowNear(at);
            RaiseTopmost(card);
            await vm.CopyHexOnOpenAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Could not show the colour card");
        }
    }

    /// <summary>On top of every window, as the donut (Windows can push a topmost window below normal ones).</summary>
    private void RaiseTopmost(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle)
            platform.RaiseTopmost(handle);
    }
}
