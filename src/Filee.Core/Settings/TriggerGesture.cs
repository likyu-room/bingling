// Describes what the user has to press to open the donut toolbar. Deliberately flexible:
// any modifier combination, any mouse button, optional extra key, drag / hold / key-only.

using System.Text.Json.Serialization;
using Filee.Core.Presets;

namespace Filee.Core.Settings;

/// <summary>Modifier keys. Left/right variants are treated the same.</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    /// <summary>Windows key on Windows, Command on macOS.</summary>
    Meta = 8,
}

/// <summary>Mouse buttons a gesture can use.</summary>
public enum TriggerMouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>What physical action completes the gesture.</summary>
public enum TriggerKind
{
    /// <summary>Hold the modifiers and drag with the mouse button past the threshold. Used for file drags.</summary>
    Drag,
    /// <summary>Hold the modifiers and keep the mouse button pressed without moving for <see cref="TriggerGesture.HoldMilliseconds"/>.</summary>
    Hold,
    /// <summary>Press the modifiers + <see cref="TriggerGesture.Key"/> (no mouse). Uses the file manager selection.</summary>
    KeyChord,
}

/// <summary>Where the gesture is active.</summary>
public enum TriggerScope
{
    /// <summary>
    /// Only when the cursor is over a file manager (Explorer, Finder; on Windows also Files, Directory Opus and other
    /// file managers) or the desktop.
    /// </summary>
    FileManager,
    /// <summary>Everywhere, except excluded applications.</summary>
    Anywhere,
}

/// <summary>What a gesture does when it fires. Added after the others so the numbers of the older values stay.</summary>
public enum TriggerAction
{
    /// <summary>Opens the donut toolbar.</summary>
    Donut,
    /// <summary>Takes a screenshot (the capture window).</summary>
    Capture,
    /// <summary>Picks a colour from the screen.</summary>
    PickColor,
}

/// <summary>
/// A user-configurable gesture. The enums are read leniently (a value this version doesn't know becomes the first one),
/// so a gesture from a newer Bingling never resets the whole settings file.
/// </summary>
public sealed class TriggerGesture
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(LenientEnumJsonConverter<TriggerKind>))]
    public TriggerKind Kind { get; set; } = TriggerKind.Drag;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Ctrl;

    [JsonConverter(typeof(LenientEnumJsonConverter<TriggerMouseButton>))]
    public TriggerMouseButton Button { get; set; } = TriggerMouseButton.Left;

    /// <summary>
    /// What the gesture does. A Bingling from before this knows only the donut, so it opens the donut with any of them;
    /// only <see cref="TriggerAction.Donut"/> gestures count as "the" shortcut of the donut (Thunar).
    /// </summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<TriggerAction>))]
    public TriggerAction Action { get; set; } = TriggerAction.Donut;

    /// <summary>
    /// Extra non-modifier key (SharpHook <c>KeyCode</c> name without the "Vc" prefix, e.g. <c>"Space"</c>).
    /// Required for <see cref="TriggerKind.KeyChord"/>, optional otherwise.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>Distance in logical pixels the mouse must travel before a drag counts. 0 = system default.</summary>
    public int DragThreshold { get; set; }

    /// <summary>Press duration for <see cref="TriggerKind.Hold"/>.</summary>
    public int HoldMilliseconds { get; set; } = 450;

    [JsonConverter(typeof(LenientEnumJsonConverter<TriggerScope>))]
    public TriggerScope Scope { get; set; } = TriggerScope.FileManager;

    public TriggerGesture Clone() => (TriggerGesture)MemberwiseClone();

    /// <summary>The shortcuts that capture the screen and pick a colour, on everywhere.</summary>
    public static List<TriggerGesture> ScreenShortcuts() =>
    [
        new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Button = TriggerMouseButton.None, Key = "S", Scope = TriggerScope.Anywhere, Action = TriggerAction.Capture },
        new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Button = TriggerMouseButton.None, Key = "C", Scope = TriggerScope.Anywhere, Action = TriggerAction.PickColor },
    ];

    /// <summary>Default gestures for the current OS.</summary>
    public static List<TriggerGesture> Defaults() => Defaults(OperatingSystem.IsWindows());

    /// <inheritdoc cref="Defaults()"/>
    /// <param name="windows">Whether the OS is Windows (a test's choice): only there are the screen tools.</param>
    internal static List<TriggerGesture> Defaults(bool windows)
    {
        // On macOS Ctrl+click means right-click, and Option already means "copy" in Finder.
        var dragModifier = OperatingSystem.IsMacOS() ? ModifierKeys.Alt : ModifierKeys.Ctrl;
        List<TriggerGesture> defaults =
        [
            new() { Kind = TriggerKind.Drag, Modifiers = dragModifier, Button = TriggerMouseButton.Left },
            new() { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Button = TriggerMouseButton.None, Key = "Space" },
        ];
        // Windows also lists Shift + drag, last and switched off: one tick in Settings turns it on, and the hint and the
        // tray tooltip show the first enabled drag, which stays Ctrl. macOS and Linux keep their single drag default.
        if (!windows)
            return defaults;
        defaults.Add(new() { Kind = TriggerKind.Drag, Modifiers = ModifierKeys.Shift, Button = TriggerMouseButton.Left, Enabled = false });
        // After the donut's own, so their places in the list stay. Windows only: elsewhere they would do nothing.
        defaults.AddRange(ScreenShortcuts());
        return defaults;
    }
}
