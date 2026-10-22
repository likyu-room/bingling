// Folders dragged onto the donut: the folders donut, whose slices pack each folder into an archive or all of them into one.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Tests;

public class FolderDonutTests
{
    /// <summary>A folder of its own with one file in it.</summary>
    private static string Folder(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "filee-app-tests", "folders-" + Guid.NewGuid().ToString("N")[..8], name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "a.txt"), "hello");
        return path;
    }

    [AvaloniaFact]
    public void Folders_open_a_donut_that_packs_them()
    {
        TestServices.EnsureInitialized("ko");
        var vm = AppHost.Get<RadialViewModel>();

        vm.Load([Folder("사진"), Folder("v1.2")], clickMode: true);

        Assert.Equal(["to-zip", "to-7z", "zip-all"], vm.Items.Select(i => i.Id));
        // 7Z needs 7-Zip, which a test run may not have; ZIP is built in.
        Assert.All(vm.Items.Where(i => i.Id != "to-7z"), i => Assert.True(i.IsEnabled, i.DisabledReason));
        Assert.Equal("폴더 2개", vm.CenterTitle); // a folder's size is not added up
    }

    [Fact]
    public void A_dragged_folder_counts_as_something_to_drop()
    {
        Assert.True(DraggedFiles.Exists(Folder("drag")));
        Assert.False(DraggedFiles.Exists(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))));
    }
}
