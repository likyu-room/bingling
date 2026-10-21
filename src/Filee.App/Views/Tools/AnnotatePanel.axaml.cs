// The annotate tool's panel: the tool cells, colour and thickness chips are in AnnotatePanel.axaml, bound to the image tool
// window's view model. The code here is what a binding can't say: the keys going back to the picture after the mouse.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Filee.App.Controls;

namespace Filee.App.Views.Tools;

/// <summary>The annotate tool's panel in <see cref="ImageToolWindow"/> (its data context is the window's view model).</summary>
public partial class AnnotatePanel : UserControl
{
    public AnnotatePanel()
    {
        InitializeComponent();
        // Tunnelled and also when handled: the list boxes' own parts take the pointer, and the focus must go back whatever they did.
        foreach (var list in new[] { ToolCells, ColorChips, WidthChips })
            list.AddHandler(PointerReleasedEvent, (_, _) => FocusPicture(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// The mouse is done with a cell or a chip: the focus goes to the picture, whose keys are the window's (← / → for the
    /// files, the tool keys, [ and ], Delete). The chips take no focus of their own, but a click must not leave it somewhere
    /// that eats those keys.
    /// </summary>
    private void FocusPicture() => TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<PictureView>().FirstOrDefault()?.Focus();
}
