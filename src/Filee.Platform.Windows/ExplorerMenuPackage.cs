// The sparse ("packaged with external location") MSIX that puts "Convert with Bingling" into the top-level Windows 11
// File Explorer context menu. The package holds only AppxManifest.xml and a logo; the COM server it declares is the
// native BinglingExplorerMenu.dll in the install folder (built from src/Filee.ExplorerMenu).

using System.Globalization;
using System.Xml.Linq;
using Filee.Core;

namespace Filee.Platform.Windows;

/// <summary>
/// Identity, file names and manifest of the Explorer context-menu package. build/tools/make-explorer-package.cs packs
/// <see cref="CreateManifest"/> at build time; <see cref="ExplorerMenuRegistration"/> registers the result.
/// </summary>
public static class ExplorerMenuPackage
{
    /// <summary>Package name (the <c>Identity/@Name</c>); also used to find and remove the registration.</summary>
    public const string PackageName = AppIdentity.Folder + ".ExplorerMenu";

    /// <summary>
    /// Publisher of the unsigned package. Windows 11 only registers unsigned packages whose publisher contains this
    /// special OID, so they can never collide with (or spoof) a signed package. CN differs from the publisher the
    /// original Filee and Filee Plus used, so the family (and the registration) is Bingling's own.
    /// </summary>
    public const string Publisher = "CN=Bingling, OID.2.25.311729368913984317654407730594956997722=1";

    /// <summary>
    /// Package family name = name + "_" + a hash of <see cref="Publisher"/> (checked by a test against
    /// <c>PackageFamilyNameFromId</c>). Used to look the registration up without PowerShell.
    /// </summary>
    public const string FamilyName = PackageName + "_sryjq816xeb38";

    /// <summary>
    /// Version of the manifest, deliberately independent of the app version: registering an unsigned package with a
    /// COM server needs administrator rights, so the package must stay valid across app updates (the DLL itself is
    /// loaded from the install folder and is updated with the app). Bump it only when the manifest changes; the app
    /// then offers to register again.
    /// </summary>
    public static readonly Version ManifestVersion = new(1, 0, 0, 0);

    /// <summary>
    /// CLSID of the IExplorerCommand in BinglingExplorerMenu.dll (kClsidCommand in FileeExplorerMenu.c). New for
    /// Bingling: neither the original Filee's nor Filee Plus's, so no stale registration of theirs can load this DLL.
    /// </summary>
    public static readonly Guid ClassId = new("4E812CE3-362A-4823-BB60-46148A883B44");

    /// <summary>The native COM server next to Bingling.exe.</summary>
    public const string DllFileName = "BinglingExplorerMenu.dll";

    /// <summary>The package file next to Bingling.exe.</summary>
    public const string PackageFileName = "BinglingExplorerMenu.msix";

    /// <summary>
    /// File in the data folder (%APPDATA%\Bingling, TITLE_FILE in FileeExplorerMenu.c) whose first line is the menu title
    /// in the UI language. It exists while the Explorer menu setting is on; without it the DLL hides the entry.
    /// </summary>
    public const string TitleFileName = "explorer-menu.txt";

    /// <summary>Processor architecture of the DLL (it is loaded into explorer.exe, so it must match Explorer).</summary>
    public const string Architecture = "x64";

    /// <summary>Windows 11 (build 22000) is the first version with the new context menu and unsigned packages.</summary>
    public const int MinimumBuild = 22000;

