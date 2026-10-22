// The capture and colour-pick shortcuts outside the hook: what Settings › Shortcuts saves for them, how the summary
// names them, and how the tray starts them (after its menu is gone, at the last cursor position).

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.Services.Triggers;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filee.App.Tests;

public class ScreenShortcutTests
{
    private static TriggerGesture Chord(TriggerAction action = TriggerAction.Donut, TriggerScope scope = TriggerScope.FileManager) => new()
    {
        Kind = TriggerKind.KeyChord,
        Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt,
        Button = TriggerMouseButton.None,
        Key = "S",
        Action = action,
        Scope = scope,
    };

    private static ILocalizer Localizer(string language)
    {
        TestServices.EnsureInitialized(language);
        return AppHost.Get<ILocalizer>();
    }

    [AvaloniaFact]
    public void Choosing_capture_saves_the_action_and_makes_it_work_everywhere()
    {
        var gesture = Chord();
        var saves = 0;
        var vm = new TriggerGestureViewModel(gesture, Localizer("en"), () => saves++);

        vm.Action = vm.Actions.Single(a => a.Value == TriggerAction.Capture);

        Assert.Equal(TriggerAction.Capture, gesture.Action);
        Assert.Equal(TriggerScope.Anywhere, gesture.Scope);
        Assert.Equal(TriggerScope.Anywhere, vm.Scope.Value);
        Assert.True(saves > 0);
    }

    [AvaloniaFact]
    public void Choosing_the_colour_pick_makes_it_work_everywhere_too()
    {
        var gesture = Chord();
        var vm = new TriggerGestureViewModel(gesture, Localizer("en"), () => { });

        vm.Action = vm.Actions.Single(a => a.Value == TriggerAction.PickColor);

        Assert.Equal(TriggerAction.PickColor, gesture.Action);
        Assert.Equal(TriggerScope.Anywhere, gesture.Scope);
    }

    [AvaloniaFact]
    public void Going_back_to_the_ring_leaves_the_scope_as_it_is()
    {
        var gesture = Chord(TriggerAction.Capture);
        var vm = new TriggerGestureViewModel(gesture, Localizer("en"), () => { });

        vm.Action = vm.Actions.Single(a => a.Value == TriggerAction.Donut);

        Assert.Equal(TriggerAction.Donut, gesture.Action);
        Assert.Equal(TriggerScope.FileManager, gesture.Scope);
    }

    [AvaloniaFact]
    public void Opening_a_saved_gesture_shows_its_action_without_touching_it()
    {
        var gesture = Chord(TriggerAction.PickColor, TriggerScope.FileManager);
        var vm = new TriggerGestureViewModel(gesture, Localizer("en"), () => Assert.Fail("opening must not save"));

        Assert.Equal(TriggerAction.PickColor, vm.Action.Value);
        Assert.Equal(TriggerScope.FileManager, gesture.Scope);
    }

    [AvaloniaTheory]
    [InlineData(TriggerKind.Drag)]
    [InlineData(TriggerKind.Hold)]
    public void A_drag_or_hold_gesture_can_only_open_the_ring(TriggerKind kind)
    {
        var gesture = Chord(TriggerAction.PickColor, TriggerScope.Anywhere);
        var vm = new TriggerGestureViewModel(gesture, Localizer("en"), () => { });

        vm.Kind = vm.Kinds.Single(k => k.Value == kind);

        Assert.Equal(kind, gesture.Kind);
        Assert.Equal(TriggerAction.Donut, gesture.Action);
        Assert.Equal(TriggerAction.Donut, vm.Action.Value);
        Assert.False(vm.UsesAction);
    }

    [AvaloniaFact]
    public void The_action_choice_is_for_key_chords_on_Windows()
    {
        var vm = new TriggerGestureViewModel(Chord(), Localizer("en"), () => { });
        Assert.Equal(OperatingSystem.IsWindows(), vm.UsesAction);

        vm.Kind = vm.Kinds.Single(k => k.Value == TriggerKind.Drag);
        Assert.False(vm.UsesAction);
    }

    [AvaloniaFact]
    public void The_actions_are_named_in_the_current_language()
    {
        var vm = new TriggerGestureViewModel(Chord(), Localizer("ko"), () => { });

        Assert.Equal(["링 열기 (선택한 파일)", "화면 캡처", "색 찍기"], vm.Actions.Select(a => a.Label));
        Assert.Equal("키보드 단축키", vm.Kinds.Single(k => k.Value == TriggerKind.KeyChord).Label);
    }

