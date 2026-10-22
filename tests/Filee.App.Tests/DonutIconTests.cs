// Icons on donut slices: which picture each preset gets, that the default donuts (and the Tools group's ring) are all
// covered, and that they reach the slices the view model builds.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class DonutIconTests
{
    private static Geometry? Icon(string key) => Application.Current!.FindResource(key) as Geometry;

    private static Preset Find(string id) => AppHost.Get<UserDataStore>().FindPreset(id)!;

    [AvaloniaFact]
    public void Every_slice_on_the_default_image_and_pdf_donuts_has_an_icon()
    {
        TestServices.EnsureInitialized("ko");
        var store = AppHost.Get<UserDataStore>();
        foreach (var profileId in new[] { "images", "pdf" })
            foreach (var id in store.Profiles.Single(p => p.Id == profileId).PresetIds)
                Assert.NotNull(DonutIcons.For(store.FindPreset(id)!));
    }

    [AvaloniaFact]
    public void Every_preset_on_the_tools_rotate_and_flip_rings_has_its_own_icon()
    {
        TestServices.EnsureInitialized("en");
        var seen = new List<Geometry>();
        foreach (var group in new[] { "tools", "rotate", "flip" })
        {
            var preset = Find(group);
            seen.Add(DonutIcons.For(preset)!);
            foreach (var child in preset.SubPresetIds)
                seen.Add(DonutIcons.For(Find(child))!);
        }
        Assert.All(seen, Assert.NotNull);
    }

    [AvaloniaTheory]
    [InlineData("crop", "Icon.Donut.Crop")]
    [InlineData("add-background", "Icon.Donut.AddBackground")]
    [InlineData("quick-background", "Icon.Donut.QuickBackground")]
    [InlineData("redact", "Icon.Donut.Redact")]
    [InlineData("annotate", "Icon.Donut.Annotate")]
    [InlineData("compress-image", "Icon.Donut.Compress")]
    [InlineData("strip-metadata", "Icon.Donut.StripMetadata")]
    [InlineData("tools", "Icon.Donut.Tools")]
    [InlineData("rotate", "Icon.Donut.RotateRight")]
    [InlineData("rotate-right", "Icon.Donut.RotateRight")]
    [InlineData("rotate-180", "Icon.Donut.RotateRight")]
    [InlineData("rotate-left", "Icon.Donut.RotateLeft")]
    [InlineData("flip", "Icon.Donut.FlipHorizontal")]
    [InlineData("flip-horizontal", "Icon.Donut.FlipHorizontal")]
    [InlineData("flip-vertical", "Icon.Donut.FlipVertical")]
    // The id wins over the target: merging PDFs is not "a document", zipping everything is not "an archive".
    [InlineData("merge-pdf", "Icon.Donut.Merge")]
    [InlineData("zip-all", "Icon.Donut.Merge")]
    [InlineData("extract", "Icon.Donut.Archive")]
    // By what the preset makes.
    [InlineData("to-png", "Icon.Donut.Image")]
    [InlineData("trace-svg", "Icon.Donut.Image")]
    [InlineData("half-size", "Icon.Donut.Image")]
    [InlineData("to-pdf", "Icon.Donut.Document")]
    [InlineData("pdf-split", "Icon.Donut.Document")]
    [InlineData("to-hwp", "Icon.Donut.Document")]
    [InlineData("to-epub", "Icon.Donut.Document")]
    [InlineData("to-xlsx", "Icon.Donut.Document")]
    [InlineData("to-pptx", "Icon.Donut.Document")]
    [InlineData("to-mp4", "Icon.Donut.Video")]
    [InlineData("to-mp3", "Icon.Donut.Audio")]
    [InlineData("to-zip", "Icon.Donut.Archive")]
    public void A_preset_gets_the_icon_for_what_it_does(string presetId, string key)
    {
        TestServices.EnsureInitialized("en");
        var expected = Icon(key);
        Assert.NotNull(expected);
        Assert.Same(expected, DonutIcons.For(Find(presetId)));
    }

    [AvaloniaFact]
    public void The_quick_background_looks_different_from_add_background_even_though_it_is_the_same_tool()
    {
        TestServices.EnsureInitialized("en");
        var quick = DonutIcons.For(Find("quick-background"));
        var add = DonutIcons.For(Find("add-background"));
        Assert.NotNull(quick);
        Assert.NotNull(add);
        Assert.NotSame(add, quick);
        Assert.NotEqual(add.Bounds, quick.Bounds); // not a copy of the other picture
        Assert.Equal(Find("quick-background").Tool, Find("add-background").Tool); // the id tells them apart, not the tool
    }

    [AvaloniaTheory]
    [InlineData("to-dxf")]
    [InlineData("to-ttf")]
    [InlineData("to-srt")]
    [InlineData("to-json")]
    [InlineData("to-glb")]
    public void A_preset_with_no_fitting_picture_gets_none(string presetId)
    {
        TestServices.EnsureInitialized("en");
        Assert.Null(DonutIcons.For(Find(presetId)));
    }

    [AvaloniaFact]
    public void A_group_of_the_users_own_has_no_icon_and_the_preview_formats_have_one()
    {
        TestServices.EnsureInitialized("en");
        var own = new Preset { Kind = PresetKind.Group, TargetFormat = Preset.GroupTarget };
        Assert.Null(DonutIcons.For(own));
        Assert.Same(Icon("Icon.Donut.Image"), DonutIcons.ForFormat("webp"));
        Assert.Same(Icon("Icon.Donut.Document"), DonutIcons.ForFormat("hwpx"));
        Assert.Null(DonutIcons.ForFormat("no-such-format"));
    }

    [AvaloniaFact]
    public void The_slices_the_view_model_builds_carry_their_icons_also_on_a_groups_ring()
    {
        TestServices.EnsureInitialized("ko");
        var vm = AppHost.Get<RadialViewModel>();
        vm.Load([ImageToolWindowTests.TestImages.Png(64, 64)], clickMode: true);

        Assert.NotEmpty(vm.Items);
        Assert.All(vm.Items, item => Assert.NotNull(item.Icon));
        var tools = vm.Items.Single(i => i.Id == "tools");
        Assert.Equal(6, tools.Children!.Count);
        Assert.All(tools.Children, child => Assert.NotNull(child.Icon));
        Assert.Same(Icon("Icon.Donut.Crop"), tools.Children[0].Icon);
        Assert.Same(Icon("Icon.Donut.AddBackground"), tools.Children[1].Icon);
        Assert.Same(Icon("Icon.Donut.Redact"), tools.Children[2].Icon);
        Assert.Same(Icon("Icon.Donut.Annotate"), tools.Children[3].Icon);
    }
}
