// The extension chips of a mixed-selection profile (Settings -> Toolbar): a checked chip under the pointer is the one
// state whose colour is not the accent itself, so it has to be checked on its own.

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.App.Views.Pages;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class ExtensionChipContrastTests
{
    [AvaloniaTheory]
    [InlineData(ThemeStyle.Classic, ThemeMode.Light)]
    [InlineData(ThemeStyle.Classic, ThemeMode.Dark)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Light)]
    [InlineData(ThemeStyle.PeachGlass, ThemeMode.Dark)]
    public void A_checked_chip_stays_readable_under_the_pointer(ThemeStyle style, ThemeMode mode)
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var themes = AppHost.Get<ThemeService>();
        var profiles = store.Profiles.Select(p => p.Clone()).ToList();
        Window? window = null;
        try
        {
            themes.Apply(new ThemeSettings { Style = style, Mode = mode }, "en");
            var vm = new ToolbarPageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>());
            vm.SelectedProfile = vm.Profiles.Single(p => p.Profile.Id == "mixed");
            vm.MixedExtensions!.Groups[0].IsExpanded = true;
            window = new Window { Content = new ToolbarPage { DataContext = vm }, Width = 1080, Height = 1600 };
            window.Classes.Set("peach", style == ThemeStyle.PeachGlass); // what Motion.Track does for the app's own windows
            window.Show();
            Pump();

            var chip = window.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Classes.Contains("extchip"));
            chip.IsChecked = true;
            ((IPseudoClasses)chip.Classes).Set(":pointerover", true);
            Pump();

            var face = chip.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color;
            var text = Assert.IsAssignableFrom<ISolidColorBrush>(face.Foreground).Color;
            Assert.True(Contrast(text, background) >= 4.5, $"{style} {mode}: {text} on {background} is {Contrast(text, background):0.00}:1");
        }
        finally
        {
            window?.Close();
            themes.Apply(store.Settings.Theme, "en");
            store.Profiles.Clear();
            store.Profiles.AddRange(profiles);
            store.SaveLibrary();
        }
    }

    /// <summary>WCAG contrast ratio of two opaque colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Lin(byte v) => v / 255.0 is var c && c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double L(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
