// The style choice in Settings → Appearance: it is saved, and it switches the preview donut and the accent colour card.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class ThemeStyleTests
{
    [AvaloniaFact]
    public void Choosing_a_style_saves_it_and_switches_the_preview()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.Theme.Style;
        try
        {
            store.Settings.Theme.Style = ThemeStyle.PeachGlass;
            var page = new ThemePageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>());
            var changed = new List<string?>();
            page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.True(page.IsGlass);

            page.Style = page.Styles.Single(s => s.Value == ThemeStyle.Classic);

            Assert.Equal(ThemeStyle.Classic, store.Settings.Theme.Style);
            Assert.False(page.IsGlass);
            Assert.Contains(nameof(page.IsGlass), changed);
        }
        finally
        {
            store.Settings.Theme.Style = before;
        }
    }
}
