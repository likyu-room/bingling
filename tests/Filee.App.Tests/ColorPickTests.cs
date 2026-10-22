// The instant eyedropper (Ctrl+Alt+C): a picture of the screen the cursor is on, a window over it that takes the click
// and shows a magnifier, then the colour card with the HEX code already on the clipboard. The screen is a fake platform
// whose every pixel has its own colour, so a test can tell which pixel was picked.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Filee.App.Tests;

public class ColorPickTests
{
    /// <summary>The colour the fake screen has at the desktop pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    internal static Color ScreenColor(int x, int y) => Color.FromRgb((byte)x, (byte)y, 0x7A);

    /// <summary>What the clipboard holds, read through a window of its own (the headless clipboard is one for all).</summary>
    private static async Task<string?> ClipboardText()
    {
        var top = new Window();
        top.Show();
        try
        {
            return await top.Clipboard!.TryGetTextAsync();
        }
        finally
        {
            top.Close();
        }
    }

    /// <summary>Runs the UI thread until <paramref name="done"/> or a second has gone by.</summary>
    internal static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Picking_a_pixel_copies_its_hex_and_the_card_shows_its_codes()
    {
        TestServices.EnsureInitialized("ko");
        var tools = new ScreenTools(new FakeScreen(), new ToolsLog(), AppHost.Get<CaptureOutput>());

        tools.StartColorPick(new PixelPoint(100, 100));
        var picker = Assert.IsType<PickColorWindow>(tools.Picker);
        Assert.True(picker.IsVisible);
        await picker.Clipboard!.SetTextAsync("what was there before");
        picker.MouseDown(new Point(30, 40), MouseButton.Left);

        Assert.Null(tools.Picker);
        Assert.False(picker.IsVisible);
        var card = Assert.IsType<ColorCardWindow>(tools.Card);
        var vm = Assert.IsType<ColorCardViewModel>(card.DataContext);
        await Until(() => vm.SaysHexCopied);
        Assert.Equal(("#1E287A", "rgb(30, 40, 122)", "hsl(233, 61%, 30%)"), (vm.Hex, vm.Rgb, vm.Hsl));
        Assert.Equal("#1E287A", await ClipboardText());
        Assert.True(vm.SaysHexCopied);
        card.Close();
    }

    [AvaloniaFact]
    public async Task Esc_or_a_right_click_closes_the_eyedropper_and_copies_nothing()
    {
        TestServices.EnsureInitialized("ko");
        var tools = new ScreenTools(new FakeScreen(), new ToolsLog(), AppHost.Get<CaptureOutput>());

        tools.StartColorPick(new PixelPoint(100, 100));
        var picker = tools.Picker!;
        await picker.Clipboard!.SetTextAsync("what was there before");
        picker.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(tools.Picker);
        Assert.False(picker.IsVisible);
        Assert.Null(tools.Card);
        Assert.Equal("what was there before", await ClipboardText());

        tools.StartColorPick(new PixelPoint(100, 100));
        picker = tools.Picker!;
        picker.MouseDown(new Point(30, 40), MouseButton.Right);
        // Still up while the button is down: closed now, the release would reach the app below and open its menu.
        Assert.True(picker.IsVisible);
        picker.MouseUp(new Point(30, 40), MouseButton.Right);
        Assert.False(picker.IsVisible);
        Assert.Null(tools.Picker);
        Assert.Null(tools.Card);
    }

    [AvaloniaFact]
    public void At_200_percent_the_click_picks_the_pixel_under_it()
    {
        TestServices.EnsureInitialized("en");
        // A 400 × 300 px screen right of the main one, at 200 %: the window over it is 200 × 150 DIPs.
        var bounds = new PixelRect(1920, 0, 400, 300);
        using var shot = ScreenGrab.Grab(new FakeScreen(), bounds, 2)!;
        var picker = new PickColorWindow();
        picker.SetRenderScaling(2); // the window's on that screen
        (PixelPoint At, Color Color)? picked = null;
        picker.Picked += (at, color) => picked = (at, color);

        picker.Open(shot, new PixelPoint(1920 + 100, 100));
        Assert.Equal((200, 150), ((int)picker.Width, (int)picker.Height));
        picker.MouseDown(new Point(30.6, 40.2), MouseButton.Left);

        // DIP (30.6, 40.2) is pixel (61, 80) of that screen: the desktop's (1981, 80).
        Assert.Equal(new PixelPoint(1981, 80), picked?.At);
        Assert.Equal(ScreenColor(1981, 80), picked?.Color);
        Assert.False(picker.IsVisible);
    }

