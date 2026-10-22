// What the annotate and add background tools share around their custom colour: the eyedropper that fills it from the
// picture as the window shows it (key I; Esc turns it off and leaves the window open), and the button that copies the
// colour's HEX code.

using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels.Tools;

/// <summary>An image tool with a custom colour: the eyedropper and the copy button of the panel.</summary>
public abstract partial class ColorToolViewModel : ImageToolViewModel
{
    private int _copiedRound;

    protected ColorToolViewModel(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log)
        : base(tool, files, store, loc, log)
    {
    }

    /// <summary>The eyedropper is on: the picture shows a magnifier, and a press on it picks the colour there.</summary>
    [ObservableProperty] private bool _isEyedropperOn;

    /// <summary>The copy button says "Copied" for a moment after it copied.</summary>
    [ObservableProperty] private bool _colorCopied;

    /// <summary>
    /// Puts text on the clipboard (the window service sets it: the window's), and throws when another program holds the
    /// clipboard. Null: there is no clipboard, and the copy button does nothing.
    /// </summary>
    public Func<string, Task>? SetClipboardText { get; set; }

    /// <summary>The colour the copy button copies, <c>#RRGGBB</c>; null when the tool has no single colour now (the button is off).</summary>
    protected abstract string? ColorHex { get; }

    /// <summary>Makes <paramref name="hex"/> (<c>#RRGGBB</c>) the tool's custom colour, as if it had been typed in.</summary>
    protected abstract void UseColor(string hex);

    /// <summary>Call when <see cref="ColorHex"/> may have become null or stopped being so.</summary>
    protected void NotifyColorChanged() => CopyColorCommand.NotifyCanExecuteChanged();

    /// <summary>The eyedropper picked <paramref name="color"/> off the picture: it becomes the custom colour, and the eyedropper goes off.</summary>
    public void PickColor(Color color)
    {
        UseColor(ColorCodes.Hex(color.R, color.G, color.B));
        IsEyedropperOn = false;
    }

    /// <summary>I turns the eyedropper on and off; the tool's own keys go through <see cref="HandleKey"/> of its view model.</summary>
    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None || key != Key.I)
            return false;
        IsEyedropperOn = !IsEyedropperOn;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanCopyColor), AllowConcurrentExecutions = true)] // pressable while it says "Copied"
    private async Task CopyColor()
    {
        if (ColorHex is not { } hex || SetClipboardText is null)
            return;
        try
        {
            await SetClipboardText(hex);
        }
        catch (Exception ex)
        {
            // Another program holds the clipboard: the button says nothing, the log does.
            Log?.LogWarning(ex, "Could not copy the colour code {Code} to the clipboard", hex);
            return;
        }
        var round = ++_copiedRound;
        ColorCopied = true;
        await Task.Delay(CopiedHold);
        if (round == _copiedRound)
            ColorCopied = false;
    }

    private bool CanCopyColor => ColorHex is not null;
}