    /// <summary>Path of the logo inside the package (shown in package lists only; the menu uses Bingling.exe's icon).</summary>
    public const string LogoPath = @"Assets\Logo.png";

    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace Desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    /// <summary>
    /// Builds AppxManifest.xml. Paths in it (Bingling.exe, the DLL) are relative to the external location, i.e. the
    /// install folder given at registration.
    /// </summary>
    public static string CreateManifest()
    {
        var clsid = ClassId.ToString("D").ToUpperInvariant();
        var package = new XElement(Foundation + "Package",
            new XAttribute("xmlns", Foundation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "uap", Uap),
            new XAttribute(XNamespace.Xmlns + "uap10", Uap10),
            new XAttribute(XNamespace.Xmlns + "desktop4", Desktop4),
            new XAttribute(XNamespace.Xmlns + "desktop5", Desktop5),
            new XAttribute(XNamespace.Xmlns + "com", Com),
            new XAttribute(XNamespace.Xmlns + "rescap", Rescap),
            new XAttribute("IgnorableNamespaces", "uap uap10 desktop4 desktop5 com rescap"),
            new XElement(Foundation + "Identity",
                new XAttribute("Name", PackageName),
                new XAttribute("Publisher", Publisher),
                new XAttribute("Version", ManifestVersion.ToString(4)),
                new XAttribute("ProcessorArchitecture", Architecture)),
            new XElement(Foundation + "Properties",
                new XElement(Foundation + "DisplayName", AppIdentity.Name),
                new XElement(Foundation + "PublisherDisplayName", "Beomsuk Kim (LikeU)"),
                new XElement(Foundation + "Logo", LogoPath),
                new XElement(Uap10 + "AllowExternalContent", "true")),
            new XElement(Foundation + "Resources",
                new XElement(Foundation + "Resource", new XAttribute("Language", "en-us"))),
            new XElement(Foundation + "Dependencies",
                new XElement(Foundation + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Desktop"),
                    new XAttribute("MinVersion", "10.0." + MinimumBuild.ToString(CultureInfo.InvariantCulture) + ".0"),
                    new XAttribute("MaxVersionTested", "10.0.26100.0"))),
            new XElement(Foundation + "Capabilities",
                new XElement(Rescap + "Capability", new XAttribute("Name", "runFullTrust")),
                new XElement(Rescap + "Capability", new XAttribute("Name", "unvirtualizedResources"))),
            new XElement(Foundation + "Applications",
                new XElement(Foundation + "Application",
                    new XAttribute("Id", "Bingling"),
                    new XAttribute("Executable", "Bingling.exe"),
                    new XAttribute(Uap10 + "TrustLevel", "mediumIL"),
                    new XAttribute(Uap10 + "RuntimeBehavior", "win32App"),
                    // AppListEntry="none": the package only carries the shell extension, it adds no Start menu entry.
                    new XElement(Uap + "VisualElements",
                        new XAttribute("AppListEntry", "none"),
                        new XAttribute("DisplayName", AppIdentity.Name),
                        new XAttribute("Description", AppIdentity.Name + " File Explorer integration"),
                        new XAttribute("BackgroundColor", "transparent"),
                        new XAttribute("Square150x150Logo", LogoPath),
                        new XAttribute("Square44x44Logo", LogoPath)),
                    new XElement(Foundation + "Extensions",
                        // Type="*": files only (folders would be "Directory"); the DLL additionally hides the
                        // command for virtual items (e.g. inside a ZIP folder) and mixed selections.
                        new XElement(Desktop4 + "Extension",
                            new XAttribute("Category", "windows.fileExplorerContextMenus"),
                            new XElement(Desktop4 + "FileExplorerContextMenus",
                                new XElement(Desktop5 + "ItemType",
                                    new XAttribute("Type", "*"),
                                    new XElement(Desktop5 + "Verb",
                                        new XAttribute("Id", "ConvertWithBingling"),
                                        new XAttribute("Clsid", clsid))))),
                        new XElement(Com + "Extension",
                            new XAttribute("Category", "windows.comServer"),
                            new XElement(Com + "ComServer",
                                new XElement(Com + "SurrogateServer",
                                    new XAttribute("DisplayName", AppIdentity.Name + " context menu"),
                                    new XElement(Com + "Class",
                                        new XAttribute("Id", clsid),
                                        new XAttribute("Path", DllFileName),
                                        new XAttribute("ThreadingModel", "STA")))))))));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), package);
        return document.Declaration + Environment.NewLine + document.Root;
    }
}
