// Theme: light/dark, accent colour, softness (corner radius), reduce animations. Changes apply live.

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.ViewModels.Pages;

public sealed partial class ThemePageViewModel : ObservableObject
{
    private readonly UserDataStore _store;
    private readonly bool _loading;

    public ThemePageViewModel(UserDataStore store, ILocalizer loc, IPlatformServices platform)
    {
        _store = store;
        _loading = true;
        var theme = store.Settings.Theme;
        Modes = Enum.GetValues<ThemeMode>().Select(m => new Choice<ThemeMode>(m, loc[$"theme.mode.{m}"])).ToList();
        Mode = Modes.First(m => m.Value == theme.Mode);
        Styles = Enum.GetValues<ThemeStyle>().Select(s => new Choice<ThemeStyle>(s, loc[$"theme.style.{s}"])).ToList();
        Style = Styles.First(s => s.Value == theme.Style);
        Accent = Color.TryParse(theme.Accent, out var c) ? c : Colors.MediumPurple;
        AccentHex = $"#{Accent.R:X2}{Accent.G:X2}{Accent.B:X2}";
        CornerRadius = theme.CornerRadius;
        ReduceMotion = theme.ReduceMotion ?? platform.PrefersReducedMotion;
        PreviewItems =
        [
            new DonutItem { Id = "a", Label = "PNG", Icon = DonutIcons.ForFormat("png") },
            new DonutItem { Id = "b", Label = "PDF", Icon = DonutIcons.ForFormat("pdf") },
            new DonutItem { Id = "c", Label = "WEBP", Icon = DonutIcons.ForFormat("webp") },
            new DonutItem { Id = "d", Label = "HWPX", Icon = DonutIcons.ForFormat("hwpx") },
            new DonutItem { Id = "e", Label = "JPG 70%", Caption = "JPG", Icon = DonutIcons.ForFormat("jpg") },
            new DonutItem { Id = "f", Label = "TIFF", Icon = DonutIcons.ForFormat("tiff") },
        ];
        _loading = false;
    }

    public IReadOnlyList<Choice<ThemeMode>> Modes { get; }
    public IReadOnlyList<Choice<ThemeStyle>> Styles { get; }
    public IReadOnlyList<Color> Swatches { get; } = ThemeService.Swatches.Select(Color.Parse).ToList();
    public IReadOnlyList<DonutItem> PreviewItems { get; }

    [ObservableProperty] private Choice<ThemeMode> _mode = null!;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGlass))] private Choice<ThemeStyle> _style = null!;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccentBrush))] private Color _accent;
    [ObservableProperty] private string _accentHex = "";

    /// <summary>Peach Glass is chosen: the preview donut is peach, and the accent colour is fixed (the card is off).</summary>
    public bool IsGlass => Style.Value == ThemeStyle.PeachGlass;

    public IBrush AccentBrush => new SolidColorBrush(Accent);
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private bool _reduceMotion;

    partial void OnModeChanged(Choice<ThemeMode>? oldValue, Choice<ThemeMode> newValue)
    {
        if (!Choices.Restore(oldValue, newValue, v => Mode = v))
            Save(t => t.Mode = newValue.Value);
    }
    partial void OnStyleChanged(Choice<ThemeStyle>? oldValue, Choice<ThemeStyle> newValue)
    {
        if (!Choices.Restore(oldValue, newValue, v => Style = v))
            Save(t => t.Style = newValue.Value);
    }
    partial void OnAccentChanged(Color value)
    {
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        if (!string.Equals(AccentHex, hex, StringComparison.OrdinalIgnoreCase))
            AccentHex = hex;
        Save(t => t.Accent = hex);
    }

    // Custom colour typed as #RRGGBB; ignored until it parses.
    partial void OnAccentHexChanged(string value)
    {
        if (Color.TryParse(value.Trim(), out var color) && color != Accent)
            Accent = Color.FromRgb(color.R, color.G, color.B);
    }
    partial void OnCornerRadiusChanged(double value) => Save(t => t.CornerRadius = Math.Round(value));
    partial void OnReduceMotionChanged(bool value) => Save(t => t.ReduceMotion = value);

    [RelayCommand]
    private void PickSwatch(Color color) => Accent = color;

    private void Save(Action<ThemeSettings> change)
    {
        if (_loading)
            return;
        change(_store.Settings.Theme);
        _store.SaveSettings(); // App re-applies the theme on SettingsChanged
    }
}
