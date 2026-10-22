// Settings that the image tools remember between uses.

using System.Text.Json.Serialization;
using Filee.Core.Presets;

namespace Filee.Core.Settings;

/// <summary>What the image tools (crop, background, redact, annotate) remember between uses; part of <see cref="AppSettings"/>.</summary>
public sealed class ImageToolSettings
{
    private BackgroundEdit _background = new();

    /// <summary>
    /// The ratio the crop window starts with, one of <see cref="Presets.CropMath.AspectIds"/>. An id this version doesn't
    /// know gives a free box (<see cref="Presets.CropMath.RatioOf"/>).
    /// </summary>
    public string CropAspect { get; set; } = "original";

    /// <summary>
    /// The backdrop the "add background" window starts with. Assigning keeps a copy that never has a picture's path: a
    /// picture from the user's disk may be gone the next time, so after one the last gradient or colour is what stays.
    /// </summary>
    public BackgroundEdit Background
    {
        get => _background;
        set
        {
            var copy = value?.Clone() ?? new BackgroundEdit();
            if (copy.Kind == BackgroundKind.Image)
            {
                copy.Kind = _background.Kind;
                copy.Value = _background.Value;
            }
            copy.ImagePath = null;
            _background = copy;
        }
    }

    /// <summary>How the "redact" window starts hiding a box.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<RedactMode>))]
    public RedactMode RedactMode { get; set; }

    /// <summary>The colour the "annotate" window starts with, <c>#RRGGBB</c>.</summary>
    public string PenColor { get; set; } = "#FF3B30";

    /// <summary>The thickness the "annotate" window starts with.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<StrokeLevel>))]
    public StrokeLevel PenWidth { get; set; } = StrokeLevel.Normal;

    /// <summary>The drawing tool the "annotate" window starts with.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<AnnotationKind>))]
    public AnnotationKind LastTool { get; set; }

    /// <summary>
    /// Whether a tool's window closes once Copy has put the picture on the clipboard (as after Save). Off: it stays open,
    /// to edit more or save a file too.
    /// </summary>
    public bool CloseAfterCopy { get; set; } = true;

    public ImageToolSettings Clone()
    {
        var copy = (ImageToolSettings)MemberwiseClone();
        copy._background = _background.Clone();
        return copy;
    }
}
