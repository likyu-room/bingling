// What the home page tells a new user to press: the first drag gesture that is on. Shift + drag is in the default list
// but switched off, so the hint names Ctrl. The tray tooltip picks its gesture with the same query.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class TriggerHintTests
{
    [AvaloniaFact]
    public void The_home_hint_names_ctrl_while_the_default_shift_drag_is_off()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows defaults");
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.Triggers;
        try
        {
            store.Settings.Triggers = TriggerGesture.Defaults();
            Assert.Equal(HintFor("Ctrl"), Hint(store));

            // Turned on and Ctrl turned off, Shift is the first drag that is on.
            store.Settings.Triggers[0].Enabled = false;
            store.Settings.Triggers[2].Enabled = true;
            Assert.Equal(HintFor("Shift"), Hint(store));
        }
        finally
        {
            store.Settings.Triggers = before;
        }
    }

    private static string Hint(UserDataStore store)
    {
        using var page = new HomePageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>());
        return page.DropHint;
    }

    private static string HintFor(string keys) => AppHost.Get<ILocalizer>().Format("home.drop_hint", keys);
}
