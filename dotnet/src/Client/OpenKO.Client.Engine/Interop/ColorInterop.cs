using Microsoft.Xna.Framework;

namespace OpenKO.Client.Engine.Interop;

/// <summary>
/// D3DCOLOR (0xAARRGGBB) → MonoGame Color. The packed values must never be
/// reinterpreted directly: MonoGame packs ABGR internally.
/// </summary>
public static class ColorInterop
{
    public static Color FromArgb(uint d3dColor) => new(
        (byte)(d3dColor >> 16), // R
        (byte)(d3dColor >> 8),  // G
        (byte)d3dColor,         // B
        (byte)(d3dColor >> 24)); // A

    /// <summary>Back to D3DCOLOR (for round-trips/tests).</summary>
    public static uint ToArgb(Color color)
        => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
