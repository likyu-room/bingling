// The screen tools a shortcut or the tray menu starts: the capture window and the colour picker.

using Avalonia;

namespace Filee.App.Services;

public interface IScreenTools
{
    /// <summary>Takes a picture of the screen the cursor is on and lets the user pick what to keep.</summary>
    void StartCapture(PixelPoint cursor);

    /// <summary>Lets the user pick a colour from the screen the cursor is on.</summary>
    void StartColorPick(PixelPoint cursor);
}
