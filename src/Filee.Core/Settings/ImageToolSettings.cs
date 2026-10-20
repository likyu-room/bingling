// Settings that the image tools remember between uses.

namespace Filee.Core.Settings;

/// <summary>What the image tools (crop, ...) remember between uses; part of <see cref="AppSettings"/>.</summary>
public sealed class ImageToolSettings
{
    /// <summary>
    /// The ratio the crop window starts with, one of <see cref="Presets.CropMath.AspectIds"/>. An id this version doesn't
    /// know gives a free box (<see cref="Presets.CropMath.RatioOf"/>).
    /// </summary>
    public string CropAspect { get; set; } = "original";

    public ImageToolSettings Clone() => (ImageToolSettings)MemberwiseClone();
}
