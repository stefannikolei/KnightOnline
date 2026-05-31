namespace OpenKO.Game.Rendering;

/// <summary>
/// A straight (non-premultiplied) RGBA colour with 8-bit channels, used by the 2D UI render path.
/// Kept independent of System.Drawing / DirectX colour types so the game layer carries no platform
/// or GPU dependency.
/// </summary>
public readonly struct UiColor
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    public UiColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Channels as 0..1 floats in R,G,B,A order (the form GL shaders want).</summary>
    public (float R, float G, float B, float A) ToFloats()
        => (R / 255f, G / 255f, B / 255f, A / 255f);

    public static UiColor FromArgb(uint argb)
        => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    public static readonly UiColor White = new(255, 255, 255);
    public static readonly UiColor Black = new(0, 0, 0);
    public static readonly UiColor Transparent = new(0, 0, 0, 0);
}
