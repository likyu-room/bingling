// What this fork calls itself. Everything that must not collide with the original Filee on the same PC (data folders,
// the single-instance mutex, registry entries, shortcuts) is named from these, so both can be installed side by side.

namespace Filee.Core;

/// <summary>The name Filee Plus goes by, kept in one place so no data, menu or mutex is shared with the original Filee.</summary>
public static class AppIdentity
{
    /// <summary>Display name: window and message-box titles, the tray tooltip, the start-up entry, shortcuts.</summary>
    public const string Name = "Filee Plus";

    /// <summary>The same without spaces, for folder, mutex, registry and file names.</summary>
    public const string Folder = "FileePlus";

    /// <summary>The upstream release this fork branched from, shown on the About page.</summary>
    public const string BasedOn = "Filee 1.12.0";
}
