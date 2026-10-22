// The colour card the eyedropper leaves: the picked colour, its codes, and a button to copy each of them.

using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.Core.Formats;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

/// <param name="color">The picked colour.</param>
/// <param name="setText">Puts text on the clipboard (the card window's).</param>
/// <param name="log">Where a copy that failed is written down.</param>
public sealed partial class ColorCardViewModel(Color color, Func<string, Task> setText, ILogger? log) : ObservableObject
{
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.6);

    private int _copiedRound;

    public IBrush Swatch { get; } = new ImmutableSolidColorBrush(color);
    public string Hex { get; } = ColorCodes.Hex(color.R, color.G, color.B);
    public string Rgb { get; } = ColorCodes.Rgb(color.R, color.G, color.B);
    public string Hsl { get; } = ColorCodes.Hsl(color.R, color.G, color.B);

    /// <summary>"Copied the HEX code": the copy made as the card opened went through, and no button has copied since.</summary>
    [ObservableProperty] private bool _saysHexCopied;

    /// <summary>The code a button copied a moment ago: that button says "Copied" meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HexCopied), nameof(RgbCopied), nameof(HslCopied))]
    private string? _justCopied;

    public bool HexCopied => JustCopied == Hex;
    public bool RgbCopied => JustCopied == Rgb;
    public bool HslCopied => JustCopied == Hsl;

    /// <summary>Puts the HEX code on the clipboard as the card opens; the card says so when it went through.</summary>
    public async Task CopyHexOnOpenAsync() => SaysHexCopied = await TryCopyAsync(Hex);

    [RelayCommand(AllowConcurrentExecutions = true)] // the button stays pressable while it says "Copied"
    private Task CopyHex() => CopyAsync(Hex);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopyRgb() => CopyAsync(Rgb);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopyHsl() => CopyAsync(Hsl);

    private async Task CopyAsync(string code)
    {
        if (!await TryCopyAsync(code))
            return;
        SaysHexCopied = false;
        var round = ++_copiedRound;
        JustCopied = code;
        await Task.Delay(CopiedFor);
        if (round == _copiedRound)
            JustCopied = null;
    }

    private async Task<bool> TryCopyAsync(string code)
    {
        try
        {
            await setText(code);
            return true;
        }
        catch (Exception ex)
        {
            // Another program holds the clipboard: the card says nothing, the log does.
            log?.LogWarning(ex, "Could not copy the colour code {Code} to the clipboard", code);
            return false;
        }
    }
}
