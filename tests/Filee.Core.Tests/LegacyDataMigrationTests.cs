// Bingling was called Filee Plus up to 1.1.0 and kept its settings and engines in FileePlus folders: the first start
// moves them over, never touches the original Filee's folders and never stops the app from starting.

using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class LegacyDataMigrationTests
{
    [Fact]
    public void Settings_and_engines_of_Filee_Plus_move_to_Bingling()
    {
        using var roaming = new TempDir();
        using var local = new TempDir();
        Write(roaming.Path, "FileePlus", "settings.json", "{\"language\":\"ko\"}");
        Write(roaming.Path, "FileePlus", Path.Combine("logs", "filee-20261021.log"), "line");
        Write(local.Path, "FileePlus", Path.Combine("engines", "ffmpeg", ".filee-component"), "abc");

        var report = LegacyDataMigration.Run(roaming.Path, local.Path);

        Assert.Equal(2, report.Count);
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "FileePlus")));
        Assert.False(Directory.Exists(Path.Combine(local.Path, "FileePlus")));
        Assert.Equal("{\"language\":\"ko\"}", File.ReadAllText(Path.Combine(roaming.Path, "Bingling", "settings.json")));
        Assert.True(File.Exists(Path.Combine(roaming.Path, "Bingling", "logs", "filee-20261021.log")));
        Assert.Equal("abc", File.ReadAllText(Path.Combine(local.Path, "Bingling", "engines", "ffmpeg", ".filee-component")));
    }

    [Fact]
    public void Folders_Bingling_has_already_are_left_as_they_are()
    {
        using var roaming = new TempDir();
        Write(roaming.Path, "FileePlus", "settings.json", "old");
        Write(roaming.Path, "Bingling", "settings.json", "new");

        Assert.Empty(LegacyDataMigration.Run(roaming.Path));

        Assert.Equal("new", File.ReadAllText(Path.Combine(roaming.Path, "Bingling", "settings.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(roaming.Path, "FileePlus", "settings.json")));
    }

    [Fact]
    public void The_original_Filee_keeps_its_folder()
    {
        using var roaming = new TempDir();
        Write(roaming.Path, "Filee", "settings.json", "original");

        Assert.Empty(LegacyDataMigration.Run(roaming.Path));

        Assert.Equal("original", File.ReadAllText(Path.Combine(roaming.Path, "Filee", "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "Bingling")));
    }

    [Fact]
    public void A_move_that_fails_is_reported_and_leaves_the_old_folder_alone()
    {
        using var roaming = new TempDir();
        Write(roaming.Path, "FileePlus", "settings.json", "old");
        File.WriteAllText(Path.Combine(roaming.Path, "Bingling"), "a file where the folder would go");

        var line = Assert.Single(LegacyDataMigration.Run(roaming.Path));

        Assert.StartsWith("Could not move", line);
        Assert.Equal("old", File.ReadAllText(Path.Combine(roaming.Path, "FileePlus", "settings.json")));
    }

    private static void Write(string root, string folder, string file, string content)
    {
        var path = Path.Combine(root, folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
