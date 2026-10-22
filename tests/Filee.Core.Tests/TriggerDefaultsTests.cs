using System.Text.Json;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class TriggerDefaultsTests
{
    [Fact]
    public void Windows_defaults_keep_shift_drag_but_switched_off()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows defaults");
        // The ring shortcuts (the screen ones are Capture and PickColor).
        var d = TriggerGesture.Defaults().Where(g => g.Action == TriggerAction.Donut).ToList();
        Assert.Equal(3, d.Count);
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Ctrl, true), (d[0].Kind, d[0].Modifiers, d[0].Enabled));
        Assert.Equal(TriggerKind.KeyChord, d[1].Kind);
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Shift, false), (d[2].Kind, d[2].Modifiers, d[2].Enabled));
    }

    [Fact]
    public void Defaults_add_capture_and_colour_pick_shortcuts_anywhere()
    {
        var d = TriggerGesture.Defaults(windows: true);
        var capture = Assert.Single(d, g => g.Action == TriggerAction.Capture);
        var pick = Assert.Single(d, g => g.Action == TriggerAction.PickColor);
        Assert.Equal((TriggerKind.KeyChord, ModifierKeys.Ctrl | ModifierKeys.Alt, "S", TriggerScope.Anywhere, true),
            (capture.Kind, capture.Modifiers, capture.Key, capture.Scope, capture.Enabled));
        Assert.Equal((TriggerKind.KeyChord, ModifierKeys.Ctrl | ModifierKeys.Alt, "C", TriggerScope.Anywhere, true),
            (pick.Kind, pick.Modifiers, pick.Key, pick.Scope, pick.Enabled));
        // The ring's own shortcuts are still there and still open the ring.
        Assert.Contains(d, g => g.Kind == TriggerKind.Drag && g.Action == TriggerAction.Donut);
        Assert.Contains(d, g => g.Kind == TriggerKind.KeyChord && g.Key == "Space" && g.Action == TriggerAction.Donut);
        Assert.Equal(TriggerAction.Capture, capture.Clone().Action);
    }

    [Fact]
    public void Outside_Windows_the_defaults_have_no_screen_shortcuts()
    {
        // The screen tools are Windows-only: elsewhere the shortcuts would do nothing and only crowd Settings › Shortcuts.
        Assert.All(TriggerGesture.Defaults(windows: false), g => Assert.Equal(TriggerAction.Donut, g.Action));
        Assert.Equal(OperatingSystem.IsWindows(), TriggerGesture.Defaults().Any(g => g.Action == TriggerAction.PickColor));
    }

    [Fact]
    public void The_action_is_saved_and_a_settings_file_from_before_it_opens_the_ring()
    {
        var saved = JsonSerializer.Serialize(new AppSettings { Triggers = [new TriggerGesture { Kind = TriggerKind.KeyChord, Key = "S", Action = TriggerAction.Capture }] },
            FileeJsonContext.Default.AppSettings);
        Assert.Contains("\"action\": \"Capture\"", saved);
        Assert.Equal(TriggerAction.Capture, JsonSerializer.Deserialize(saved, FileeJsonContext.Default.AppSettings)!.Triggers.Single().Action);

        var old = JsonSerializer.Deserialize("""{"triggers":[{"kind":"KeyChord","key":"Space"}]}""", FileeJsonContext.Default.AppSettings)!;
        Assert.Equal(TriggerAction.Donut, old.Triggers.Single().Action);
    }

    [Fact]
    public void A_value_from_a_newer_Filee_does_not_reset_the_settings()
    {
        var json = """
            {"language":"ko","triggers":[
              {"kind":"KeyChord","key":"S","action":"frobnicate"},
              {"kind":"frobnicate","key":"Space"},
              {"kind":"KeyChord","key":"C","scope":"frobnicate","button":"frobnicate","action":"pickColor"}]}
            """;
        var settings = JsonSerializer.Deserialize(json, FileeJsonContext.Default.AppSettings)!;

        Assert.Equal("ko", settings.Language); // the rest of the file still reads
        Assert.Equal(3, settings.Triggers.Count);
        Assert.Equal((TriggerKind.KeyChord, TriggerAction.Donut), (settings.Triggers[0].Kind, settings.Triggers[0].Action));
        Assert.Equal(TriggerKind.Drag, settings.Triggers[1].Kind); // the first value
        Assert.Equal((TriggerScope.FileManager, TriggerMouseButton.None, TriggerAction.PickColor),
            (settings.Triggers[2].Scope, settings.Triggers[2].Button, settings.Triggers[2].Action));
    }
}