    [AvaloniaFact]
    public void A_window_that_renders_at_another_scale_than_its_screen_said_still_covers_it_and_picks_the_pixel_under_the_click()
    {
        TestServices.EnsureInitialized("en");
        // The screen said 100 %, but the window over it renders at 200 % (a second screen of another scale).
        var bounds = new PixelRect(1920, 0, 400, 300);
        using var shot = ScreenGrab.Grab(new FakeScreen(), bounds, 1)!;
        var picker = new PickColorWindow();
        picker.SetRenderScaling(2);
        (PixelPoint At, Color Color)? picked = null;
        picker.Picked += (at, color) => picked = (at, color);

        picker.Open(shot, new PixelPoint(1920 + 100, 100));

        Assert.Equal((200, 150), ((int)picker.Width, (int)picker.Height)); // all of the 400 × 300 pixels, no more
        Assert.Equal(new PixelPoint(100, 100), picker.Lens.Pixel); // the magnifier under the cursor's pixel too
        picker.MouseMove(new Point(50.2, 20.7));
        Assert.Equal(new PixelPoint(100, 41), picker.Lens.Pixel);
        picker.MouseDown(new Point(30.6, 40.2), MouseButton.Left);
        Assert.Equal(new PixelPoint(1981, 80), picked?.At);
        Assert.Equal(ScreenColor(1981, 80), picked?.Color);
    }

