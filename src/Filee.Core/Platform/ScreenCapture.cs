// A picture of part of the screen: what the Peach Glass donut shows, blurred, through its slices.

namespace Filee.Core.Platform;

/// <summary>
/// Pixels of a screen area, top row first, <paramref name="Width"/> × 4 bytes a row in B, G, R, A order. The screen is
/// opaque: A is 255.
/// </summary>
public sealed record ScreenCapture(int Width, int Height, byte[] Bgra);