    [AvaloniaFact]
    public void The_summary_adds_the_action_unless_it_opens_the_ring()
    {
        var ko = Localizer("ko");
        Assert.Equal("Ctrl + Alt + S", GestureText.Describe(Chord(), ko));
        Assert.Equal("Ctrl + Alt + S · 화면 캡처", GestureText.Describe(Chord(TriggerAction.Capture), ko));
        Assert.Equal("Ctrl + Alt + S · 색 찍기", GestureText.Describe(Chord(TriggerAction.PickColor), ko));

        var vm = new TriggerGestureViewModel(Chord(), Localizer("en"), () => { });
        Assert.Equal("Ctrl + Alt + S", vm.Summary);
        vm.Action = vm.Actions.Single(a => a.Value == TriggerAction.Capture);
        Assert.Equal("Ctrl + Alt + S · Capture the screen", vm.Summary);
    }

    [AvaloniaFact]
    public void A_drag_never_names_an_action_even_one_left_in_its_settings()
    {
        var drag = Chord(TriggerAction.PickColor);
        drag.Kind = TriggerKind.Drag;
        drag.Button = TriggerMouseButton.Left;

        Assert.Equal("Ctrl + Alt + Left", GestureText.Describe(drag, Localizer("ko"))); // a drag opens the ring whatever it says
    }

    [AvaloniaFact]
    public async Task A_screen_tool_that_fails_from_the_tray_does_not_end_Bingling()
    {
        TestServices.EnsureInitialized("en");
        var tray = new TrayService(AppHost.Get<UserDataStore>(), AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>(),
            AppHost.Get<UpdateService>(), new RecordingScreenTools(), AppHost.Get<TriggerService>(), NullLogger<TrayService>.Instance);

        // The menu item's handler is async void: an exception out of it would end the app.
        await tray.StartAfterMenuClosesAsync(_ => throw new InvalidOperationException("no screen"));
    }

    [AvaloniaFact]
    public async Task The_tray_starts_a_screen_tool_after_its_menu_is_gone_at_the_last_cursor()
    {
        TestServices.EnsureInitialized("en");
        var triggers = new TriggerService(AppHost.Get<IPlatformServices>(), NullLogger<TriggerService>.Instance) { LastCursor = (120, 340) };
        var screen = new RecordingScreenTools();
        var tray = new TrayService(AppHost.Get<UserDataStore>(), AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>(),
            AppHost.Get<UpdateService>(), screen, triggers, NullLogger<TrayService>.Instance);

        var clock = Stopwatch.StartNew();
        await tray.StartAfterMenuClosesAsync(screen.StartCapture);
        var waited = clock.Elapsed;
        await tray.StartAfterMenuClosesAsync(screen.StartColorPick);

        Assert.Equal([new PixelPoint(120, 340)], screen.Captures);
        Assert.Equal([new PixelPoint(120, 340)], screen.ColorPicks);
        Assert.True(waited >= TimeSpan.FromMilliseconds(100), $"started after {waited.TotalMilliseconds} ms");
    }

    [AvaloniaFact]
    public void On_Windows_the_tray_menu_offers_both_screen_tools()
    {
        TestServices.EnsureInitialized("ko");
        var tray = new TrayService(AppHost.Get<UserDataStore>(), AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>(),
            AppHost.Get<UpdateService>(), new RecordingScreenTools(), AppHost.Get<TriggerService>(), NullLogger<TrayService>.Instance);
        tray.Create(Application.Current!);
        try
        {
            var headers = TrayIcon.GetIcons(Application.Current!)![0].Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Equal(OperatingSystem.IsWindows(), headers.Contains("화면 캡처"));
            Assert.Equal(OperatingSystem.IsWindows(), headers.Contains("색 찍기"));
        }
        finally
        {
            tray.Dispose();
        }
    }

    private sealed class RecordingScreenTools : IScreenTools
    {
        public List<PixelPoint> Captures { get; } = [];
        public List<PixelPoint> ColorPicks { get; } = [];
        public void StartCapture(PixelPoint cursor) => Captures.Add(cursor);
        public void StartColorPick(PixelPoint cursor) => ColorPicks.Add(cursor);
    }
}
