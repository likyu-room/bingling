// The style choice in Settings → Appearance: it is saved, and it switches the preview donut and the accent colour card.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

    /// <summary>White on the Peach Glass orange is only ~2.6:1, so text on the accent turns dark brown there; Classic keeps the luminance rule.</summary>
    [AvaloniaFact]
    public void Text_on_the_accent_is_dark_brown_under_peach_glass_only()
    {
        TestServices.EnsureInitialized("en");
        var theme = AppHost.Get<UserDataStore>().Settings.Theme.Clone();
        var themes = AppHost.Get<ThemeService>();
        try
        {
            theme.Style = ThemeStyle.PeachGlass;
            theme.Accent = "#1D9E75"; // ignored under Peach Glass
            themes.Apply(theme, "en");
            Assert.Equal(Color.Parse("#3B2416"), OnAccent());

            theme.Style = ThemeStyle.Classic;
            themes.Apply(theme, "en");
            Assert.Equal(Colors.White, OnAccent());
            theme.Accent = "#F2E394"; // a light accent keeps its dark text
            themes.Apply(theme, "en");
            Assert.Equal(Color.Parse("#1F1E1D"), OnAccent());
        }
        finally
        {
            themes.Apply(AppHost.Get<UserDataStore>().Settings.Theme, "en");
        }

        static Color OnAccent() => ((SolidColorBrush)Application.Current!.Resources["OnAccentBrush"]!).Color;
    }
}
