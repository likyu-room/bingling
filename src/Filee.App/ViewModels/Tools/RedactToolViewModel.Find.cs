// The redact tool's "find what to hide": the picture, upright as the window shows it, goes to the platform's text and
// face recognition (on this PC), SensitiveFinder picks what to offer, and the panel lists it, every spot chosen at first
// and outlined on the picture in its kind's colour (the preview's only: the result holds boxes). "Hide the chosen" adds
// their boxes, in the way of hiding chosen, as one undo step. Only for one file: the boxes go on every file, and a spot is
// where it is on one picture only.

using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.Core;
using Filee.Core.Formats;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Privacy;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels.Tools;

public sealed partial class RedactToolViewModel
{
    private CancellationTokenSource? _finding;

    /// <summary>
    /// Reads the text and faces on a PNG (the window service sets it: the platform's <see cref="IPlatformServices.RecognizeAsync"/>).
    /// Unset, nothing can be read.
    /// </summary>
    public Func<string, CancellationToken, Task<RecognitionResult>> Recognize { get; set; } = (_, _) => Task.FromResult(RecognitionResult.Empty);

    /// <summary>What was found, as the panel lists it; empty before a find, and once the chosen ones are hidden.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChosenSpots), nameof(HasSpots)), NotifyCanExecuteChangedFor(nameof(HideChosenCommand))]
    private IReadOnlyList<FoundSpot> _spots = [];

    /// <summary>The line under the button: looking, nothing found, no text recognition here, or that it failed; empty for none.</summary>
    [ObservableProperty] private string _findStatus = "";

    /// <summary>A find is going on: the button is off.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private bool _isFinding;

    /// <summary>There is a list: the "hide the chosen" button shows.</summary>
    public bool HasSpots => Spots.Count > 0;

    /// <summary>The spots to hide, as the picture outlines them.</summary>
    public IReadOnlyList<SensitiveSpot> ChosenSpots => [.. Spots.Where(s => s.IsChosen).Select(s => s.Spot)];

    /// <summary>The button's tip: what it looks for, or why it is off (several files).</summary>
    public string FindTip => Loc[HasMany ? "tool.redact.find_one_file" : "tool.redact.find_tip"];

    private bool CanFind => !HasMany && !IsFinding;

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task Find()
    {
        IsFinding = true;
        Spots = [];
        FindStatus = Loc["tool.redact.finding"];
        var folder = Path.Combine(Path.GetTempPath(), AppIdentity.Folder, "find", Guid.NewGuid().ToString("N")[..8]);
        using var cancel = _finding = new CancellationTokenSource();
        try
        {
            var (png, width, height) = await Task.Run(() => UprightCopy(Files[0], folder), cancel.Token);
            var read = await Recognize(png, cancel.Token);
            var found = SensitiveFinder.Find(read, width, height);
            Spots = [.. found.Select(s => new FoundSpot(s, Loc["tool.redact.kind." + s.Kind.ToString().ToLowerInvariant()], OnChosenChanged))];
            FindStatus = read.Failed ? Loc["tool.redact.find_failed"] : string.Join("\n", Notes(read.TextAvailable, found.Count));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // The window closed.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A picture that can't be read, a full temp folder.
            Log?.LogWarning(ex, "Could not look for what to hide on {File}", Files[0]);
            FindStatus = Loc["tool.redact.find_failed"];
        }
        finally
        {
            _finding = null;
            IsFinding = false;
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log?.LogInformation(ex, "Could not delete {Folder}", folder);
            }
        }
    }

    /// <summary>What the line under the button says after a find that worked.</summary>
    private IEnumerable<string> Notes(bool textAvailable, int found)
    {
        if (!textAvailable)
            yield return Loc["tool.redact.no_text_language"];
        if (found == 0)
            yield return Loc["tool.redact.found_none"];
    }

    /// <summary>Adds the chosen spots' boxes to the boxes there are, as one undo step, and clears the list.</summary>
    [RelayCommand(CanExecute = nameof(HasChosen))]
    private void HideChosen()
    {
        var chosen = ChosenSpots;
        if (chosen.Count == 0)
            return;
        Redact = new RedactEdit { Mode = _redact.Mode, Boxes = [.. _redact.Boxes, .. chosen.Select(s => s.Box)] };
        Spots = [];
        FindStatus = "";
    }

    private bool HasChosen => Spots.Any(s => s.IsChosen);

    private void OnChosenChanged()
    {
        OnPropertyChanged(nameof(ChosenSpots));
        HideChosenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The picture as the window shows it (its first frame, turned upright) as a PNG in <paramref name="folder"/>, and its
    /// size: whatever its format, Windows can read it, and the boxes found on it are where they show. See-through parts
    /// go white, as black text on them would be black on black for the OCR.
    /// </summary>
    private static (string Path, int Width, int Height) UprightCopy(string path, string folder)
    {
        using var frames = ImageReader.Read(path, FormatRegistry.Detect(path)?.Id ?? "", new Preset(), firstFrameOnly: true);
        var image = frames[0];
        image.AutoOrient();
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "picture.png");
        image.Write(copy, MagickFormat.Png);
        return (copy, (int)image.Width, (int)image.Height);
    }

    /// <summary>Stops a find going on; the window is closing.</summary>
    public override void Dispose()
    {
        _finding?.Cancel();
        base.Dispose();
    }
}

/// <summary>A spot in the find list: its kind as a coloured chip, the words it covers, and whether to hide it (at first, yes).</summary>
public sealed partial class FoundSpot : ObservableObject
{
    private readonly Action _chosenChanged;

    internal FoundSpot(SensitiveSpot spot, string kindText, Action chosenChanged)
    {
        Spot = spot;
        KindText = kindText;
        KindBrush = new ImmutableSolidColorBrush(RedactCanvas.SpotColor(spot.Kind));
        _chosenChanged = chosenChanged;
    }

    public SensitiveSpot Spot { get; }

    /// <summary>"Phone".</summary>
    public string KindText { get; }

    /// <summary>The kind's colour: the chip's and the outline's on the picture.</summary>
    public IBrush KindBrush { get; }

    /// <summary>The words, as read; empty for a face.</summary>
    public string Text => Spot.Text;

    [ObservableProperty] private bool _isChosen = true;

    partial void OnIsChosenChanged(bool value) => _chosenChanged();
}
