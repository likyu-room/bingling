// Bingling's names: what Windows shows and what its folders, lock and registry entries are called. The names of 1.1.0
// (Filee Plus) are only kept to move its data over and clean up after it; the original Filee's are never used.

namespace Filee.Core.Tests;

public class AppIdentityTests
{
    [Fact]
    public void The_name_Windows_shows_is_Bingling_and_installer_files_start_with_it()
    {
        Assert.Equal("Bingling", AppIdentity.Name);
        Assert.Equal("Bingling", AppIdentity.FilePrefix);
        Assert.Equal("Filee Plus", AppIdentity.LegacyName);
    }

    [Fact]
    public void Folders_are_named_Bingling_and_differ_from_Filee_Plus_and_the_original_Filee()
    {
        Assert.Equal("Bingling", AppIdentity.Folder);
        Assert.Equal("FileePlus", AppIdentity.LegacyFolder);
        Assert.NotEqual("Filee", AppIdentity.Folder);
        Assert.NotEqual("Filee", AppIdentity.LegacyFolder);
        Assert.DoesNotContain(' ', AppIdentity.Folder);
    }
}
