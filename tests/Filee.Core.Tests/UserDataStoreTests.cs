using System.Text.Json.Nodes;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class UserDataStoreTests
{
    [Fact]
    public void Groups_keep_only_conversions_once_and_at_most_six()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Presets.Add(new Preset { Id = "other", Name = "Other", Kind = PresetKind.Group, SubPresetIds = ["to-pdf"] });
        store.Presets.Add(new Preset
        {
            Id = "mine",
            Name = "Mine",
            Kind = PresetKind.Group,
            SubPresetIds = ["to-png", "gone", "to-png", "other", "to-jpg", "to-webp", "to-bmp", "to-gif", "to-ico", "to-tiff"],
        });
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        // A deleted preset and a group in the group are dropped, a repeat counts once, the rest is cut at six.
        var group = reloaded.FindPreset("mine")!;
        Assert.True(group.IsGroup);
        Assert.Equal(["to-png", "to-jpg", "to-webp", "to-bmp", "to-gif", "to-ico"], group.SubPresetIds);
        // No target format a Filee from before groups (which reads it as a conversion) could run it with.
        Assert.Equal(Preset.GroupTarget, group.TargetFormat);
        Assert.Null(Filee.Core.Formats.FormatRegistry.FindById(Preset.GroupTarget));
    }

    [Fact]
    public void Only_a_conversion_is_found_to_run()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Presets.Add(new Preset { Id = "web", Name = "Web", Kind = PresetKind.Group, SubPresetIds = ["to-png"] });

        Assert.NotNull(store.FindPreset("web")); // to edit it
        Assert.Null(store.FindConversion("web")); // never to run it (a watch folder pointing at it converts nothing)
        Assert.Equal("to-png", store.FindConversion("to-png")!.Id);
    }

    [Fact]
    public void A_kind_from_a_newer_Filee_reads_as_a_group_and_the_other_presets_still_load()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Presets.Add(new Preset { Id = "future", Name = "Future", Kind = PresetKind.Group, SubPresetIds = ["to-png"] });
        store.SaveLibrary();
        var path = Path.Combine(dir.Path, "presets.json");
        var json = File.ReadAllText(path);
        Assert.Contains("\"kind\": \"Group\"", json);
        File.WriteAllText(path, json.Replace("\"kind\": \"Group\"", "\"kind\": \"Chain\""));

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        Assert.True(reloaded.FindPreset("future")!.IsGroup); // never run as a conversion
        Assert.NotNull(reloaded.FindPreset("to-png")); // and not every preset reset to the defaults
        Assert.False(File.Exists(path + ".broken"));
    }

    [Fact]
    public void A_group_saved_by_a_Filee_from_before_groups_is_still_a_group()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Presets.Add(new Preset { Id = "old-group", Name = "Rotate", Kind = PresetKind.Group, SubPresetIds = ["to-png"] });
        store.SaveLibrary();

        // An older Filee knows neither "kind" nor "subPresetIds" and leaves them out when it saves.
        var path = Path.Combine(dir.Path, "presets.json");
        var presets = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var saved = presets.Single(p => (string?)p!["id"] == "old-group")!.AsObject();
        Assert.True(saved.Remove("kind"));
        Assert.True(saved.Remove("subPresetIds"));
        File.WriteAllText(path, presets.ToJsonString());

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        var group = reloaded.FindPreset("old-group")!;
        Assert.True(group.IsGroup); // not a conversion to "group" in the preset list, watch folders and the CLI
        Assert.Empty(group.SubPresetIds);
        Assert.Null(reloaded.FindConversion("old-group"));
    }

    [Fact]
    public void Round_trips_settings_presets_and_profiles()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Settings.Language = "ko";
        store.Settings.Theme.Accent = "#1D9E75";
        store.Settings.Triggers[0].Modifiers = ModifierKeys.Alt | ModifierKeys.Shift;
        store.Presets.Add(new Preset { Id = "mine", Name = "Mine", TargetFormat = "webp", Image = { Quality = 42 } });
        store.Profiles[0].PresetIds.Insert(0, "mine");
        store.SaveSettings();
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        Assert.Equal("ko", reloaded.Settings.Language);
        Assert.Equal("#1D9E75", reloaded.Settings.Theme.Accent);
        Assert.Equal(ModifierKeys.Alt | ModifierKeys.Shift, reloaded.Settings.Triggers[0].Modifiers);
        Assert.Equal(42, reloaded.FindPreset("mine")!.Image.Quality);
        Assert.Equal("mine", reloaded.Profiles[0].PresetIds[0]);
    }

    [Fact]
    public void Hardware_encoding_is_saved_and_stays_off_for_presets_from_before_it()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        Assert.All(store.Presets, p => Assert.False(p.Media.HardwareEncoding)); // no built-in preset turns it on
        store.Presets.Add(new Preset { Id = "fast", Name = "Fast", TargetFormat = "mp4", Media = { HardwareEncoding = true, VideoCodec = VideoCodec.Hevc } });
        store.Presets.Add(new Preset { Id = "old", Name = "Old", TargetFormat = "mp4" });
        store.SaveLibrary();

        // A Filee from before the option saved no "hardwareEncoding".
        var path = Path.Combine(dir.Path, "presets.json");
        var presets = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Assert.True((bool)presets.Single(p => (string?)p!["id"] == "fast")!["media"]!["hardwareEncoding"]!);
        Assert.True(presets.Single(p => (string?)p!["id"] == "old")!["media"]!.AsObject().Remove("hardwareEncoding"));
        File.WriteAllText(path, presets.ToJsonString());

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        var fast = reloaded.FindPreset("fast")!;
        Assert.True(fast.Media.HardwareEncoding);
        Assert.Equal(VideoCodec.Hevc, fast.Media.VideoCodec);
        Assert.True(fast.Clone().Media.HardwareEncoding);
        Assert.False(reloaded.FindPreset("old")!.Media.HardwareEncoding);
    }

    [Fact]
    public void Animation_speed_is_saved_and_is_the_original_speed_for_presets_from_before_it()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        Assert.All(store.Presets, p => Assert.Equal(1, p.Image.AnimationSpeed)); // no built-in preset changes it
        store.Presets.Add(new Preset { Id = "slow", Name = "Slow", TargetFormat = "gif", Image = { AnimationSpeed = 0.5 } });
        store.Presets.Add(new Preset { Id = "old", Name = "Old", TargetFormat = "gif" });
        store.Presets.Add(new Preset { Id = "odd", Name = "Odd", TargetFormat = "gif" });
        store.SaveLibrary();

        // A Filee from before the option saved no "animationSpeed"; a hand-edited file may hold anything.
        var path = Path.Combine(dir.Path, "presets.json");
        var presets = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Assert.Equal(0.5, (double)presets.Single(p => (string?)p!["id"] == "slow")!["image"]!["animationSpeed"]!);
        Assert.True(presets.Single(p => (string?)p!["id"] == "old")!["image"]!.AsObject().Remove("animationSpeed"));
        presets.Single(p => (string?)p!["id"] == "odd")!["image"]!["animationSpeed"] = -3;
        File.WriteAllText(path, presets.ToJsonString());

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        var slow = reloaded.FindPreset("slow")!;
        Assert.Equal(0.5, slow.Image.AnimationSpeed);
        Assert.Equal(0.5, slow.Clone().Image.AnimationSpeed);
        Assert.True(slow.Image.ChangesSpeed);
        Assert.Equal(1, reloaded.FindPreset("old")!.Image.AnimationSpeed);
        Assert.False(reloaded.FindPreset("old")!.Image.ChangesSpeed);
        Assert.Equal(1, reloaded.FindPreset("odd")!.Image.PlaybackSpeed); // a speed that makes no sense plays as made
    }

    [Fact]
    public void History_is_kept_until_turned_off_and_can_be_cleared()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();

        store.AddHistory(new Filee.Core.History.HistoryEntry { JobId = "a" });
        Assert.Single(store.History);

        store.Settings.KeepHistory = false;
        store.AddHistory(new Filee.Core.History.HistoryEntry { JobId = "b" });
        Assert.Single(store.History); // not recorded

        store.ClearHistory();
        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Empty(reloaded.History);
    }

    [Fact]
    public void Version_1_settings_are_upgraded_to_the_current_engine_list()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"),
            """{ "schemaVersion": 1, "enginePriority": ["magick", "word", "hancom", "libreoffice"] }""");

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Equal(["magick", "markdown", "spreadsheet", "ooxml", "hwpx-writer", "docx-writer", "pdf-text", "pdf-slides", "email", "data", "vector", "icns", "font", "cad", "model3d", "archive", "ebook", "subtitle", "wic", "ffmpeg", "pandoc", "ghostscript", "calibre", "libreoffice"], store.Settings.EnginePriority);
        Assert.Equal(AppSettings.CurrentSchemaVersion, store.Settings.SchemaVersion);
    }

    [Fact]
    public void Version_3_libraries_get_the_text_profile_once()
    {
        using var dir = new TempDir();
        var old = new UserDataStore(dir.Path);
        old.Load();
        old.Profiles.RemoveAll(p => p.Id == "text"); // as written by 1.0.0
        old.Settings.SchemaVersion = 3;
        old.Settings.EnginePriority.Remove("markdown");
        old.SaveSettings();
        old.SaveLibrary();

        var store = new UserDataStore(dir.Path);
        store.Load();

        var ids = store.Profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("mixed") - 1, ids.IndexOf("text")); // right before the fallback
        Assert.Contains("md", store.Profiles.Single(p => p.Id == "text").Extensions);
        Assert.Equal(store.Settings.EnginePriority.IndexOf("hwpx-writer") - 1, store.Settings.EnginePriority.IndexOf("markdown"));

        // Saved right away: the next start neither migrates again nor brings back a profile the user deleted.
        store.Profiles.RemoveAll(p => p.Id == "text");
        store.SaveLibrary();
        var next = new UserDataStore(dir.Path);
        next.Load();
        Assert.DoesNotContain(next.Profiles, p => p.Id == "text");
        Assert.Equal(AppSettings.CurrentSchemaVersion, next.Settings.SchemaVersion);
    }

    [Fact]
    public void Version_4_libraries_get_spreadsheet_and_presentation_profiles()
    {
        using var dir = new TempDir();
        var old = new UserDataStore(dir.Path);
        old.Load();
        // As written by 1.0.1: one documents donut for Word, Excel and PowerPoint files, Word-only PDF targets.
        old.Profiles.RemoveAll(p => p.Id is "spreadsheets" or "presentations");
        old.Profiles.Single(p => p.Id == "office").Extensions.AddRange(["xlsx", "xls", "ods", "csv", "pptx", "ppt", "odp"]);
        old.Profiles.Single(p => p.Id == "pdf").PresetIds.AddRange(["to-docx", "to-hwpx"]);
        old.Presets.RemoveAll(p => p.Id is "to-xlsx" or "to-csv");
        old.Settings.SchemaVersion = 4;
        old.Settings.EnginePriority = ["magick", "word", "markdown", "hwpx-writer", "pandoc", "libreoffice"];
        old.SaveSettings();
        old.SaveLibrary();

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Equal(["magick", "markdown", "spreadsheet", "ooxml", "hwpx-writer", "docx-writer", "pdf-text", "pdf-slides", "email", "data", "vector", "icns", "font", "cad", "model3d", "archive", "ebook", "subtitle", "wic", "ffmpeg", "pandoc", "ghostscript", "calibre", "libreoffice"], store.Settings.EnginePriority);
        var ids = store.Profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("office") + 1, ids.IndexOf("spreadsheets"));
        Assert.Equal(ids.IndexOf("office") + 2, ids.IndexOf("presentations"));
        Assert.DoesNotContain("xlsx", store.Profiles.Single(p => p.Id == "office").Extensions);
        Assert.Contains("to-csv", store.Profiles.Single(p => p.Id == "spreadsheets").PresetIds);
        Assert.NotNull(store.FindPreset("to-xlsx"));
        Assert.DoesNotContain("to-hwpx", store.Profiles.Single(p => p.Id == "pdf").PresetIds); // Word-only target removed by v5
    }

    [Fact]
    public void Version_5_libraries_get_the_format_catalog()
    {
        // As written by 1.0.x with schema 5: no catalog presets, donuts or extensions.
        var presets = BuiltInData.CreatePresets()
            .Where(p => BuiltInData.CatalogPresets().Concat(BuiltInData.CodecAndSubtitlePresets()).Concat(BuiltInData.ModelPresets()).All(c => c.Id != p.Id)).ToList();
        var catalogIds = BuiltInData.CatalogProfiles().Select(p => p.Id).Append("subtitles").Append("models").ToHashSet();
        var profiles = BuiltInData.CreateProfiles().Where(p => !catalogIds.Contains(p.Id)).ToList();
        profiles.Single(p => p.Id == "images").Extensions = ["png", "jpg"];
        profiles.Single(p => p.Id == "pdf").PresetIds.Remove("to-docx");
        profiles.Single(p => p.Id == "mixed").PresetIds.Remove("zip-all");
        // A donut of the user already handles MP3.
        profiles.Insert(0, new ToolbarProfile { Id = "mine", Name = "Mine", Extensions = ["mp3"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(5, presets, profiles));

        var ids = profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("text") + 1, ids.IndexOf("ebooks"));
        Assert.Equal(ids.IndexOf("fonts") + 1, ids.IndexOf("data")); // schema 14
        Assert.Equal(ids.IndexOf("data") + 1, ids.IndexOf("models")); // schema 15
        Assert.Equal(ids.IndexOf("folders") - 1, ids.IndexOf("models")); // the last category donut; the folders one follows
        Assert.Equal(ids.IndexOf("audio") + 1, ids.IndexOf("subtitles"));
        Assert.Contains("cr2", profiles.Single(p => p.Id == "images").Extensions);
        Assert.DoesNotContain("mp3", profiles.Single(p => p.Id == "audio").Extensions);
        Assert.Contains("flac", profiles.Single(p => p.Id == "audio").Extensions);
        Assert.Contains(presets, p => p.Id == "to-mp4");
        Assert.Equal(["to-png", "to-jpg", "to-docx"], profiles.Single(p => p.Id == "pdf").PresetIds.Take(3));
        Assert.Contains("zip-all", profiles.Single(p => p.Id == "mixed").PresetIds);
        // Every extension belongs to one donut only.
        var all = profiles.SelectMany(p => p.Extensions).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Version_6_libraries_get_the_conversions_that_were_on_no_donut()
    {
        // As written by 1.3.0: no HWP preset, and the donuts below without the targets 1.3.1 added.
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "to-hwp");
        var profiles = BuiltInData.CreateProfiles();
        foreach (var profile in profiles)
            profile.PresetIds.Remove("to-hwp");
        profiles.Single(p => p.Id == "pdf").PresetIds.RemoveAll(id => id is "to-md" or "to-epub");
        profiles.Single(p => p.Id == "office").PresetIds.Remove("to-png");
        profiles.Single(p => p.Id == "hwp").PresetIds.RemoveAll(id => id is "to-jpg" or "to-epub");

        Assert.True(SettingsMigrations.ApplyToLibrary(6, presets, profiles));

        // The same donuts as a new install.
        Assert.NotNull(presets.SingleOrDefault(p => p.Id == "to-hwp"));
        foreach (var expected in BuiltInData.CreateProfiles())
            Assert.Equal(expected.PresetIds, profiles.Single(p => p.Id == expected.Id).PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(6, presets, profiles)); // once only
    }

    [Fact]
    public void Version_8_libraries_get_the_long_image_preset_after_jpg_and_a_heic_preset()
    {
        // As written by 1.9.0: no "Long image" or HEIC preset, and the PDF donut without the long image.
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id is "pdf-long-image" or "to-heic");
        var profiles = BuiltInData.CreateProfiles();
        var pdf = profiles.Single(p => p.Id == "pdf");
        pdf.PresetIds.Remove("pdf-long-image");

        Assert.True(SettingsMigrations.ApplyToLibrary(8, presets, profiles));

        var longImage = presets.Single(p => p.Id == "pdf-long-image");
        Assert.True(longImage.Pdf.CombinePages);
        Assert.Equal(pdf.PresetIds.IndexOf("to-jpg") + 1, pdf.PresetIds.IndexOf("pdf-long-image"));
        Assert.Equal("heic", presets.Single(p => p.Id == "to-heic").TargetFormat);
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("to-heic"));
        Assert.False(SettingsMigrations.ApplyToLibrary(8, presets, profiles)); // once only
    }

    [Fact]
    public void Version_9_libraries_get_the_remove_background_preset_on_no_donut()
    {
        // As written by 1.10.x: no "Remove background" preset.
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "remove-background");
        var profiles = BuiltInData.CreateProfiles();

        Assert.True(SettingsMigrations.ApplyToLibrary(9, presets, profiles));

        var preset = presets.Single(p => p.Id == "remove-background");
        Assert.True(preset.Image.RemoveBackground);
        Assert.Equal("png", preset.TargetFormat);
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("remove-background"));
        Assert.False(SettingsMigrations.ApplyToLibrary(9, presets, profiles)); // once only
    }

    [Fact]
    public void Version_18_libraries_get_the_extract_frames_presets_after_the_pdf_presets_on_no_donut()
    {
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id is "extract-frames-png" or "extract-frames-jpg");
        var profiles = BuiltInData.CreateProfiles();

        Assert.True(SettingsMigrations.ApplyToLibrary(18, presets, profiles));

        var pngPreset = presets.Single(p => p.Id == "extract-frames-png");
        Assert.True(pngPreset.Media.SaveEveryFrame);
        Assert.Equal("png", pngPreset.TargetFormat);

        var jpgPreset = presets.Single(p => p.Id == "extract-frames-jpg");
        Assert.True(jpgPreset.Media.SaveEveryFrame);
        Assert.Equal("jpg", jpgPreset.TargetFormat);

        // Where a new library has them.
        Assert.Equal(BuiltInData.CreatePresets().Select(p => p.Id), presets.Select(p => p.Id));
        var slides = presets.FindIndex(p => p.Id == "pdf-slides");
        Assert.Equal(["extract-frames-png", "extract-frames-jpg"], presets.Skip(slides + 1).Take(2).Select(p => p.Id));

        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("extract-frames-png"));
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("extract-frames-jpg"));
        Assert.False(SettingsMigrations.ApplyToLibrary(18, presets, profiles)); // once only
    }

    [Fact]
    public void Version_13_libraries_get_the_data_donut_msg_files_and_json_for_spreadsheets()
    {
        // As written with schema 13: no data presets or donut, no MSG on the documents donut, no JSON on the spreadsheets
        // donut; and a donut of the user that already takes YAML.
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => BuiltInData.DataPresets().Any(d => d.Id == p.Id));
        var profiles = BuiltInData.CreateProfiles();
        profiles.RemoveAll(p => p.Id == "data");
        profiles.Single(p => p.Id == "office").Extensions.Remove("msg");
        profiles.Single(p => p.Id == "spreadsheets").PresetIds.Remove("to-json");
        profiles.Insert(0, new ToolbarProfile { Id = "mine", Name = "Mine", Extensions = ["yml"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(13, presets, profiles));

        Assert.All(BuiltInData.DataPresets(), d => Assert.Contains(presets, p => p.Id == d.Id));
        var ids = profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("fonts") + 1, ids.IndexOf("data"));
        var data = profiles.Single(p => p.Id == "data");
        Assert.Contains("json", data.Extensions);
        Assert.Contains("yaml", data.Extensions);
        Assert.DoesNotContain("yml", data.Extensions); // the user's donut keeps it
        Assert.Equal(["to-json", "to-yaml", "to-toml", "to-xml", "to-csv", "to-xlsx"], data.PresetIds);
        Assert.Contains("msg", profiles.Single(p => p.Id == "office").Extensions);
        var sheets = profiles.Single(p => p.Id == "spreadsheets").PresetIds;
        Assert.Equal(sheets.IndexOf("to-csv") + 1, sheets.IndexOf("to-json"));
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("to-eml") || p.PresetIds.Contains("to-vcf"));
        Assert.False(SettingsMigrations.ApplyToLibrary(13, presets, profiles)); // once only

        var all = profiles.SelectMany(p => p.Extensions).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Version_13_settings_get_the_data_engine_after_email()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"),
            """{ "schemaVersion": 13, "enginePriority": ["magick", "email", "vector", "pandoc", "libreoffice"] }""");

        var store = new UserDataStore(dir.Path);
        store.Load();

        // ... and, from schema 15, the 3D model engine before Pandoc (no CAD or archive engine in this list), from
        // schema 18 the PDF slides engine there too (no PDF text engine), from schema 21 the WIC engine (no FFmpeg).
        Assert.Equal(["magick", "email", "data", "vector", "model3d", "pdf-slides", "wic", "pandoc", "libreoffice"], store.Settings.EnginePriority);
    }

    [Fact]
    public void Version_12_libraries_open_the_new_image_formats_on_the_images_donut()
    {
        // As written with schema 12: the images donut knows no APNG, QOI, DICOM or .hif; a donut of the user takes DDS.
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        var images = profiles.Single(p => p.Id == "images");
        images.Extensions.RemoveAll(e => e is "apng" or "qoi" or "dcm" or "hif" or "dds");
        profiles.Insert(0, new ToolbarProfile { Id = "game", Name = "Game textures", Extensions = ["dds"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(12, presets, profiles));

        Assert.Contains("apng", images.Extensions);
        Assert.Contains("qoi", images.Extensions);
        Assert.Contains("dcm", images.Extensions);
        Assert.Contains("hif", images.Extensions);
        Assert.DoesNotContain("dds", images.Extensions);
        Assert.False(SettingsMigrations.ApplyToLibrary(12, presets, profiles)); // once only
    }

    [Fact]
    public void Version_11_libraries_get_codecs_new_audio_formats_and_a_subtitles_donut()
    {
        // As written with schema 11: no HEVC / AV1 / ALAC / subtitle presets, no subtitles donut, the video donut and
        // the audio extensions of that time, and a donut of the user that already takes .ape.
        var added = BuiltInData.CodecAndSubtitlePresets().Select(p => p.Id).ToHashSet();
        var presets = BuiltInData.CreatePresets().Where(p => !added.Contains(p.Id)).ToList();
        var profiles = BuiltInData.CreateProfiles().Where(p => p.Id != "subtitles").ToList();
        foreach (var profile in profiles)
            profile.PresetIds.RemoveAll(added.Contains);
        var audio = profiles.Single(p => p.Id == "audio");
        audio.Extensions.RemoveAll(e => e is "wv" or "tta" or "w64" or "eac3" or "ec3" or "ape" or "mpc" or "mpp" or "mp+" or "tak"
            or "dsf" or "dff" or "dts" or "spx");
        profiles.Insert(0, new ToolbarProfile { Id = "mine", Name = "Mine", Extensions = ["ape"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(11, presets, profiles));

        Assert.Contains("wv", audio.Extensions);
        Assert.Contains("dsf", audio.Extensions);
        Assert.DoesNotContain("ape", audio.Extensions); // the user's donut keeps it
        var ids = profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("audio") + 1, ids.IndexOf("subtitles"));
        Assert.Equal(BuiltInData.SubtitleProfile().PresetIds, profiles.Single(p => p.Id == "subtitles").PresetIds);
        Assert.Contains("smi", profiles.Single(p => p.Id == "subtitles").Extensions);
        Assert.Equal(BuiltInData.CreateProfiles().Single(p => p.Id == "video").PresetIds, profiles.Single(p => p.Id == "video").PresetIds);
        Assert.Equal(VideoCodec.Hevc, presets.Single(p => p.Id == "mp4-hevc").Media.VideoCodec);
        Assert.Equal(AudioCodec.Alac, presets.Single(p => p.Id == "to-alac").Media.AudioCodec);
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("to-alac") || p.PresetIds.Contains("mp4-av1"));
        var all = profiles.SelectMany(p => p.Extensions).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(SettingsMigrations.ApplyToLibrary(11, presets, profiles)); // once only
    }

    [Fact]
    public void Version_10_libraries_get_kepub_and_the_new_document_extensions()
    {
        // As written by 1.11.x: no KEPUB preset, and the donuts without the formats added in schema 11.
        string[] added = ["org", "adoc", "typ", "mediawiki", "wiki", "dbk", "ipynb", "fodt", "ott", "cwk", "602", "ots", "fods", "otp", "fodp", "fodg", "kepub.epub"];
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "to-kepub");
        var profiles = BuiltInData.CreateProfiles();
        foreach (var profile in profiles)
        {
            profile.PresetIds.Remove("to-kepub");
            profile.Extensions.RemoveAll(added.Contains);
        }
        // A donut of the user already handles Org files.
        profiles.Insert(0, new ToolbarProfile { Id = "mine", Name = "Mine", Extensions = ["org"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(10, presets, profiles));

        Assert.Equal("kepub", presets.Single(p => p.Id == "to-kepub").TargetFormat);
        var ebooks = profiles.Single(p => p.Id == "ebooks");
        Assert.Equal(ebooks.PresetIds.IndexOf("to-mobi") + 1, ebooks.PresetIds.IndexOf("to-kepub"));
        Assert.Contains("kepub.epub", ebooks.Extensions);
        Assert.Contains("typ", profiles.Single(p => p.Id == "text").Extensions);
        Assert.DoesNotContain("org", profiles.Single(p => p.Id == "text").Extensions);
        Assert.Contains("fodt", profiles.Single(p => p.Id == "office").Extensions);
        Assert.Contains("fods", profiles.Single(p => p.Id == "spreadsheets").Extensions);
        Assert.Contains("otp", profiles.Single(p => p.Id == "presentations").Extensions);
        Assert.Contains("fodg", profiles.Single(p => p.Id == "vector").Extensions);
        foreach (var expected in BuiltInData.CreateProfiles())
            Assert.Equal(expected.PresetIds, profiles.Single(p => p.Id == expected.Id).PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(10, presets, profiles)); // once only
    }

    [Fact]
    public void Version_14_libraries_get_3d_model_presets_and_their_donut_after_data()
    {
        // As written with schema 14: no 3D formats.
        var modelIds = BuiltInData.ModelPresets().Select(p => p.Id).ToHashSet();
        var presets = BuiltInData.CreatePresets().Where(p => !modelIds.Contains(p.Id)).ToList();
        var profiles = BuiltInData.CreateProfiles().Where(p => p.Id != "models").ToList();

        Assert.True(SettingsMigrations.ApplyToLibrary(14, presets, profiles));

        Assert.Equal(["to-glb", "to-stl", "to-obj", "to-3mf", "to-ply", "to-gltf"], presets.Where(p => modelIds.Contains(p.Id)).Select(p => p.Id));
        var ids = profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("data") + 1, ids.IndexOf("models"));
        Assert.Equal(ids.IndexOf("folders") - 1, ids.IndexOf("models")); // the last category donut; the folders one follows
        var models = profiles.Single(p => p.Id == "models");
        Assert.Equal(["glb", "gltf", "stl", "obj", "3mf", "ply", "fbx"], models.Extensions);
        Assert.Equal(BuiltInData.ModelProfile().PresetIds, models.PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(14, presets, profiles)); // once only
    }

    [Fact]
    public void The_3d_donut_leaves_out_extensions_a_profile_of_the_user_takes()
    {
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles().Where(p => p.Id != "models").ToList();
        profiles.Insert(0, new ToolbarProfile { Id = "printing", Name = "Printing", Extensions = ["stl", "3mf"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(14, presets, profiles));

        Assert.Equal(["glb", "gltf", "obj", "ply", "fbx"], profiles.Single(p => p.Id == "models").Extensions);
    }

    [Fact]
    public void Version_21_libraries_learn_fbx_on_the_3d_donut_and_nothing_else()
    {
        // As written with schema 21: no FBX, and the user took PLY off the 3D donut.
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        var models = profiles.Single(p => p.Id == "models");
        models.Extensions.RemoveAll(e => e is "fbx" or "ply");

        Assert.True(SettingsMigrations.ApplyToLibrary(21, presets, profiles));

        Assert.Equal(["glb", "gltf", "stl", "obj", "3mf", "fbx"], models.Extensions);
        Assert.False(SettingsMigrations.ApplyToLibrary(21, presets, profiles)); // once only

        // A donut of the user that already opens FBX files keeps them.
        profiles = BuiltInData.CreateProfiles();
        profiles.Single(p => p.Id == "models").Extensions.Remove("fbx");
        profiles.Insert(0, new ToolbarProfile { Id = "games", Name = "Games", Extensions = ["fbx"] });
        Assert.False(SettingsMigrations.ApplyToLibrary(21, presets, profiles));
        Assert.DoesNotContain("fbx", profiles.Single(p => p.Id == "models").Extensions);
    }

    [Fact]
    public void Version_15_libraries_get_the_tracing_presets_on_the_images_donut_after_pdf()
    {
        // As written before tracing: no trace presets, and the images donut without them.
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id is "trace-svg" or "trace-line-art");
        var profiles = BuiltInData.CreateProfiles();
        var images = profiles.Single(p => p.Id == "images");
        images.PresetIds.RemoveAll(id => id is "trace-svg" or "trace-line-art");

        Assert.True(SettingsMigrations.ApplyToLibrary(15, presets, profiles));

        Assert.Equal(TracingMode.Color, presets.Single(p => p.Id == "trace-svg").Trace.Mode);
        Assert.Equal(TracingMode.BlackAndWhite, presets.Single(p => p.Id == "trace-line-art").Trace.Mode);
        Assert.All(presets.Where(p => p.Id.StartsWith("trace-", StringComparison.Ordinal)), p => Assert.Equal("svg", p.TargetFormat));
        Assert.Equal(BuiltInData.CreateProfiles().Single(p => p.Id == "images").PresetIds, images.PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(15, presets, profiles)); // once only
    }

    [Fact]
    public void Trace_options_survive_a_save_and_load()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        var preset = store.Presets.Single(p => p.Id == "trace-svg");
        preset.Trace.Colors = 6;
        preset.Trace.Detail = 80;
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        var trace = reloaded.Presets.Single(p => p.Id == "trace-svg").Trace;
        Assert.Equal((TracingMode.Color, 6, 80), (trace.Mode, trace.Colors, trace.Detail));
        Assert.Equal(TracingMode.BlackAndWhite, reloaded.Presets.Single(p => p.Id == "trace-line-art").Trace.Mode);
    }

    [Fact]
    public void Version_16_libraries_get_rotate_and_flip_presets_and_groups_on_no_donut()
    {
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id is "rotate-left" or "rotate-right" or "rotate-180" or "flip-horizontal" or "flip-vertical" or "rotate" or "flip");
        var profiles = BuiltInData.CreateProfiles();

        Assert.True(SettingsMigrations.ApplyToLibrary(16, presets, profiles));

        var rotate = presets.Single(p => p.Id == "rotate");
        Assert.True(rotate.IsGroup);
        Assert.Equal(["rotate-left", "rotate-right", "rotate-180"], rotate.SubPresetIds);
        Assert.Equal(ImageRotation.Left90, presets.Single(p => p.Id == "rotate-left").Image.Rotation);
        Assert.Equal(ImageFlip.Horizontal, presets.Single(p => p.Id == "flip-horizontal").Image.Flip);
        Assert.DoesNotContain(profiles, p => p.PresetIds.Any(id => id.StartsWith("rotate") || id.StartsWith("flip")));
        Assert.False(SettingsMigrations.ApplyToLibrary(16, presets, profiles)); // once only
        // Where a new library has them: after "Remove background".
        var background = presets.FindIndex(p => p.Id == "remove-background");
        Assert.Equal("rotate-left", presets[background + 1].Id);
    }

    /// <summary>Presets and donuts as a Filee before PDF → PPTX wrote them (schema 17).</summary>
    private static (List<Preset> Presets, List<ToolbarProfile> Profiles) Version17Library()
    {
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "pdf-slides");
        var profiles = BuiltInData.CreateProfiles();
        profiles.Single(p => p.Id == "pdf").PresetIds = [.. BuiltInData.PdfDonutBefore18];
        return (presets, profiles);
    }

    [Fact]
    public void Version_17_libraries_get_pptx_slides_on_the_unchanged_pdf_donut_in_place_of_tiff()
    {
        var (presets, profiles) = Version17Library();

        Assert.True(SettingsMigrations.ApplyToLibrary(17, presets, profiles));

        var slides = presets.Single(p => p.Id == "pdf-slides");
        Assert.Equal(("pptx", "preset.pdf_slides"), (slides.TargetFormat, slides.NameKey));
        Assert.Equal("pdf-split", presets[presets.IndexOf(slides) - 1].Id); // where a new library has it
        var pdf = profiles.Single(p => p.Id == "pdf").PresetIds;
        Assert.Equal(BuiltInData.CreateProfiles().Single(p => p.Id == "pdf").PresetIds, pdf);
        Assert.Equal(pdf.IndexOf("to-docx") + 1, pdf.IndexOf("pdf-slides"));
        Assert.DoesNotContain("to-tiff", pdf);
        Assert.NotNull(presets.SingleOrDefault(p => p.Id == "to-tiff")); // still a preset to add again
        Assert.False(SettingsMigrations.ApplyToLibrary(17, presets, profiles)); // once only
    }

    [Fact]
    public void A_pdf_donut_the_user_changed_gets_pptx_slides_only_when_it_has_room()
    {
        // Full, but not the default: the user's choice of twelve stays.
        var (presets, profiles) = Version17Library();
        var pdf = profiles.Single(p => p.Id == "pdf");
        pdf.PresetIds[pdf.PresetIds.IndexOf("to-tiff")] = "to-webp";

        Assert.True(SettingsMigrations.ApplyToLibrary(17, presets, profiles)); // the preset is added
        Assert.DoesNotContain("pdf-slides", pdf.PresetIds);
        Assert.Contains("to-webp", pdf.PresetIds);

        // With room: after DOCX.
        (presets, profiles) = Version17Library();
        pdf = profiles.Single(p => p.Id == "pdf");
        pdf.PresetIds.RemoveAll(id => id is "to-md" or "to-epub");

        Assert.True(SettingsMigrations.ApplyToLibrary(17, presets, profiles));
        Assert.Equal(pdf.PresetIds.IndexOf("to-docx") + 1, pdf.PresetIds.IndexOf("pdf-slides"));
        Assert.Contains("to-tiff", pdf.PresetIds);
    }

    [Fact]
    public void Version_17_settings_get_the_pdf_slides_engine_after_pdf_text()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"),
            """{ "schemaVersion": 17, "enginePriority": ["magick", "pdfium", "docx-writer", "pdf-text", "email", "pandoc", "libreoffice"] }""");

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Equal(["magick", "pdfium", "docx-writer", "pdf-text", "pdf-slides", "email", "wic", "pandoc", "libreoffice"], store.Settings.EnginePriority);
        Assert.Equal(AppSettings.CurrentSchemaVersion, store.Settings.SchemaVersion);
    }

    [Fact]
    public void Version_20_settings_get_the_wic_engine_after_the_other_built_in_engines()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"),
            """{ "schemaVersion": 20, "enginePriority": ["magick", "pdfium", "hwpx-writer", "subtitle", "ffmpeg", "pandoc", "libreoffice"] }""");

        var store = new UserDataStore(dir.Path);
        store.Load();

        // Not near the top: the HWPX writer stays among the ranks that win ties (see EbookTests).
        Assert.Equal(["magick", "pdfium", "hwpx-writer", "subtitle", "wic", "ffmpeg", "pandoc", "libreoffice"], store.Settings.EnginePriority);
        Assert.Equal(AppSettings.CurrentSchemaVersion, store.Settings.SchemaVersion);
    }

    [Fact]
    public void Version_20_libraries_open_jpeg_xr_on_the_images_donut()
    {
        // As written with schema 20: no JPEG XR yet; the user took PSD off the images donut and has a donut for .wdp.
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        var images = profiles.Single(p => p.Id == "images");
        images.Extensions.RemoveAll(e => e is "jxr" or "wdp" or "hdp" or "psd");
        profiles.Insert(0, new ToolbarProfile { Id = "photos", Name = "Windows Photo Gallery", Extensions = ["wdp"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(20, presets, profiles));

        Assert.Contains("jxr", images.Extensions);
        Assert.Contains("hdp", images.Extensions);
        Assert.DoesNotContain("wdp", images.Extensions);
        Assert.DoesNotContain("psd", images.Extensions); // only the new extensions are learnt
        Assert.False(SettingsMigrations.ApplyToLibrary(20, presets, profiles)); // once only
    }

    [Fact]
    public void The_close_action_is_saved_and_one_from_a_newer_Filee_reads_as_ask()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        Assert.Equal(CloseAction.Ask, store.Settings.CloseAction);
        store.Settings.CloseAction = CloseAction.Quit;
        store.SaveSettings();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Equal(CloseAction.Quit, reloaded.Settings.CloseAction);

        var path = Path.Combine(dir.Path, "settings.json");
        var json = File.ReadAllText(path);
        Assert.Contains("\"closeAction\": \"Quit\"", json);
        File.WriteAllText(path, json.Replace("\"closeAction\": \"Quit\"", "\"closeAction\": \"Minimize\""));
        var newer = new UserDataStore(dir.Path);
        newer.Load();
        Assert.Equal(CloseAction.Ask, newer.Settings.CloseAction);
        Assert.False(File.Exists(path + ".broken"));
    }

    [Fact]
    public void New_installs_start_with_peach_glass() => Assert.Equal(ThemeStyle.PeachGlass, new AppSettings().Theme.Style);

    [Fact]
    public void The_theme_style_is_saved_and_one_from_a_newer_Filee_reads_as_classic()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Settings.Theme.Style = ThemeStyle.Classic;
        store.SaveSettings();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Equal(ThemeStyle.Classic, reloaded.Settings.Theme.Style);

        // A style this Filee doesn't know falls back to the plain look, not to the default and not to a reset settings file.
        var path = Path.Combine(dir.Path, "settings.json");
        var json = File.ReadAllText(path);
        Assert.Contains("\"style\": \"Classic\"", json);
        File.WriteAllText(path, json.Replace("\"style\": \"Classic\"", "\"style\": \"Aurora\""));
        var newer = new UserDataStore(dir.Path);
        newer.Load();
        Assert.Equal(ThemeStyle.Classic, newer.Settings.Theme.Style);
        Assert.False(File.Exists(path + ".broken"));
    }

    [Fact]
    public void A_rotation_from_a_newer_Filee_reads_as_none_and_the_other_presets_still_load()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.SaveLibrary();
        var path = Path.Combine(dir.Path, "presets.json");
        var json = File.ReadAllText(path);
        Assert.Contains("\"rotation\": \"Left90\"", json);
        File.WriteAllText(path, json.Replace("\"rotation\": \"Left90\"", "\"rotation\": \"Transpose\"")
            .Replace("\"flip\": \"Vertical\"", "\"flip\": 7"));

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();

        Assert.Equal(ImageRotation.None, reloaded.FindPreset("rotate-left")!.Image.Rotation);
        Assert.Equal(ImageFlip.None, reloaded.FindPreset("flip-vertical")!.Image.Flip);
        Assert.Equal(ImageRotation.Right90, reloaded.FindPreset("rotate-right")!.Image.Rotation); // the rest as saved
        Assert.False(File.Exists(path + ".broken"));
    }

    [Fact]
    public void Back_from_an_older_Filee_rotate_and_flip_work_again_and_keep_the_users_changes()
    {
        // What a Filee before 17 leaves after saving: kind, members and rotation dropped, the rest as it was.
        var presets = BuiltInData.CreatePresets();
        var left = presets.Single(p => p.Id == "rotate-left");
        left.Image.Rotation = ImageRotation.None;
        left.Name = "Turn left";
        var rotate = presets.Single(p => p.Id == "rotate");
        rotate.Kind = PresetKind.Convert;
        rotate.SubPresetIds = [];
        var profiles = BuiltInData.CreateProfiles();

        Assert.True(SettingsMigrations.ApplyToLibrary(16, presets, profiles));

        Assert.Equal(ImageRotation.Left90, left.Image.Rotation);
        Assert.Equal("Turn left", left.Name);
        Assert.True(rotate.IsGroup);
        Assert.Equal(["rotate-left", "rotate-right", "rotate-180"], rotate.SubPresetIds);
        Assert.Equal(Preset.GroupTarget, rotate.TargetFormat);
        Assert.Single(presets, p => p.Id == "rotate-left"); // repaired, not added twice
    }

    private static readonly string[] NewTools = ["add-background", "redact", "annotate"];

    /// <summary>Writes the library 1.0.0 left (the three tools of that version, none of the new ones) and loads it.</summary>
    private static UserDataStore LibraryFrom1_0_0(string directory, params string[] toolsGroup)
    {
        var seed = new UserDataStore(directory);
        seed.Load();
        seed.Presets.RemoveAll(p => NewTools.Contains(p.Id) || p.Id == "quick-background");
        seed.Presets.Single(p => p.Id == "tools").SubPresetIds = [.. toolsGroup];
        seed.SaveLibrary(); // saving adds nothing: only loading does
        Assert.All(NewTools, id => Assert.Null(seed.FindPreset(id)));

        var store = new UserDataStore(directory);
        store.Load();
        return store;
    }

    [Fact]
    public void A_library_from_1_0_0_gains_the_new_tools_once()
    {
        using var dir = new TempDir();

        var store = LibraryFrom1_0_0(dir.Path, "crop", "compress-image", "strip-metadata");

        Assert.Equal(["crop", "add-background", "redact", "annotate", "compress-image", "strip-metadata"], store.FindPreset("tools")!.SubPresetIds);
        foreach (var id in NewTools)
        {
            Assert.Single(store.Presets, p => p.Id == id);
            Assert.Equal(PresetKind.Tool, store.FindPreset(id)!.Kind);
        }
        // Where a new library has them in the preset list (the quick background comes with them, after add-background),
        // and saved right away so the next start finds them there.
        Assert.Equal(["crop", "add-background", "quick-background", "redact", "annotate", "compress-image"],
            store.Presets.Select(p => p.Id).SkipWhile(id => id != "crop").Take(6));
        Assert.Contains("\"id\": \"redact\"", File.ReadAllText(Path.Combine(dir.Path, "presets.json")));

        // The user takes "Redact" off the ring: the preset stays, so it is not offered again.
        store.FindPreset("tools")!.SubPresetIds.Remove("redact");
        store.SaveLibrary();
        var again = new UserDataStore(dir.Path);
        again.Load();
        Assert.Equal(["crop", "add-background", "annotate", "compress-image", "strip-metadata"], again.FindPreset("tools")!.SubPresetIds);
        Assert.Single(again.Presets, p => p.Id == "redact");
    }

    [Fact]
    public void The_new_tools_go_where_the_spec_puts_them_in_a_group_the_user_arranged_and_not_into_a_full_one()
    {
        using var dir = new TempDir();
        var arranged = LibraryFrom1_0_0(dir.Path, "strip-metadata", "crop");
        // After the tool that comes before each in the spec, whatever else the user moved around it.
        Assert.Equal(["strip-metadata", "crop", "add-background", "redact", "annotate"], arranged.FindPreset("tools")!.SubPresetIds);

        using var other = new TempDir();
        string[] full = ["strip-metadata", "crop", "compress-image", "rotate-left", "rotate-right", "flip-horizontal"];
        var crowded = LibraryFrom1_0_0(other.Path, full);
        Assert.Equal(full, crowded.FindPreset("tools")!.SubPresetIds); // no room: nothing is pushed out
        Assert.All(NewTools, id => Assert.NotNull(crowded.FindPreset(id))); // still there to add in Settings → Toolbar
    }

    [Fact]
    public void A_library_from_before_the_folders_donut_gains_it_once_after_the_normal_ones()
    {
        using var dir = new TempDir();
        var seed = new UserDataStore(dir.Path);
        seed.Load();
        seed.Profiles.RemoveAll(p => p.ForFolders);
        seed.SaveLibrary(); // saving adds nothing: only loading does
        Assert.DoesNotContain(seed.Profiles, p => p.ForFolders);

        var store = new UserDataStore(dir.Path);
        store.Load();

        var folders = Assert.Single(store.Profiles, p => p.ForFolders);
        Assert.Equal(("folders", "profile.folders"), (folders.Id, folders.NameKey));
        Assert.Equal(["to-zip", "to-7z", "zip-all"], folders.PresetIds);
        Assert.Empty(folders.Extensions);
        Assert.Equal(store.Profiles.FindIndex(p => p.IsFallback) - 1, store.Profiles.IndexOf(folders));
        Assert.Contains("\"forFolders\": true", File.ReadAllText(Path.Combine(dir.Path, "profiles.json")));
        Assert.False(SettingsMigrations.AddMissingFolderProfile(store.Profiles)); // once
    }

    /// <summary>A library as 1.2 left it: no quick background preset, no slice for it. <paramref name="arrange"/> changes the images donut before it is saved.</summary>
    private static UserDataStore SeedWithoutQuickBackground(string directory, Action<ToolbarProfile>? arrange = null)
    {
        var seed = new UserDataStore(directory);
        seed.Load();
        seed.Presets.RemoveAll(p => p.Id == "quick-background");
        var images = seed.Profiles.Single(p => p.Id == "images");
        images.PresetIds.Remove("quick-background");
        arrange?.Invoke(images);
        seed.SaveLibrary(); // saving adds nothing: only loading does
        Assert.Null(seed.FindPreset("quick-background"));
        return seed;
    }

    [Fact]
    public void A_library_from_before_the_quick_background_gains_it_once_at_the_end_of_the_images_donut()
    {
        using var dir = new TempDir();
        var before = SeedWithoutQuickBackground(dir.Path).Profiles.Single(p => p.Id == "images").PresetIds.ToList();
        Assert.Equal(ToolbarProfile.MaxSlices - 1, before.Count);

        var store = new UserDataStore(dir.Path);
        store.Load();

        var quick = Assert.Single(store.Presets, p => p.Id == "quick-background");
        Assert.True(quick.RunWithLastChoices);
        Assert.Equal("add-background", store.Presets[store.Presets.IndexOf(quick) - 1].Id); // where a new library has it
        Assert.Equal([.. before, "quick-background"], store.Profiles.Single(p => p.Id == "images").PresetIds);
        // Saved at once, so the next start finds both there.
        Assert.Contains("\"id\": \"quick-background\"", File.ReadAllText(Path.Combine(dir.Path, "presets.json")));
        Assert.Contains("\"quick-background\"", File.ReadAllText(Path.Combine(dir.Path, "profiles.json")));

        // The user takes the slice off the donut: the preset stays, so the slice is not put back.
        store.Profiles.Single(p => p.Id == "images").PresetIds.Remove("quick-background");
        store.SaveLibrary();
        var again = new UserDataStore(dir.Path);
        again.Load();
        Assert.DoesNotContain("quick-background", again.Profiles.Single(p => p.Id == "images").PresetIds);
        Assert.Single(again.Presets, p => p.Id == "quick-background");
        Assert.False(SettingsMigrations.AddQuickBackground(again.Presets, again.Profiles));
    }

    [Fact]
    public void The_quick_background_is_not_squeezed_onto_a_full_images_donut_but_is_still_a_preset_to_add()
    {
        using var dir = new TempDir();
        var full = SeedWithoutQuickBackground(dir.Path, images => images.PresetIds.Add("to-gif")).Profiles.Single(p => p.Id == "images").PresetIds.ToList();
        Assert.Equal(ToolbarProfile.MaxSlices, full.Count); // a 12th slice of the user's own

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Equal(full, store.Profiles.Single(p => p.Id == "images").PresetIds);
        Assert.Single(store.Presets, p => p.Id == "quick-background");
    }

    [Fact]
    public void The_quick_background_slice_is_added_only_for_a_new_preset_a_donut_that_exists_and_has_room()
    {
        Assert.False(SettingsMigrations.AddQuickBackground(BuiltInData.CreatePresets(), BuiltInData.CreateProfiles())); // a complete library is left alone

        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "quick-background");
        var profiles = BuiltInData.CreateProfiles();
        var images = profiles.Single(p => p.Id == "images");
        images.PresetIds.Remove("quick-background");

        // No images donut (the user deleted it): nothing to put it on.
        Assert.False(SettingsMigrations.AddQuickBackground(presets, [.. profiles.Where(p => p.Id != "images")]));

        // It runs before AddMissingToolPresets, which is what makes the preset: the slice goes on while the id is still missing.
        Assert.True(SettingsMigrations.AddQuickBackground(presets, profiles));
        Assert.Equal("quick-background", images.PresetIds[^1]);
        Assert.DoesNotContain(presets, p => p.Id == "quick-background");
        Assert.True(SettingsMigrations.AddMissingToolPresets(presets));
        Assert.Single(presets, p => p.Id == "quick-background");

        // Its id is on the donut already (the profiles were read, the presets were not): once.
        presets.RemoveAll(p => p.Id == "quick-background");
        Assert.False(SettingsMigrations.AddQuickBackground(presets, profiles));
        Assert.Single(images.PresetIds, id => id == "quick-background");
    }

    [Fact]
    public void A_quick_background_an_older_Filee_saved_without_its_flag_gets_it_back_once_and_the_donut_is_left_alone()
    {
        using var dir = new TempDir();
        var seed = new UserDataStore(dir.Path);
        seed.Load();
        // What a Filee from before the flag leaves after saving: the preset as it was, the flag dropped (read as false). The
        // user had taken the slice off the donut.
        seed.FindPreset("quick-background")!.RunWithLastChoices = false;
        var ring = seed.Profiles.Single(p => p.Id == "images").PresetIds;
        ring.Remove("quick-background");
        seed.SaveLibrary();

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.True(store.FindPreset("quick-background")!.RunWithLastChoices); // no editor could have: it opens no window again
        Assert.DoesNotContain("quick-background", store.Profiles.Single(p => p.Id == "images").PresetIds);
        Assert.Contains("\"runWithLastChoices\": true", File.ReadAllText(Path.Combine(dir.Path, "presets.json"))); // saved at once

        // Once; and a preset of that id that is another tool (a hand-edited file) is not turned into this one.
        Assert.False(SettingsMigrations.AddQuickBackground(store.Presets, store.Profiles));
        var other = store.FindPreset("quick-background")!;
        (other.Tool, other.RunWithLastChoices) = (ImageToolKind.Crop, false);
        Assert.False(SettingsMigrations.AddQuickBackground(store.Presets, store.Profiles));
        Assert.False(other.RunWithLastChoices);
    }

    [Fact]
    public void Missing_tool_presets_are_added_only_when_the_id_is_absent_and_the_group_is_not_brought_back()
    {
        Assert.False(SettingsMigrations.AddMissingToolPresets(BuiltInData.CreatePresets())); // a complete library is left alone

        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "tools" || NewTools.Contains(p.Id));
        var mine = presets.Single(p => p.Id == "crop");
        mine.Name = "My crop";

        Assert.True(SettingsMigrations.AddMissingToolPresets(presets));

        Assert.Equal(NewTools, presets.Where(p => NewTools.Contains(p.Id)).Select(p => p.Id));
        Assert.DoesNotContain(presets, p => p.Id == "tools"); // the user deleted the group
        Assert.Equal("My crop", mine.Name); // an id that is there is not touched
        Assert.False(SettingsMigrations.AddMissingToolPresets(presets));
    }

    [Fact]
    public void Version_17_libraries_get_compress_pdf_after_split_pdf_on_no_donut()
    {
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "compress-pdf");
        var profiles = BuiltInData.CreateProfiles();

        Assert.True(SettingsMigrations.ApplyToLibrary(17, presets, profiles));

        var compress = presets.Single(p => p.Id == "compress-pdf");
        Assert.Equal(("pdf", PdfCompression.Ebook, false), (compress.TargetFormat, compress.Pdf.Compression, compress.Pdf.CompressWithGhostscript));
        Assert.Equal("pdf-split", presets[presets.IndexOf(compress) - 1].Id); // where a new library has it
        Assert.DoesNotContain(profiles, p => p.PresetIds.Contains("compress-pdf"));
        Assert.Equal(BuiltInData.CreateProfiles().Single(p => p.Id == "pdf").PresetIds, profiles.Single(p => p.Id == "pdf").PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(17, presets, profiles)); // once only
    }

    [Fact]
    public void Back_from_an_older_Filee_compress_pdf_compresses_again_and_keeps_the_users_changes()
    {
        // What a Filee before 20 leaves after saving: the compression level dropped, the rest as it was.
        var presets = BuiltInData.CreatePresets();
        var compress = presets.Single(p => p.Id == "compress-pdf");
        compress.Pdf.Compression = PdfCompression.None;
        compress.Name = "Shrink";

        Assert.True(SettingsMigrations.ApplyToLibrary(17, presets, BuiltInData.CreateProfiles()));

        Assert.Equal(PdfCompression.Ebook, compress.Pdf.Compression);
        Assert.Equal("Shrink", compress.Name);
        Assert.Single(presets, p => p.Id == "compress-pdf");
    }

    [Fact]
    public void Pdf_compression_survives_a_save_and_load_and_a_newer_level_reads_as_none()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        var preset = store.Presets.Single(p => p.Id == "compress-pdf");
        preset.Pdf.Compression = PdfCompression.Print;
        preset.Pdf.CompressWithGhostscript = true;
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        var pdf = reloaded.Presets.Single(p => p.Id == "compress-pdf").Pdf;
        Assert.Equal((PdfCompression.Print, true), (pdf.Compression, pdf.CompressWithGhostscript));

        var path = Path.Combine(dir.Path, "presets.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"compression\": \"Print\"", "\"compression\": \"Extreme\""));
        var newer = new UserDataStore(dir.Path);
        newer.Load();
        Assert.Equal(PdfCompression.None, newer.FindPreset("compress-pdf")!.Pdf.Compression);
        Assert.False(File.Exists(path + ".broken"));
    }

    [Fact]
    public void Catalog_migration_ignores_extensions_checked_on_mixed_profiles()
    {
        // Schema-5 data where the user already limited a mixed-files donut to MP3 and JPG: those checks must not
        // count as "taken" when the catalog donuts and extensions are added.
        var presets = BuiltInData.CreatePresets().Where(p => BuiltInData.CatalogPresets().All(c => c.Id != p.Id)).ToList();
        var catalogIds = BuiltInData.CatalogProfiles().Select(p => p.Id).Append("models").ToHashSet();
        var profiles = BuiltInData.CreateProfiles().Where(p => !catalogIds.Contains(p.Id)).ToList();
        profiles.Single(p => p.Id == "images").Extensions = ["png"];
        profiles.Add(new ToolbarProfile { Id = "music-mix", Name = "Music mix", IsFallback = true, Extensions = ["mp3", "jpg"] });

        Assert.True(SettingsMigrations.ApplyToLibrary(5, presets, profiles));

        Assert.Contains("mp3", profiles.Single(p => p.Id == "audio").Extensions);
        Assert.Contains("jpg", profiles.Single(p => p.Id == "images").Extensions);
        Assert.Equal(["mp3", "jpg"], profiles.Single(p => p.Id == "music-mix").Extensions);
    }

    [Fact]
    public void Several_mixed_profiles_are_kept_and_one_is_added_when_none_is_left()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Profiles.Add(new ToolbarProfile { Id = "scans", Name = "Scans", IsFallback = true, Extensions = ["jpg", "pdf"] });
        store.SaveLibrary();

        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Equal(["mixed", "scans"], reloaded.Profiles.Where(p => p.IsFallback).Select(p => p.Id));

        reloaded.Profiles.RemoveAll(p => p.IsFallback);
        reloaded.SaveLibrary();
        Assert.Equal("mixed", Assert.Single(reloaded.Profiles, p => p.IsFallback).Id);
    }

    [Fact]
    public void Imported_mixed_profiles_stay_mixed()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Profiles.Add(new ToolbarProfile { Id = "scans", Name = "Scans", IsFallback = true, Extensions = ["jpg", "pdf"] });
        var bundle = Path.Combine(dir.Path, "bundle.json");
        store.Export(bundle);

        using var other = new TempDir();
        var target = new UserDataStore(other.Path);
        target.Load();
        target.Import(bundle);

        Assert.True(target.Profiles.Single(p => p.Id == "scans").IsFallback);
        Assert.Single(target.Profiles, p => p.Id == "mixed");
        Assert.Equal("scans", ProfileSelector.Select(target.Profiles, ["a.jpg", "b.pdf"])!.Id);
        Assert.Equal("mixed", ProfileSelector.Select(target.Profiles, ["a.jpg", "b.docx"])!.Id);
    }

    [Fact]
    public void Spreadsheet_profile_is_not_added_when_a_user_profile_takes_spreadsheets()
    {
        var profiles = BuiltInData.CreateProfiles().Where(p => p.Id is not ("spreadsheets" or "presentations")).ToList();
        profiles.Insert(0, new ToolbarProfile { Id = "mine", Name = "Mine", Extensions = ["xlsx"] });

        SettingsMigrations.ApplyToLibrary(4, BuiltInData.CreatePresets(), profiles);

        Assert.DoesNotContain(profiles, p => p.Id == "spreadsheets");
        Assert.Contains(profiles, p => p.Id == "presentations");
        Assert.Equal(["xlsx"], profiles[0].Extensions);
    }

    [Fact]
    public void Text_profile_is_not_added_when_a_profile_already_takes_markdown()
    {
        var profiles = BuiltInData.CreateProfiles().Where(p => p.Id != "text").ToList();
        profiles[0].Extensions.Add("md");

        SettingsMigrations.ApplyToLibrary(3, BuiltInData.CreatePresets(), profiles);
        Assert.DoesNotContain(profiles, p => p.Id == "text");
    }

    [Fact]
    public void Enums_are_stored_as_strings()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.SaveSettings();

        var json = File.ReadAllText(Path.Combine(dir.Path, "settings.json"));
        Assert.Contains("\"Drag\"", json);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_kept()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "presets.json"), "{ not json");

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.NotEmpty(store.Presets);
        Assert.True(File.Exists(Path.Combine(dir.Path, "presets.json.broken")));
    }

    [Fact]
    public void Dangling_preset_references_are_removed()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Profiles[0].PresetIds.Add("does-not-exist");
        store.SaveLibrary();

        Assert.DoesNotContain("does-not-exist", store.Profiles[0].PresetIds);
    }

    [Fact]
    public void Export_then_import_restores_presets()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Presets.Add(new Preset { Id = "shared", Name = "Shared", TargetFormat = "png" });
        var bundle = Path.Combine(dir.Path, "bundle.json");
        store.Export(bundle);

        using var other = new TempDir();
        var target = new UserDataStore(other.Path);
        target.Load();
        target.Import(bundle);

        Assert.NotNull(target.FindPreset("shared"));
        Assert.Single(target.Profiles, p => p.IsFallback);
    }

    [Fact]
    public void Screen_shortcuts_come_to_an_existing_user_once()
    {
        var settings = new AppSettings { Triggers = [new() { Kind = TriggerKind.Drag }] };
        Assert.False(settings.ScreenShortcutsAdded);

        Assert.True(SettingsMigrations.AddScreenShortcuts(settings, windows: true));

        Assert.True(settings.ScreenShortcutsAdded);
        Assert.Equal([TriggerAction.Donut, TriggerAction.Capture, TriggerAction.PickColor], settings.Triggers.Select(g => g.Action));
        Assert.All(settings.Triggers, g => Assert.True(g.Enabled));
        Assert.Equal(("S", "C"), (settings.Triggers[1].Key, settings.Triggers[2].Key));

        // A shortcut the user then deletes stays deleted.
        settings.Triggers.RemoveAt(1);
        Assert.False(SettingsMigrations.AddScreenShortcuts(settings, windows: true));
        Assert.Equal(2, settings.Triggers.Count);
    }

    [Fact]
    public void A_screen_shortcut_on_a_key_the_user_already_uses_comes_in_switched_off()
    {
        var settings = new AppSettings
        {
            Triggers = [new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Key = "S" }],
        };

        SettingsMigrations.AddScreenShortcuts(settings, windows: true);

        Assert.True(settings.Triggers[0].Enabled); // theirs, untouched
        Assert.False(settings.Triggers.Single(g => g.Action == TriggerAction.Capture).Enabled);
        Assert.True(settings.Triggers.Single(g => g.Action == TriggerAction.PickColor).Enabled);
    }

    [Fact]
    public void Screen_shortcuts_are_not_added_twice_to_settings_that_have_them()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A new install has the screen shortcuts on Windows only");
        var defaults = new AppSettings(); // a new install's triggers already hold both
        Assert.False(SettingsMigrations.AddScreenShortcuts(defaults)); // nothing to add, nothing to save
        Assert.True(defaults.ScreenShortcutsAdded);
        Assert.Single(defaults.Triggers, g => g.Action == TriggerAction.Capture);
        Assert.Single(defaults.Triggers, g => g.Action == TriggerAction.PickColor);

        var done = new AppSettings { ScreenShortcutsAdded = true, Triggers = [] };
        Assert.False(SettingsMigrations.AddScreenShortcuts(done));
        Assert.Empty(done.Triggers);
    }

    [Fact]
    public void Load_adds_the_screen_shortcuts_to_settings_from_before_them_and_saves_them()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The screen shortcuts are Windows-only");
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path,
            """{"schemaVersion":22,"language":"ko","triggers":[{"kind":"Drag","modifiers":"Ctrl","button":"Left"},{"kind":"KeyChord","modifiers":"Ctrl, Alt","key":"Space"}]}""");

        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Equal("ko", store.Settings.Language);
        Assert.Equal([TriggerAction.Donut, TriggerAction.Donut, TriggerAction.Capture, TriggerAction.PickColor], store.Settings.Triggers.Select(g => g.Action));
        Assert.True(store.Settings.ScreenShortcutsAdded);
        Assert.Contains("\"screenShortcutsAdded\": true", File.ReadAllText(path)); // saved without any other change

        store.Settings.Triggers.RemoveAll(g => g.Action == TriggerAction.Capture);
        store.SaveSettings();
        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.DoesNotContain(reloaded.Settings.Triggers, g => g.Action == TriggerAction.Capture);
    }

    [Fact]
    public void A_ring_shortcut_on_the_keys_of_a_screen_shortcut_gets_its_action_back_instead_of_a_second_card()
    {
        // What a Bingling from before the actions leaves after saving: both screen shortcuts as ring shortcuts on their
        // keys (everywhere), one of them switched off, and the flag gone.
        var settings = new AppSettings
        {
            Triggers =
            [
                new() { Kind = TriggerKind.Drag },
                new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Button = TriggerMouseButton.None, Key = "s", Scope = TriggerScope.Anywhere },
                new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Button = TriggerMouseButton.None, Key = "C", Scope = TriggerScope.Anywhere, Enabled = false },
            ],
        };

        Assert.True(SettingsMigrations.AddScreenShortcuts(settings, windows: true));

        Assert.Equal([TriggerAction.Donut, TriggerAction.Capture, TriggerAction.PickColor], settings.Triggers.Select(g => g.Action));
        Assert.Equal([true, true, false], settings.Triggers.Select(g => g.Enabled)); // on or off as it was
        Assert.True(settings.ScreenShortcutsAdded);
    }

    [Fact]
    public void A_round_trip_through_a_Bingling_that_does_not_know_the_actions_keeps_the_screen_shortcuts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The screen shortcuts are Windows-only");
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var first = new UserDataStore(dir.Path);
        first.Load();
        first.Settings.Triggers.Single(g => g.Action == TriggerAction.PickColor).Enabled = false;
        first.SaveSettings();
        var before = first.Settings.Triggers.Select(g => (g.Kind, g.Key, g.Scope, g.Action, g.Enabled)).ToList();
        Assert.Equal(5, before.Count);

        // 1.2.x saves its settings without what it doesn't know: each gesture's action, and the flag.
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(json.Remove("screenShortcutsAdded"));
        foreach (var gesture in json["triggers"]!.AsArray())
            Assert.True(gesture!.AsObject().Remove("action"));
        File.WriteAllText(path, json.ToJsonString());

        var again = new UserDataStore(dir.Path);
        again.Load();

        Assert.Equal(before, again.Settings.Triggers.Select(g => (g.Kind, g.Key, g.Scope, g.Action, g.Enabled)));
        Assert.True(again.Settings.ScreenShortcutsAdded);
        Assert.Contains("\"action\": \"Capture\"", File.ReadAllText(path)); // saved with the fix, once

        var third = new UserDataStore(dir.Path);
        third.Load();
        Assert.Equal(before, third.Settings.Triggers.Select(g => (g.Kind, g.Key, g.Scope, g.Action, g.Enabled)));
    }

    [Fact]
    public void A_new_install_has_each_screen_shortcut_once()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The screen shortcuts are Windows-only");
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();

        Assert.Single(store.Settings.Triggers, g => g.Action == TriggerAction.Capture);
        Assert.Single(store.Settings.Triggers, g => g.Action == TriggerAction.PickColor);
        Assert.True(store.Settings.ScreenShortcutsAdded);
    }

    [Fact]
    public void Outside_Windows_no_screen_shortcut_is_added_and_the_offer_stays_open()
    {
        var settings = new AppSettings { Triggers = [new() { Kind = TriggerKind.Drag }] };

        Assert.False(SettingsMigrations.AddScreenShortcuts(settings, windows: false));

        Assert.Single(settings.Triggers);
        Assert.False(settings.ScreenShortcutsAdded); // still offered once the tools come to this platform
    }
}
