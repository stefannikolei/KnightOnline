namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// The colour-over-time math shared by every effect part (N3FXParticle.cpp,
/// N3FXPartBillBoard.cpp, N3FXPartBottomBoard.cpp). All colours are D3DCOLOR
/// (0xAARRGGBB) exactly as the C++ stores them in <c>m_dwColor</c>; the device
/// layer converts to MonoGame colours via <c>ColorInterop</c>.
/// </summary>
public static class FxColor
{
    /// <summary>Fully opaque white — the C++ <c>0xffffffff</c>.</summary>
    public const uint White = 0xffffffff;

    /// <summary>Transparent white — the C++ <c>0x00ffffff</c> (alpha 0, RGB kept).</summary>
    public const uint TransparentWhite = 0x00ffffff;

    /// <summary>
    /// The bucket selection a colour-key particle uses (N3FXParticle::Tick):
    /// <c>idx = (int)(currLife * NUM_KEY_COLOR / life)</c>, clamped to the last
    /// key. The 100 keys are the interpolation — the tool bakes the ramp, the
    /// runtime just picks the bucket, so this is a nearest-key lookup (no lerp),
    /// faithful to the shipped client.
    /// </summary>
    public static uint ColorKeyAt(uint[] colors, float currLife, float life, int numKeyColor)
    {
        if (life <= 0f)
            return colors.Length > 0 ? colors[0] : White;

        int idx = (int)(currLife * numKeyColor / life);
        if (idx >= numKeyColor)
            idx = numKeyColor - 1;
        if (idx < 0)
            idx = 0;
        if (idx >= colors.Length)
            idx = colors.Length - 1;
        return colors[idx];
    }

    /// <summary>
    /// The continuous fade a non-colour-key particle uses (N3FXParticle::Tick):
    /// ramp alpha up over <paramref name="fadeIn"/>, hold opaque for
    /// <paramref name="life"/>, ramp down over <paramref name="fadeOut"/>, then
    /// fully transparent. Alpha is computed in float and truncated to a byte then
    /// shifted into bits 24..31, exactly like the C++ <c>(Alpha&lt;&lt;24)</c>.
    /// </summary>
    public static uint ParticleFade(float currLife, float fadeIn, float life, float fadeOut)
    {
        if (currLife <= fadeIn)
        {
            uint alpha = ToAlpha(255.0f * currLife / fadeIn);
            return (alpha << 24) + TransparentWhite;
        }

        if (currLife < fadeIn + life)
            return White;

        if (currLife < fadeIn + life + fadeOut)
        {
            uint alpha = ToAlpha(255.0f * ((fadeIn + life + fadeOut) - currLife) / fadeOut);
            return (alpha << 24) + TransparentWhite;
        }

        return TransparentWhite;
    }

    /// <summary>
    /// The board fade (N3FXPartBillBoard/BottomBoard::Tick): ramp in, hold opaque
    /// while alive, and only ramp out once the part is DYING.
    /// </summary>
    public static uint BoardFade(float currLife, float fadeIn, float life, float fadeOut, bool dying)
    {
        uint color;
        if (currLife <= fadeIn)
        {
            uint alpha = ToAlpha(255.0f * currLife / fadeIn);
            color = (alpha << 24) + TransparentWhite;
        }
        else
        {
            color = White;
        }

        if (dying)
        {
            float total = fadeIn + life + fadeOut;
            if (currLife >= total)
            {
                color = TransparentWhite;
            }
            else
            {
                uint alpha = ToAlpha(255.0f * (total - currLife) / fadeOut);
                color = (alpha << 24) + TransparentWhite;
            }
        }

        return color;
    }

    /// <summary>
    /// The C++ <c>(uint32_t)fAlpha</c> truncation. A division by zero produces
    /// NaN/Inf; the MSVC float→uint cast yields 0 in those cases, which the .NET
    /// conversion matches, so a 0-length fade simply reads as alpha 0.
    /// </summary>
    private static uint ToAlpha(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return 0u;
        if (value >= 255f)
            return 255u;
        return (uint)value;
    }
}
