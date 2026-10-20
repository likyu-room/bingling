// Tests for the preset model of the image tools: the "tools" group on the image donut, crop (a tool preset), and
// compress / strip metadata (ordinary same-format presets).

using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class ImageToolPresetTests
{
    private static Preset P(string id) => BuiltInData.CreatePresets().Single(p => p.Id == id);

    [Fact]
    public void Tools_group_fans_out_crop_compress_and_strip_metadata()
    {
        var tools = P("tools");
        Assert.Equal(PresetKind.Group, tools.Kind);
        Assert.Equal(Preset.GroupTarget, tools.TargetFormat);
        Assert.Equal(["crop", "compress-image", "strip-metadata"], tools.SubPresetIds);
        Assert.Equal(PresetKind.Tool, P("crop").Kind);
        Assert.Equal(ImageToolKind.Crop, P("crop").Tool);
        Assert.Equal("{name} Cropped", P("crop").Output.FileNamePattern);
        Assert.Equal((BuiltInData.SameAsSource, "preset.crop"), (P("crop").TargetFormat, P("crop").NameKey));
    }

    [Fact]
    public void Compress_and_strip_metadata_are_ordinary_same_format_presets()
    {
        var c = P("compress-image"); var s = P("strip-metadata");
        Assert.True(c.IsConversion && s.IsConversion);
        Assert.Equal((BuiltInData.SameAsSource, 80, false, true, "{name} Compressed"),
            (c.TargetFormat, c.Image.Quality, c.Image.KeepMetadata, c.Image.Optimize, c.Output.FileNamePattern));
        Assert.Equal((BuiltInData.SameAsSource, 95, false, true, "{name} Clean"),
            (s.TargetFormat, s.Image.Quality, s.Image.KeepMetadata, s.Image.Optimize, s.Output.FileNamePattern));
    }

    [Fact]
    public void The_image_donut_has_the_tools_slice_before_half_size_and_still_fits()
    {
        var images = BuiltInData.CreateProfiles().Single(p => p.Id == "images");
        Assert.Equal(11, images.PresetIds.Count);
        Assert.Equal("tools", images.PresetIds[images.PresetIds.IndexOf("half-size") - 1]);
        Assert.True(images.PresetIds.Count <= 12);
    }

    [Fact]
    public void Tool_presets_are_not_conversions_and_survive_a_round_trip()
    {
        var crop = P("crop");
        Assert.False(crop.IsConversion);
        crop.Image.Edit = new ImageEdit { Crop = new CropRect { X = .1, Y = .2, Width = .5, Height = .4, Aspect = 1 } };
        // As the store writes presets (FileeJsonContext), so a source-generated type or converter that's missing shows here.
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(crop, FileeJsonContext.Default.Preset), FileeJsonContext.Default.Preset)!;
        Assert.Equal((PresetKind.Tool, ImageToolKind.Crop, .5, 1.0), (copy.Kind, copy.Tool, copy.Image.Edit!.Crop!.Width, copy.Image.Edit.Crop.Aspect));
        Assert.NotSame(crop.Image.Edit, crop.Clone().Image.Edit);
        Assert.NotSame(crop.Image.Edit.Crop, crop.Clone().Image.Edit!.Crop);
        Assert.Equal((PresetKind.Tool, ImageToolKind.Crop), (crop.Clone().Kind, crop.Clone().Tool));
    }

    [Fact]
    public void A_clone_of_the_image_options_keeps_optimize_and_does_not_share_the_edit()
    {
        var options = new ImageOptions { Optimize = true, Edit = new ImageEdit { Crop = new CropRect { Width = .5 } } };
        var copy = options.Clone();
        Assert.True(copy.Optimize);
        copy.Edit!.Crop!.Width = .9;
        Assert.Equal(.5, options.Edit.Crop.Width);
    }

    [Fact]
    public void A_crop_counts_as_turning_so_only_engines_that_transform_images_run_it()
    {
        Assert.False(new ImageOptions().TurnsOrMirrors);
        Assert.False(new ImageOptions { Edit = new ImageEdit() }.TurnsOrMirrors); // an edit that does nothing
        Assert.True(new ImageOptions { Edit = new ImageEdit { Crop = new CropRect() } }.TurnsOrMirrors);
        Assert.True(new ImageOptions { Rotation = ImageRotation.Left90 }.TurnsOrMirrors);
        Assert.True(new ImageOptions { Flip = ImageFlip.Horizontal }.TurnsOrMirrors);
    }

    [Fact]
    public void A_tool_kind_from_a_newer_Filee_loads_and_the_preset_kind_Tool_is_read_by_name()
    {
        var json = """{ "id": "x", "kind": "Tool", "tool": "Hologram", "targetFormat": "*" }""";
        var preset = JsonSerializer.Deserialize(json, FileeJsonContext.Default.Preset)!;
        Assert.Equal(PresetKind.Tool, preset.Kind);
        Assert.Equal(ImageToolKind.Crop, preset.Tool); // the first value: the preset still loads
    }

    [Fact]
    public void A_store_offers_no_tool_preset_where_a_conversion_is_picked()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        Assert.Null(store.FindConversion("crop"));
        Assert.Null(store.FindConversion("tools"));
        Assert.NotNull(store.FindConversion("compress-image"));
        Assert.NotNull(store.FindPreset("crop")); // still found for the donut
    }

    [Fact]
    public void A_group_keeps_its_tool_presets_after_a_save_and_load()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Equal(["crop", "compress-image", "strip-metadata"], reloaded.FindPreset("tools")!.SubPresetIds);
        Assert.Equal(PresetKind.Tool, reloaded.FindPreset("crop")!.Kind);
        Assert.Contains("tools", reloaded.Profiles.Single(p => p.Id == "images").PresetIds);
    }

    [Fact]
    public void The_image_tool_settings_start_on_the_original_ratio_and_are_part_of_the_settings()
    {
        Assert.Equal("original", new ImageToolSettings().CropAspect);
        Assert.Equal("original", new AppSettings().ImageTools.CropAspect);
        var copy = new ImageToolSettings { CropAspect = "16:9" }.Clone();
        Assert.Equal("16:9", copy.CropAspect);
    }

    [Fact]
    public async Task A_crop_is_asked_for_once_in_the_step_that_transforms_and_not_in_the_others()
    {
        using var dir = new TempDir();
        var eps = dir.File("art.eps");
        var turner = new FakeTurner("magick", new ConversionEdge("png", "jpg"));
        var renderer = new FakeTurner("ghostscript", new ConversionEdge("eps", "png"));
        await using var queue = new JobQueue(new ConverterCatalog([turner, renderer]));
        var preset = new Preset
        {
            TargetFormat = "jpg",
            Image = { Edit = new ImageEdit { Crop = new CropRect { Width = .5, Height = .5 } } },
        };

        var job = new ConversionJob([eps], preset, "Test");
        await queue.RunAsync(job);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal([("png", true)], renderer.EditAsked);
        Assert.Equal([("jpg", false)], turner.EditAsked);
        Assert.NotNull(preset.Image.Edit); // the preset itself keeps the edit
    }
}
