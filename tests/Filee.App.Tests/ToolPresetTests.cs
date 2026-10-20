// Where a tool preset (crop) is and isn't offered: the preset list and watch folders pick conversions only, the donut
// right-click has nothing to edit, and it doesn't run before its tool has made an edit.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class ToolPresetTests
{
    private static UserDataStore Store()
    {
        TestServices.EnsureInitialized("en");
        return AppHost.Get<UserDataStore>();
    }

    [AvaloniaFact]
    public void The_preset_list_and_watch_folders_offer_conversions_but_no_group_or_tool()
    {
        var store = Store();
        var presets = new PresetsPageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>());
        Assert.Contains(presets.Presets, p => p.Preset.Id == "compress-image");
        Assert.DoesNotContain(presets.Presets, p => p.Preset.Id is "crop" or "tools");

        using var watch = new WatchFoldersPageViewModel(store, AppHost.Get<WatchFolderService>(), AppHost.Get<ILocalizer>());
        Assert.Contains(watch.Presets, p => p.Value == "strip-metadata");
        Assert.DoesNotContain(watch.Presets, p => p.Value is "crop" or "tools");
    }

    [AvaloniaFact]
    public async Task A_tool_preset_has_no_editor_and_does_not_run_without_its_edit()
    {
        var store = Store();
        var crop = store.FindPreset("crop")!;

        // The donut's right-click asks to edit it: nothing opens and nothing was saved.
        Assert.False(await AppHost.Get<WindowService>().EditPresetAsync(crop, null));

        var conversions = AppHost.Get<ConversionService>();
        var file = Path.Combine(Path.GetTempPath(), "filee-tests", "crop-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        Assert.Null(conversions.Start([file], crop, showInToast: false));
        Assert.Null(conversions.Start([file], store.FindPreset("tools")!, showInToast: false));

        // With the box its tool made, it runs.
        var cropped = crop.Clone();
        cropped.Image.Edit = new ImageEdit { Crop = new CropRect { Width = .5, Height = .5 } };
        var job = conversions.Start([file], cropped, showInToast: false);
        Assert.NotNull(job);
        job.DismissCommand.Execute(null);
    }
}
