// The local files in a drag. Avalonia reads file URLs (what Explorer, Finder and Linux file managers offer); on macOS
// some other file managers offer the older NSFilenamesPboardType array of paths instead (QSpace Pro, issue #160),
// which Avalonia passes through as a platform format of its own.

using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.Core.Platform;
using Filee.Platform.MacOS;

namespace Filee.App.Services;

internal static class DraggedFiles
{
    /// <summary>Whether the drag offers files in a form Filee reads (they may still turn out to be folders).</summary>
    public static bool Offered(IDataTransfer data) =>
        data.Contains(DataFormat.File) || data.Items.Any(item => item.Formats.Any(IsFilenames));

    /// <summary>
    /// Local files and folders in the drag (virtual items are ignored): folders open the folders donut. A shortcut
    /// counts as what it points to.
    /// </summary>
    public static IReadOnlyList<string> Local(IDataTransfer data) =>
        Local(data, Exists, AppHost.Get<IPlatformServices>().ResolveShortcuts);

    /// <summary>A file or a folder at <paramref name="path"/>: something the donut can take.</summary>
    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    internal static IReadOnlyList<string> Local(IDataTransfer data, Func<string, bool> fileExists,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? resolveShortcuts = null)
    {
        IReadOnlyList<string> Usable(IReadOnlyList<string> paths) =>
            (resolveShortcuts?.Invoke(paths) ?? paths).Where(fileExists).Distinct(FileSystemPaths.Comparer).ToList();

        var files = Usable((data.TryGetFiles() ?? []).Select(item => item.TryGetLocalPath()).OfType<string>().ToList());
        return files.Count > 0 ? files : Usable(Filenames(data).ToList());
    }

    /// <summary>The formats a drag offers, for the log when Filee finds no files in it.</summary>
    public static string Describe(IDataTransfer data) =>
        data.Formats.Count == 0 ? "nothing" : string.Join(", ", data.Formats.Select(format => format.Identifier).Distinct());

    private static IEnumerable<string> Filenames(IDataTransfer data) =>
        data.Items.SelectMany(item => item.Formats.Where(IsFilenames).SelectMany(format => item.TryGetRaw(format) switch
        {
            byte[] bytes => PasteboardPaths.FromPropertyList(bytes),
            string text => PasteboardPaths.FromPropertyList(text),
            _ => [],
        }));

    private static bool IsFilenames(DataFormat format) =>
        format.Kind == DataFormatKind.Platform && PasteboardPaths.IsFilenamesType(format.Identifier);
}
