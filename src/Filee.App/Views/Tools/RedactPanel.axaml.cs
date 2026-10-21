// The redact tool's panel: the mode chips and the texts are in RedactPanel.axaml, bound to the image tool window's view
// model. The code here is what a binding can't say: the keys going back to the picture after the mouse.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Filee.App.Controls;

namespace Filee.App.Views.Tools;

/// <summary>The redact tool's panel in <see cref="ImageToolWindow"/> (its data context is the window's view model).</summary>
public partial class RedactPanel : UserControl
{
    public RedactPanel()
    {
        InitializeComponent();
        // Tunnelled and also when handled: the list box's own parts take the pointer, and the focus must go back whatever they did.
        ModeChips.AddHandler(PointerReleasedEvent, (_, _) => FocusPicture(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// The mouse is done with a chip: the focus goes to the picture, whose keys are the window's (← / → for the files, B / M,
    /// Delete). The chips take no focus of their own, but a click must not leave it somewhere that eats those keys.
    /// </summary>
    private void FocusPicture() => TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<PictureView>().FirstOrDefault()?.Focus();
}
