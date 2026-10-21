// What this app calls itself. Everything that must not collide with the original Filee on the same PC (data folders,
// the single-instance mutex, registry entries) is named from Folder. The names of 1.1.0, when the app was called
// Filee Plus, are kept only to move its data over (LegacyDataMigration) and to remove what it registered.

namespace Filee.Core;

/// <summary>
/// The names Bingling goes by, kept in one place. <see cref="Name"/> is only the name Windows shows; the app's own
/// windows show the translated <c>app.name</c> (빙글링 in Korean).
/// </summary>
public static class AppIdentity
{
    /// <summary>Display name for the places Windows reads: startup entry, shortcuts, message boxes, package lists.</summary>
    public const string Name = "Bingling";

    /// <summary>The same without spaces, for folder, mutex, registry and file names.</summary>
    public const string Folder = "Bingling";

    /// <summary>Start of the installer and checksum file names: <c>Bingling-1.2.0-win-Setup.exe</c>.</summary>
    public const string FilePrefix = "Bingling";

    /// <summary>What 1.1.0 called itself; only used to remove the startup entry and Send To shortcut it left behind.</summary>
    public const string LegacyName = "Filee Plus";

    /// <summary>The folder name of 1.1.0 (%APPDATA%\FileePlus, %LocalAppData%\FileePlus); only read to move its data over.</summary>
    public const string LegacyFolder = "FileePlus";

    /// <summary>The upstream release this fork branched from, shown on the About page.</summary>
    public const string BasedOn = "Filee 1.12.0";
}
