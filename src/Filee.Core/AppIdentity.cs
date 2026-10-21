// What this app calls itself. Everything that must not collide with the original Filee on the same PC (data folders,
// the single-instance mutex, registry entries) is named from Folder, which never changes with the display name.

namespace Filee.Core;

/// <summary>
/// The names Bingling goes by, kept in one place. <see cref="Name"/> is only the name Windows shows; the app's own
/// windows show the translated <c>app.name</c> (빙글링 in Korean).
/// </summary>
public static class AppIdentity
{
    /// <summary>Display name for the places Windows reads: startup entry, shortcuts, message boxes, package lists.</summary>
    public const string Name = "Bingling";

    /// <summary>The same without spaces, for folder, mutex, registry and file names. Kept from 1.1.0 so settings carry over.</summary>
    public const string Folder = "FileePlus";

    /// <summary>Start of the installer and checksum file names: <c>Bingling-1.2.0-win-Setup.exe</c>.</summary>
    public const string FilePrefix = "Bingling";

    /// <summary>What 1.1.0 called itself; only used to remove the startup entry and Send To shortcut it left behind.</summary>
    public const string LegacyName = "Filee Plus";

    /// <summary>The upstream release this fork branched from, shown on the About page.</summary>
    public const string BasedOn = "Filee 1.12.0";
}
