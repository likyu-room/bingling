// System tray icon: open settings, capture the screen, pick a colour, pause gestures, install a new version, quit.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Filee.App.Services.Triggers;
using Filee.Core;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Services;

public sealed class TrayService(UserDataStore store, ILocalizer loc, WindowService windows, UpdateService updates,
    IScreenTools screen, TriggerService triggers)
{
    private TrayIcon? _tray;
    private NativeMenuItem? _open, _capture, _pickColor, _pause, _quit, _update;

    public void Create(Application app)
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://Bingling/Assets/Icons/filee.ico"));
        _open = new NativeMenuItem();
        _open.Click += (_, _) => windows.ShowMain();
        _capture = new NativeMenuItem();
        _capture.Click += async (_, _) => await StartAfterMenuClosesAsync(screen.StartCapture);
        _pickColor = new NativeMenuItem();
        _pickColor.Click += async (_, _) => await StartAfterMenuClosesAsync(screen.StartColorPick);
        _pause = new NativeMenuItem();
        _pause.Click += (_, _) =>
        {
            store.Settings.Paused = !store.Settings.Paused;
            store.SaveSettings();
        };
        _quit = new NativeMenuItem();
        _quit.Click += (_, _) => (app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        _update = new NativeMenuItem();
        _update.Click += async (_, _) => await updates.UpdateAsync();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            // Set before the icon is attached: on macOS Avalonia passes the tooltip to native code, which can't take null.
            ToolTipText = loc["app.name"],
            Menu = new NativeMenu { Items = { _open, _pause, new NativeMenuItemSeparator(), _quit } },
            IsVisible = true,
        };
        // The screen tools are Windows-only for now.
        if (OperatingSystem.IsWindows())
        {
            _tray.Menu.Items.Insert(1, _capture);
            _tray.Menu.Items.Insert(2, _pickColor);
        }
        _tray.Clicked += (_, _) => windows.ShowMain();
        TrayIcon.SetIcons(app, [_tray]);

        Refresh();
        loc.LanguageChanged += (_, _) => Refresh();
        store.SettingsChanged += (_, _) => Refresh();
        updates.PropertyChanged += (_, e) =>
        {
            if (UpdateTexts.Affects(e.PropertyName))
                Refresh();
        };
    }

    private void Refresh()
    {
        if (_tray?.Menu is not { } menu)
            return;
        // "Update to x.y.z" sits at the top of the menu while a newer release is out ("Downloading… 42%" meanwhile).
        if (updates.LatestVersion is { } latest)
        {
            _update!.Header = updates.Step is UpdateStep.Downloading or UpdateStep.Installing
                ? UpdateTexts.Short(updates, loc)
                : loc.Format("tray.update", latest);
            if (!menu.Items.Contains(_update))
                menu.Items.Insert(0, _update);
        }
        else
        {
            menu.Items.Remove(_update!);
        }
        _open!.Header = loc["tray.open"];
        _capture!.Header = loc["tray.capture"];
        _pickColor!.Header = loc["tray.pick_color"];
        _pause!.Header = store.Settings.Paused ? loc["tray.resume"] : loc["tray.pause"];
        _quit!.Header = loc["tray.quit"];
        var drag = store.Settings.Triggers.FirstOrDefault(t => t.Enabled && t.Kind == TriggerKind.Drag);
        _tray.ToolTipText = store.Settings.Paused
            ? loc["triggers.paused"]
            : loc.Format("tray.tooltip", drag is null ? "—" : GestureText.Modifiers(drag.Modifiers));
    }

    /// <summary>
    /// Starts a screen tool for a tray menu item: after a short wait, so the menu has gone and isn't in the picture,
    /// and at the last cursor position the input hook saw.
    /// </summary>
    internal async Task StartAfterMenuClosesAsync(Action<PixelPoint> start)
    {
        await Task.Delay(150);
        var (x, y) = triggers.LastCursor;
        start(new PixelPoint(x, y));
    }

    public void Dispose() => _tray?.Dispose();
}
