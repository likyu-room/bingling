// The eyedropper and copy buttons of a tool's custom colour (ColorToolButtons.axaml), bound to the image tool window's view
// model. The code here is what a binding can't say: the keys going back to the picture after the mouse.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Filee.App.Controls;

namespace Filee.App.Views.Tools;

/// <summary>The eyedropper and copy buttons under the custom colour box (its data context is the window's view model).</summary>
public partial class ColorToolButtons : UserControl
{
    public ColorToolButtons()
    {
        InitializeComponent();
        // Neither button takes focus of its own; the press hands it to the picture, so the keys (I, Esc, the tool keys) are the
        // picture's after it, and a text being typed on it is put there first.
        foreach (var button in new Control[] { EyedropperButton, CopyButton })
            button.AddHandler(PointerPressedEvent, (_, _) => FocusPicture(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void FocusPicture() => TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<PictureView>().FirstOrDefault()?.Focus();
}
