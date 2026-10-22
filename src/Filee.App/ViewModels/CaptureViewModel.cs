// The capture window's state: the frozen screen, the area dragged over it (in the screen's pixels) and what the toolbar
// is on (an area, the whole screen or the eyedropper); Edit, Copy and Save take the area and close the window.

using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using SkiaSharp;

namespace Filee.App.ViewModels;

/// <summary>What the capture window's toolbar is on, in its order.</summary>
public enum CaptureMode
{
    Region,
    Screen,
    Color,
}

/// <param name="shot">The screen's picture; whoever opened the window lets go of it when the window closes.</param>
/// <param name="output">Saves the area, or opens it to draw on.</param>
public sealed partial class CaptureViewModel(ScreenShot shot, CaptureOutput output) : ObservableObject
{
    /// <summary>An area narrower or lower than this (pixels) was a click, not one to capture.</summary>
    public const int MinSide = 8;

    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(0.6);

    private bool _finishing; // Edit, Copy or Save took the area: the others do nothing

    public ScreenShot Shot => shot;

    /// <summary>The area, in the picture's pixels; null before a drag and in the eyedropper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SizeText), nameof(ShowsActions), nameof(ShowsHint))]
    private PixelRect? _selection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeIndex), nameof(PicksColor), nameof(ShowsHint))]
    private CaptureMode _mode;

    /// <summary>"Copied", for a moment before the window closes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActions))]
    private bool _saysCopied;

    /// <summary>A drag is picking the area: its buttons wait for the release.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActions))]
    private bool _dragging;

    /// <summary><see cref="Mode"/> for the toolbar's chips.</summary>
    public int ModeIndex
    {
        get => (int)Mode;
        set => Mode = (CaptureMode)value;
    }

    public bool HasSelection => Selection is not null;

    /// <summary>"1200 × 675": the area's size in the screen's pixels.</summary>
    public string SizeText => Selection is { } area ? $"{area.Width} × {area.Height}" : "";

    /// <summary>Edit / Copy / Save beside the area.</summary>
    public bool ShowsActions => HasSelection && !Dragging && !SaysCopied;

    /// <summary>"Drag over what to capture", until there is an area.</summary>
    public bool ShowsHint => Mode == CaptureMode.Region && !HasSelection;

    /// <summary>The eyedropper: the magnifier follows the pointer and a click picks its colour.</summary>
    public bool PicksColor => Mode == CaptureMode.Color;

    /// <summary>Puts a picture on the clipboard; true when it did (the window's clipboard). Null: Copy does nothing.</summary>
    public Func<SKImage, Task<bool>>? CopyPicture { get; set; }

    /// <summary>The window closes.</summary>
    public event EventHandler? Done;

    partial void OnModeChanged(CaptureMode value) =>
        Selection = value == CaptureMode.Screen ? new PixelRect(0, 0, shot.Bounds.Width, shot.Bounds.Height) : null;

    /// <summary>A drag from one of the picture's pixels to another: the area between them, both in it.</summary>
    public void DragTo(PixelPoint from, PixelPoint to)
    {
        Mode = CaptureMode.Region;
        Dragging = true;
        Selection = new PixelRect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X) + 1, Math.Abs(to.Y - from.Y) + 1);
    }

    /// <summary>The drag is over: an area under <see cref="MinSide"/> across was a click, and goes; one that stays shows its buttons.</summary>
    public void EndDrag()
    {
        if (Selection is { } area && (area.Width < MinSide || area.Height < MinSide))
            Selection = null;
        Dragging = false;
    }

    /// <summary>Copies the area as a picture, says so for a moment and closes; a busy clipboard leaves the window open.</summary>
    [RelayCommand]
    private async Task Copy()
    {
        if (_finishing || Selection is not { } area || CopyPicture is not { } copy)
            return;
        _finishing = true;
        bool copied;
        using (var picture = shot.Crop(area))
            copied = await copy(picture);
        if (!copied)
        {
            _finishing = false;
            return;
        }
        SaysCopied = true;
        await Task.Delay(CopiedFor);
        Close();
    }

    /// <summary>Closes and saves the area in the capture folder.</summary>
    [RelayCommand]
    private Task Save() => Take() is { } picture ? output.SaveAsync(picture) : Task.CompletedTask;

    /// <summary>Closes and opens the area in the annotate window, centred on where it was.</summary>
    [RelayCommand]
    private Task Edit()
    {
        var middle = Selection is { } area ? shot.Bounds.Position + new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2) : default;
        return Take() is { } picture ? output.EditAsync(picture, middle) : Task.CompletedTask;
    }

    [RelayCommand]
    private void Close() => Done?.Invoke(this, EventArgs.Empty);

    /// <summary>The area, cut out of the picture, and the window closed; null when there is none or it was taken already.</summary>
    private SKImage? Take()
    {
        if (_finishing || Selection is not { } area)
            return null;
        _finishing = true;
        var picture = shot.Crop(area);
        Close();
        return picture;
    }
}
