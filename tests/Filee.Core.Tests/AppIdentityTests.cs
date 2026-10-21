// The display name changed with the rename to Bingling; everything the settings of 1.1.0 hang on did not.

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
    public void The_folder_name_stays_so_an_update_finds_the_settings_engines_and_lock_of_1_1_0()
    {
        Assert.Equal("FileePlus", AppIdentity.Folder);
        Assert.DoesNotContain(' ', AppIdentity.Folder);
    }
}
