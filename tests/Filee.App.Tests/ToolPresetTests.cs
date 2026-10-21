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
        Assert.DoesNotContain(presets.Presets, p => p.Preset.Id is "crop" or "add-background" or "redact" or "annotate" or "tools");

        using var watch = new WatchFoldersPageViewModel(store, AppHost.Get<WatchFolderService>(), AppHost.Get<ILocalizer>());
        Assert.Contains(watch.Presets, p => p.Value == "strip-metadata");
        Assert.DoesNotContain(watch.Presets, p => p.Value is "crop" or "add-background" or "redact" or "annotate" or "tools");
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

    [AvaloniaFact]
    public void A_tool_preset_runs_with_one_edit_per_file_and_leaves_out_the_files_without_one()
    {
        var store = Store();
        var conversions = AppHost.Get<ConversionService>();
        var annotate = store.FindPreset("annotate")!;
        var folder = Path.Combine(Path.GetTempPath(), "filee-tests");
        var drawn = Path.Combine(folder, "drawn-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        var untouched = Path.Combine(folder, "untouched-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        var edit = new ImageEdit { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] };

        // Neither a preset-wide edit nor any file's own: nothing to run.
        Assert.Null(conversions.Start([drawn], annotate, showInToast: false));
        Assert.Null(conversions.Start([drawn], annotate, new Dictionary<string, ImageEdit>(), showInToast: false));
        Assert.Null(conversions.Start([untouched], annotate, new Dictionary<string, ImageEdit> { [drawn] = edit }, showInToast: false));

        // One card for the job; the file nothing was drawn on is not in it. Keys are matched without regard to case.
        var job = conversions.Start([drawn, untouched], annotate, new Dictionary<string, ImageEdit> { [drawn.ToUpperInvariant()] = edit }, showInToast: false);
        Assert.NotNull(job);
        Assert.Equal([drawn], job.Job.Sources);
        job.DismissCommand.Execute(null);
    }
}
