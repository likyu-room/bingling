// Bingling was called Filee Plus up to 1.1.0 and kept its settings in %APPDATA%\FileePlus and its downloaded engines
// in %LocalAppData%\FileePlus\engines. The first start renames those folders to Bingling, before anything (the log
// included) creates the new ones. Renaming in place is instant even for engines of several hundred MB. When that fails
// (a file in use), the app starts without the old data, and every later start moves over what Bingling still lacks.
// The original Filee's folders (Filee) are a different name and never touched.

namespace Filee.Core.Settings;

/// <summary>Moves the data folders of Filee Plus (<see cref="AppIdentity.LegacyFolder"/>) to Bingling's.</summary>
public static class LegacyDataMigration
{
    /// <summary>
    /// In each of <paramref name="roots"/> (%APPDATA% and %LocalAppData%), renames the FileePlus folder to Bingling when
    /// only the old one exists. When both exist (an earlier move failed), moves the files and folders Bingling doesn't
    /// have yet, never replacing one it has, and deletes the old folder once it is empty. A failure is reported, never
    /// thrown: the app then starts without the old data and tries again at the next start.
    /// </summary>
    /// <returns>One line per file or folder that moved or could not be moved, for the log.</returns>
    public static IReadOnlyList<string> Run(params IEnumerable<string> roots)
    {
        var report = new List<string>();
        foreach (var root in roots)
        {
            var from = Path.Combine(root, AppIdentity.LegacyFolder);
            var to = Path.Combine(root, AppIdentity.Folder);
            if (!Directory.Exists(from))
                continue;
            if (!Directory.Exists(to))
            {
                Move(from, to, report);
                continue;
            }
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(from))
                {
                    var target = Path.Combine(to, Path.GetFileName(entry));
                    if (!Path.Exists(target))
                        Move(entry, target, report);
                }
                if (Directory.GetFileSystemEntries(from).Length == 0)
                    Directory.Delete(from);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Add($"Could not move {from} to {to}: {ex.Message}");
            }
        }
        return report;
    }

    private static void Move(string from, string to, List<string> report)
    {
        try
        {
            if (Directory.Exists(from))
                Directory.Move(from, to);
            else
                File.Move(from, to);
            report.Add($"Moved {from} to {to}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Add($"Could not move {from} to {to}: {ex.Message}");
        }
    }

    /// <summary>
    /// <see cref="Run"/> for the current user's folders. A copy with its own data folder
    /// (<see cref="UserDataStore.DirectoryVariable"/>) leaves them alone.
    /// </summary>
    public static IReadOnlyList<string> RunForCurrentUser() =>
        UserDataStore.OverrideDirectory is not null
            ? []
            : Run(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
