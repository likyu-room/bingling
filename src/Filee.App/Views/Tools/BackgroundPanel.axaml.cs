// The add background tool's panel: the chips and sliders are in BackgroundPanel.axaml, bound to the image tool window's view
// model. The code here is what a binding can't say: a slider drag as one undo step, and the picture picker.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            slider.AddHandler(PointerReleasedEvent, (_, _) => Vm?.EndAdjust(), RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private BackgroundToolViewModel? Vm => DataContext as BackgroundToolViewModel;

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
