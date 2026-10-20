// The crop tool's panel: all in CropPanel.axaml, bound to the image tool window's view model.

using Avalonia.Controls;

namespace Filee.App.Views.Tools;

/// <summary>The crop tool's panel in <see cref="ImageToolWindow"/> (its data context is the window's view model).</summary>
public partial class CropPanel : UserControl
{
    public CropPanel() => InitializeComponent();
}
