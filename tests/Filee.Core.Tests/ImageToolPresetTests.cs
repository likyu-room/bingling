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
    public void Tools_group_has_six_tools_in_spec_order()
    {
        var tools = P("tools");
        Assert.Equal(PresetKind.Group, tools.Kind);
        Assert.Equal(Preset.GroupTarget, tools.TargetFormat);
        Assert.Equal(["crop", "add-background", "redact", "annotate", "compress-image", "strip-metadata"], tools.SubPresetIds);
        Assert.Equal(Preset.MaxSubPresets, tools.SubPresetIds.Count); // the ring is full
        Assert.Equal(PresetKind.Tool, P("crop").Kind);
        Assert.Equal(ImageToolKind.Crop, P("crop").Tool);
        Assert.Equal("{name} Cropped", P("crop").Output.FileNamePattern);
        Assert.Equal((BuiltInData.SameAsSource, "preset.crop"), (P("crop").TargetFormat, P("crop").NameKey));
    }

    [Fact]
    public void Three_new_tool_presets_have_their_targets_and_names()
    {
        (string Id, string NameKey, ImageToolKind Tool, string Target, string Pattern, bool KeepMetadata)[] expected =
        [
            ("add-background", "preset.add_background", ImageToolKind.AddBackground, "png", "{name} with background", true),
            ("redact", "preset.redact", ImageToolKind.Redact, BuiltInData.SameAsSource, "{name} Redacted", false),
            ("annotate", "preset.annotate", ImageToolKind.Annotate, BuiltInData.SameAsSource, "{name} Annotated", true),
        ];
        foreach (var (id, nameKey, tool, target, pattern, keepMetadata) in expected)
        {
            var preset = P(id);
            Assert.Equal(
                (PresetKind.Tool, tool, nameKey, target, pattern, 95, keepMetadata),
                (preset.Kind, preset.Tool, preset.NameKey, preset.TargetFormat, preset.Output.FileNamePattern, preset.Image.Quality,
                    preset.Image.KeepMetadata));
            Assert.False(preset.IsConversion);
            Assert.Null(preset.Image.Edit); // the tool's window makes it
        }
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
        // Compress keeps only a smaller file; stripping the metadata is the point of the other, whatever the size.
        Assert.True(c.Image.OnlyIfSmaller);
        Assert.False(s.Image.OnlyIfSmaller);
        Assert.False(new ImageOptions().OnlyIfSmaller);
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
    public void Edit_with_drawing_counts_as_a_transform()
    {
        var box = new EditRect { X = .1, Y = .1, Width = .2, Height = .2 };
        // Nothing to draw: not a transform, so any engine can still write the file.
        Assert.False(new ImageEdit().Draws);
        Assert.False(new ImageEdit { Redact = new RedactEdit() }.Draws);
        Assert.False(new ImageEdit { Annotations = [] }.Draws);
        Assert.False(new ImageOptions { Edit = new ImageEdit { Redact = new RedactEdit(), Annotations = [] } }.TurnsOrMirrors);

        ImageEdit[] drawing =
        [
            new() { Redact = new RedactEdit { Boxes = [box] } },
            new() { Annotations = [new Annotation { Kind = AnnotationKind.Arrow }] },
            new() { Background = new BackgroundEdit() },
        ];
        foreach (var edit in drawing)
        {
            Assert.True(edit.Draws);
            Assert.True(new ImageOptions { Edit = edit }.TurnsOrMirrors); // only engines that transform images run it
        }
    }

    [Fact]
    public void Image_edit_round_trips_through_json()
    {
        var preset = P("annotate");
        preset.Image.Edit = new ImageEdit
        {
            Crop = new CropRect { X = .1, Y = .2, Width = .5, Height = .4, Aspect = 1 },
            Redact = new RedactEdit
            {
                Mode = RedactMode.Mosaic,
                Boxes = [new EditRect { X = .1, Y = .2, Width = .3, Height = .4 }, new EditRect { X = .5, Y = .5, Width = .25, Height = .125 }],
            },
            Annotations =
            [
                new Annotation
                {
                    Kind = AnnotationKind.Arrow, Color = "#00AAFF", Width = StrokeLevel.Thick,
                    Points = [new EditPoint { X = .1, Y = .2 }, new EditPoint { X = .8, Y = .9 }],
                },
                new Annotation { Kind = AnnotationKind.Text, Text = "Hello 한글", Points = [new EditPoint { X = .5, Y = .5 }] },
                new Annotation { Kind = AnnotationKind.Number, Number = 3, Points = [new EditPoint { X = .25, Y = .75 }] },
            ],
            Background = new BackgroundEdit
            {
                Kind = BackgroundKind.Image,
                Value = "#112233",
                ImagePath = "wall.jpg",
                Padding = .2,
                Corner = .1,
                Shadow = 70,
                Aspect = "16:9",
            },
        };

        // As the store writes presets (FileeJsonContext), so a source-generated type or converter that's missing shows here.
        var json = JsonSerializer.Serialize(preset, FileeJsonContext.Default.Preset);
        var copy = JsonSerializer.Deserialize(json, FileeJsonContext.Default.Preset)!;

        var edit = copy.Image.Edit!;
        Assert.Equal((.5, 1.0), (edit.Crop!.Width, edit.Crop.Aspect));
        Assert.Equal(RedactMode.Mosaic, edit.Redact!.Mode);
        Assert.Equal([(.1, .2, .3, .4), (.5, .5, .25, .125)], edit.Redact.Boxes.Select(b => (b.X, b.Y, b.Width, b.Height)));
        Assert.Equal([AnnotationKind.Arrow, AnnotationKind.Text, AnnotationKind.Number], edit.Annotations!.Select(a => a.Kind));
        var arrow = edit.Annotations![0];
        Assert.Equal(("#00AAFF", StrokeLevel.Thick, 2), (arrow.Color, arrow.Width, arrow.Points.Count));
        Assert.Equal((.8, .9), (arrow.Points[1].X, arrow.Points[1].Y));
        Assert.Equal("Hello 한글", edit.Annotations[1].Text);
        Assert.Equal(3, edit.Annotations[2].Number);
        var background = edit.Background!;
        Assert.Equal((BackgroundKind.Image, "#112233", "wall.jpg", .2, .1, 70, "16:9"),
            (background.Kind, background.Value, background.ImagePath, background.Padding, background.Corner, background.Shadow, background.Aspect));
        // Nothing else was lost or added on the way: the copy writes the same file.
        Assert.Equal(json, JsonSerializer.Serialize(copy, FileeJsonContext.Default.Preset));

        // A clone shares nothing with the original.
        var clone = preset.Image.Clone().Edit!;
        clone.Redact!.Boxes[0].X = .9;
        clone.Annotations![0].Points[0].X = .9;
        clone.Annotations[1].Text = "changed";
        clone.Background!.Padding = .4;
        Assert.Equal((.1, .1, "Hello 한글", .2),
            (preset.Image.Edit!.Redact!.Boxes[0].X, preset.Image.Edit.Annotations![0].Points[0].X, preset.Image.Edit.Annotations[1].Text,
                preset.Image.Edit.Background!.Padding));
    }

    [Fact]
    public void A_value_from_a_newer_Filee_loads_as_the_first_one_and_the_rest_still_loads()
    {
        var json = """
            { "id": "x", "kind": "Tool", "tool": "Annotate", "targetFormat": "*", "image": { "edit": {
                "redact": { "mode": "Pixelate", "boxes": [ { "x": 0.1, "y": 0.1, "width": 0.2, "height": 0.2 } ] },
                "annotations": [ { "kind": "Stamp", "width": "Huge", "points": [ { "x": 0.5, "y": 0.5 } ] } ],
                "background": { "kind": "Video" } } } }
            """;
        var edit = JsonSerializer.Deserialize(json, FileeJsonContext.Default.Preset)!.Image.Edit!;
        Assert.Equal(RedactMode.Black, edit.Redact!.Mode);
        Assert.Single(edit.Redact.Boxes);
        Assert.Equal((AnnotationKind.Pen, StrokeLevel.Thin), (edit.Annotations![0].Kind, edit.Annotations[0].Width));
        Assert.Equal(BackgroundKind.Gradient, edit.Background!.Kind);
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
        Assert.Equal(["crop", "add-background", "redact", "annotate", "compress-image", "strip-metadata"], reloaded.FindPreset("tools")!.SubPresetIds);
        Assert.Equal(PresetKind.Tool, reloaded.FindPreset("crop")!.Kind);
        Assert.All(new[] { "add-background", "redact", "annotate" }, id => Assert.Equal(PresetKind.Tool, reloaded.FindPreset(id)!.Kind));
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
    public void An_image_tool_window_closes_after_Copy_unless_that_is_turned_off()
    {
        Assert.True(new ImageToolSettings().CloseAfterCopy);
        Assert.True(new ImageToolSettings { CloseAfterCopy = true }.Clone().CloseAfterCopy);
        // Settings saved before the switch existed keep its default.
        var old = JsonSerializer.Deserialize("""{"imageTools":{"cropAspect":"16:9"}}""", FileeJsonContext.Default.AppSettings)!;
        Assert.True(old.ImageTools.CloseAfterCopy);

        var json = JsonSerializer.Serialize(new AppSettings { ImageTools = new ImageToolSettings { CloseAfterCopy = false } },
            FileeJsonContext.Default.AppSettings);
        Assert.False(JsonSerializer.Deserialize(json, FileeJsonContext.Default.AppSettings)!.ImageTools.CloseAfterCopy);
    }

    [Fact]
    public void The_image_tool_settings_remember_the_last_background_redaction_and_pen()
    {
        var settings = new ImageToolSettings();
        Assert.Equal((RedactMode.Black, "#FF3B30", StrokeLevel.Normal, AnnotationKind.Pen),
            (settings.RedactMode, settings.PenColor, settings.PenWidth, settings.LastTool));
        Assert.Equal((BackgroundKind.Gradient, "peach", 0.08, 0.03, 40, "auto"),
            (settings.Background.Kind, settings.Background.Value, settings.Background.Padding, settings.Background.Corner,
                settings.Background.Shadow, settings.Background.Aspect));

        settings.Background.Padding = .3;
        var copy = settings.Clone();
        copy.Background.Padding = .1;
        Assert.Equal(.3, settings.Background.Padding); // a copy doesn't share the background

        settings.RedactMode = RedactMode.Mosaic;
        settings.PenColor = "#00AAFF";
        settings.PenWidth = StrokeLevel.Thick;
        settings.LastTool = AnnotationKind.Rectangle;
        settings.Background = new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#112233", Padding = .2, Aspect = "1:1" };
        var json = JsonSerializer.Serialize(new AppSettings { ImageTools = settings }, FileeJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, FileeJsonContext.Default.AppSettings)!.ImageTools;
        Assert.Equal((RedactMode.Mosaic, "#00AAFF", StrokeLevel.Thick, AnnotationKind.Rectangle),
            (back.RedactMode, back.PenColor, back.PenWidth, back.LastTool));
        Assert.Equal((BackgroundKind.Solid, "#112233", .2, "1:1"), (back.Background.Kind, back.Background.Value, back.Background.Padding, back.Background.Aspect));
    }

    [Fact]
    public void A_picture_chosen_as_the_background_is_not_remembered_the_last_gradient_or_colour_is()
    {
        var settings = new ImageToolSettings { Background = new BackgroundEdit { Kind = BackgroundKind.Solid, Value = "#112233" } };

        // The user picks a picture from the disk this time: its path must not be kept (it may be deleted by the next time).
        settings.Background = new BackgroundEdit
        {
            Kind = BackgroundKind.Image,
            Value = "peach",
            ImagePath = @"C:\pictures\wall.jpg",
            Padding = .25,
            Shadow = 10,
        };

        Assert.Null(settings.Background.ImagePath);
        Assert.Equal((BackgroundKind.Solid, "#112233"), (settings.Background.Kind, settings.Background.Value));
        Assert.Equal((.25, 10), (settings.Background.Padding, settings.Background.Shadow)); // the rest of the choices stay
        var json = JsonSerializer.Serialize(new AppSettings { ImageTools = settings }, FileeJsonContext.Default.AppSettings);
        Assert.DoesNotContain("wall.jpg", json);

        // Nor does a hand-edited settings.json bring one in.
        var edited = json.Replace("\"kind\": \"Solid\"", "\"kind\": \"Image\", \"imagePath\": \"gone.jpg\"");
        var back = JsonSerializer.Deserialize(edited, FileeJsonContext.Default.AppSettings)!.ImageTools.Background;
        Assert.Equal((BackgroundKind.Gradient, (string?)null), (back.Kind, back.ImagePath));
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
