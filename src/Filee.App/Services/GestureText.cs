// Human readable gesture text such as "Ctrl + Alt".

using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Services;

public static class GestureText
{
    public static string Modifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add(OperatingSystem.IsMacOS() ? "⌃" : "Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(OperatingSystem.IsMacOS() ? "⌥ Option" : "Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Meta)) parts.Add(OperatingSystem.IsMacOS() ? "⌘" : "Win");
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    /// <summary>
    /// "Ctrl + Alt + Space", and "Ctrl + Alt + S · Capture the screen" for a key shortcut that does something else than open
    /// the donut (a drag or hold always opens it, whatever its settings say).
    /// </summary>
    public static string Describe(TriggerGesture gesture, ILocalizer loc)
    {
        var mods = Modifiers(gesture.Modifiers);
        if (gesture.Kind != TriggerKind.KeyChord)
            return $"{mods} + {gesture.Button}";
        var keys = gesture.Modifiers == ModifierKeys.None ? gesture.Key ?? "—" : $"{mods} + {gesture.Key ?? "?"}";
        return gesture.Action == TriggerAction.Donut ? keys : $"{keys} · {loc[$"triggers.action.{gesture.Action}"]}";
    }
}