    [AvaloniaFact]
    public void The_magnifier_shows_the_pixels_around_the_cursor_large_with_its_hex()
    {
        TestServices.EnsureInitialized("en");
        using var shot = ScreenGrab.Grab(new FakeScreen(), new PixelRect(0, 0, 400, 300), 1)!;
        var picker = new PickColorWindow();
        picker.Open(shot, new PixelPoint(200, 220));
        Dispatcher.UIThread.RunJobs();

        var lens = picker.Lens;
        Assert.Equal(new PixelPoint(200, 220), lens.Pixel);
        // Beside the cursor (above and to the right of it, where there is room), never under it.
        var at = lens.TranslatePoint(default, picker)!.Value;
        Assert.True(at.X > 200 && at.Y + lens.Bounds.Height < 220, $"{at}");

        using var frame = picker.CaptureRenderedFrame()!;
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "color-pick-magnifier.png")))
            frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var cell = Magnifier.Diameter / Magnifier.Cells;
        // The middle cell is the cursor's pixel; the cell left of it the pixel left of the cursor's.
        Assert.Equal(ScreenColor(200, 220), Pixel(frame, at + new Point(6.5 * cell, 6.5 * cell)));
        Assert.Equal(ScreenColor(199, 220), Pixel(frame, at + new Point(5.5 * cell, 6.5 * cell)));
        Assert.Equal(ScreenColor(200, 214), Pixel(frame, at + new Point(6.5 * cell, 0.6 * cell)));
        picker.Close();
    }

    [AvaloniaFact]
    public void A_black_or_missing_screen_picture_starts_nothing_and_says_why_in_the_log()
    {
        TestServices.EnsureInitialized("en");
        var log = new ToolsLog();

        new ScreenTools(new FakeScreen { Black = true }, log, AppHost.Get<CaptureOutput>()).StartColorPick(new PixelPoint(100, 100));
        new ScreenTools(new FakeScreen { None = true }, log, AppHost.Get<CaptureOutput>()).StartColorPick(new PixelPoint(100, 100));
        var throwing = new ScreenTools(new FakeScreen { Throws = true }, log, AppHost.Get<CaptureOutput>());
        throwing.StartColorPick(new PixelPoint(100, 100)); // never throws: the tray calls it from async void

        Assert.Null(throwing.Picker);
        Assert.Equal(3, log.Lines.Count);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("colour picker"));
    }

    [AvaloniaFact]
    public void A_cursor_off_every_screen_picks_from_the_main_screen()
    {
        TestServices.EnsureInitialized("en");
        var screen = new FakeScreen();
        var tools = new ScreenTools(screen, new ToolsLog(), AppHost.Get<CaptureOutput>());

        tools.StartColorPick(new PixelPoint(-5000, -5000));

        Assert.NotNull(tools.Picker);
        Assert.Equal(new PixelRect(0, 0, 1920, 1280), screen.Captured); // the headless platform's only screen
        tools.Picker!.Close();
    }

    [AvaloniaFact]
    public async Task One_eyedropper_and_one_card_at_a_time()
    {
        TestServices.EnsureInitialized("en");
        var screen = new FakeScreen();
        var tools = new ScreenTools(screen, new ToolsLog(), AppHost.Get<CaptureOutput>());

        tools.StartColorPick(new PixelPoint(100, 100));
        var first = tools.Picker;
        tools.StartColorPick(new PixelPoint(100, 100)); // the shortcut again while picking: nothing
        Assert.Same(first, tools.Picker);
        Assert.Equal(1, screen.Captures);

        first!.MouseDown(new Point(10, 10), MouseButton.Left);
        var card = tools.Card!;
        await Until(() => ((ColorCardViewModel)card.DataContext!).SaysHexCopied);
        tools.StartColorPick(new PixelPoint(100, 100));
        await Until(() => tools.Picker is not null);
        tools.Picker!.MouseDown(new Point(20, 20), MouseButton.Left);

        Assert.False(card.IsVisible); // the earlier card went
        Assert.NotSame(card, tools.Card);
        Assert.Equal("#14147A", ((ColorCardViewModel)tools.Card!.DataContext!).Hex);
        tools.Card.Close();
        Assert.Null(tools.Card);
    }

    [AvaloniaFact]
    public async Task A_card_still_up_is_off_the_screen_before_the_next_picture_is_taken()
    {
        TestServices.EnsureInitialized("en");
        ColorCardWindow? card = null;
        var screen = new FakeScreen { Watch = () => card?.IsVisible == true };
        var tools = new ScreenTools(screen, new ToolsLog(), AppHost.Get<CaptureOutput>());
        tools.StartColorPick(new PixelPoint(100, 100));
        tools.Picker!.MouseDown(new Point(10, 10), MouseButton.Left);
        card = tools.Card!;
        Assert.True(card.IsVisible);

        // Within its 6 seconds: the shortcut again, twice (the second press while the card is going does nothing).
        tools.StartColorPick(new PixelPoint(100, 100));
        tools.StartColorPick(new PixelPoint(100, 100));
        await Until(() => tools.Picker is not null);

        Assert.False(card.IsVisible);
        Assert.Equal([false, false], screen.WatchedAtCapture); // in neither picture, so its colours can't be picked
        tools.Picker!.Close();
    }

    [AvaloniaFact]
    public void The_card_sits_beside_the_picked_point_and_inside_the_screen()
    {
        var area = new PixelRect(0, 0, 1920, 1040);
        var card = new PixelSize(300, 160);

        Assert.Equal(new PixelPoint(116, 116), ColorCardWindow.Place(new PixelPoint(100, 100), card, area, 1));
        // Near the right and bottom edges it goes left of and above the point.
        Assert.Equal(new PixelPoint(1900 - 16 - 300, 1030 - 16 - 160), ColorCardWindow.Place(new PixelPoint(1900, 1030), card, area, 1));
        // At 200 % the gap is twice as many pixels.
        Assert.Equal(new PixelPoint(132, 132), ColorCardWindow.Place(new PixelPoint(100, 100), card, area, 2));
        // A point in a corner of a screen too small for the card on either side: the card stays on the screen.
        Assert.Equal(new PixelPoint(0, 0), ColorCardWindow.Place(new PixelPoint(150, 80), card, new PixelRect(0, 0, 320, 170), 1));
    }

    [AvaloniaFact]
    public async Task The_buttons_copy_each_code_and_say_copied_for_a_moment()
    {
        TestServices.EnsureInitialized("ko");
        var copied = new List<string>();
        var vm = new ColorCardViewModel(Color.FromRgb(255, 122, 26), text => { copied.Add(text); return Task.CompletedTask; }, null);

        await vm.CopyHexOnOpenAsync();
        Assert.True(vm.SaysHexCopied);
        var rgb = vm.CopyRgbCommand.ExecuteAsync(null);
        Assert.True(vm.RgbCopied);
        Assert.False(vm.HexCopied);
        Assert.False(vm.SaysHexCopied); // the clipboard holds the RGB code now
        await rgb;
        Assert.False(vm.RgbCopied);
        await vm.CopyHslCommand.ExecuteAsync(null);
        await vm.CopyHexCommand.ExecuteAsync(null);

        Assert.Equal(["#FF7A1A", "rgb(255, 122, 26)", "hsl(25, 100%, 55%)", "#FF7A1A"], copied);
        Assert.Equal(Color.FromRgb(255, 122, 26), ((ISolidColorBrush)vm.Swatch).Color);
    }

    [AvaloniaFact]
    public async Task A_busy_clipboard_says_nothing_on_the_card_and_tells_the_log()
    {
        TestServices.EnsureInitialized("ko");
        var log = new ListLogger();
        var vm = new ColorCardViewModel(Colors.Teal, _ => throw new COMException("The clipboard is open in another program"), log);

        await vm.CopyHexOnOpenAsync();
        await vm.CopyRgbCommand.ExecuteAsync(null);

        Assert.False(vm.SaysHexCopied);
        Assert.False(vm.RgbCopied);
        Assert.Equal(2, log.Lines.Count(l => l.Level == LogLevel.Warning));
    }

    [AvaloniaFact]
    public async Task Esc_closes_the_card()
    {
        TestServices.EnsureInitialized("ko");
        var tools = new ScreenTools(new FakeScreen(), new ToolsLog(), AppHost.Get<CaptureOutput>());
        tools.StartColorPick(new PixelPoint(100, 100));
        tools.Picker!.MouseDown(new Point(10, 10), MouseButton.Left);
        var card = tools.Card!;
        await Until(() => ((ColorCardViewModel)card.DataContext!).SaysHexCopied);
        using (var frame = card.CaptureRenderedFrame()!)
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "color-card-ko.png")))
            frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        card.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(card.IsVisible);
        Assert.Null(tools.Card);
    }

    internal static Color Pixel(Avalonia.Media.Imaging.Bitmap frame, Point at)
    {
        var bytes = new byte[4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }
        return frame.Format == PixelFormat.Rgba8888 ? Color.FromRgb(bytes[0], bytes[1], bytes[2]) : Color.FromRgb(bytes[2], bytes[1], bytes[0]);
    }

    /// <summary>
    /// A screen whose pixel at (x, y) of the desktop is <see cref="ScreenColor"/>. <see cref="Black"/> hands back a black
    /// picture (DRM video, the secure desktop), <see cref="None"/> no picture, <see cref="Throws"/> an error.
    /// </summary>
    internal sealed class FakeScreen : IPlatformServices
    {
        public bool Black { get; init; }
        public bool None { get; init; }
        public bool Throws { get; init; }
        public int Captures { get; private set; }
        public PixelRect? Captured { get; private set; }

        /// <summary>Asked at every capture; the answers go to <see cref="WatchedAtCapture"/>.</summary>
        public Func<bool>? Watch { get; init; }
        public List<bool> WatchedAtCapture { get; } = [];

        public ScreenCapture? CaptureScreen(int x, int y, int width, int height)
        {
            Captures++;
            if (Watch is not null)
                WatchedAtCapture.Add(Watch());
            Captured = new PixelRect(x, y, width, height);
            if (Throws)
                throw new InvalidOperationException("GDI is gone");
            if (None)
                return null;
            var bytes = new byte[width * height * 4];
            for (var row = 0; row < height && !Black; row++)
            for (var column = 0; column < width; column++)
            {
                var color = ScreenColor(x + column, y + row);
                var i = (row * width + column) * 4;
                (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]) = (color.B, color.G, color.R, 255);
            }
            return new ScreenCapture(width, height, bytes);
        }

        public bool IsFileManagerAt(int x, int y) => false;
        public string? ProcessNameAt(int x, int y) => null;
        public IReadOnlyList<string> GetFileManagerSelection() => [];
        public int SystemDragThreshold => 4;
        public bool PrefersReducedMotion => false;
        public void SetStartWithSystem(bool enabled, string executablePath) { }
        public void SetContextMenu(bool enabled, string executablePath, string label) { }
        public ModernContextMenuState GetModernContextMenuState(string executablePath) => ModernContextMenuState.Unsupported;
        public Task<ModernContextMenuResult> SetModernContextMenuAsync(bool enabled, string executablePath) => Task.FromResult(new ModernContextMenuResult(false));
        public void RevealInFileManager(string path) { }
        public void RaiseTopmost(nint windowHandle) { }
    }

    /// <summary>The screen tools' log, kept.</summary>
    internal sealed class ToolsLog : ILogger<ScreenTools>
    {
        private readonly ListLogger _lines = new();
        public List<(LogLevel Level, string Message)> Lines => _lines.Lines;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Log(logLevel, eventId, state, exception, formatter);
    }
}
