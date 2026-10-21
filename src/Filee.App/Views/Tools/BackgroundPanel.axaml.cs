// The add background tool's panel: the chips and sliders are in BackgroundPanel.axaml, bound to the image tool window's view
// model. The code here is what a binding can't say: a slider drag as one undo step (and the keys going back to the files
// after it), and the picture picker.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Filee.App.Controls;
using Filee.App.ViewModels.Tools;

namespace Filee.App.Views.Tools;

/// <summary>The add background tool's panel in <see cref="ImageToolWindow"/> (its data context is the window's view model).</summary>
public partial class BackgroundPanel : UserControl
{
    public BackgroundPanel()
    {
        InitializeComponent();
        foreach (var slider in new[] { PaddingSlider, CornerSlider, ShadowSlider })
        {
            // Tunnelled and also when handled: the slider's own parts take the pointer, and the view model must hear of it first.
            slider.AddHandler(PointerPressedEvent, (_, _) => Vm?.BeginAdjust(), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, (_, _) => EndPointerUse(), RoutingStrategies.Tunnel, handledEventsToo: true);
            // The capture can go without a release (Alt+Tab, a system dialog, a cancelled touch); the drag is over all the same.
            slider.AddHandler(PointerCaptureLostEvent, (_, _) => EndPointerUse(), RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }
    }

    private BackgroundToolViewModel? Vm => DataContext as BackgroundToolViewModel;

    /// <summary>
    /// The pointer is done with a slider: the drag ends, and the focus goes back to the picture. A slider that kept it would
    /// take ← / → itself, which are for the files (spec §3). Someone who tabs to a slider keeps the arrows there.
    /// </summary>
    private void EndPointerUse()
    {
        Vm?.EndAdjust();
        TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<PictureView>().FirstOrDefault()?.Focus();
    }

    private async void OnPickImage(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        // "All files" as well: a HEIC or TIFF photo is a picture to the converter, though not one to the "images" filter.
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            await Vm.UseImageAsync(path);
    }
}
