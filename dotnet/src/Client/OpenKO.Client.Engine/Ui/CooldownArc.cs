using System.Numerics;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Pure geometry for the skill-icon cooldown sweep — a headless port of the
/// TRIANGLEFAN built in <c>CUIHotKeyDlg::RenderCooldown</c>
/// (Client/WarFare/UIHotKeyDlg.cpp:1043-1147). It is a radial <b>pie</b>, not a ring:
/// a centre vertex plus an arc of up to <c>segments</c> points on a circle of the given
/// <paramref name="radius"/> (the icon's corner radius, so the wedge overfills the square
/// and is scissor-clipped). The arc spans <c>2π·progress</c>, starting at 12 o'clock
/// (−π/2) and sweeping in the C++ direction (<c>angle = start − maxAngle·i/seg</c>).
/// No GraphicsDevice — the device layer feeds the fan into the primitive batcher.
/// </summary>
public static class CooldownArc
{
    /// <summary>The C++ segment budget (may be more than a small icon needs).</summary>
    public const int DefaultSegments = 64;

    /// <summary>The pie fill colour — <c>D3DCOLOR_ARGB(0x80,0xFF,0x00,0x00)</c>.</summary>
    public const uint FillColorArgb = 0x80FF0000;

    /// <summary>The arc start angle — 12 o'clock.</summary>
    public const float StartAngle = -MathF.PI / 2f;

    /// <summary>
    /// Build the triangle-fan vertices for a cooldown pie. Element 0 is the centre; the
    /// remaining elements are the arc from 12 o'clock sweeping <c>2π·progress</c>. Returns
    /// an empty array when <paramref name="progress"/> ≤ 0 (off cooldown — nothing to draw).
    /// <paramref name="progress"/> is clamped to [0,1].
    /// </summary>
    public static Vector2[] BuildPie(
        float centerX, float centerY, float radius, float progress, int segments = DefaultSegments)
    {
        if (progress <= 0f)
            return [];
        if (progress > 1f)
            progress = 1f;

        // segmentCountToDraw = (int)(segments * progress); guard the tiny-progress case where
        // the C++ would divide by a zero segment count.
        int seg = (int)(segments * progress);
        if (seg < 1)
            seg = 1;

        float maxAngle = MathF.PI * 2f * progress;

        var v = new Vector2[seg + 2];
        v[0] = new Vector2(centerX, centerY);
        for (int i = 0; i <= seg; i++)
        {
            float angle = StartAngle - maxAngle * (i / (float)seg);
            v[i + 1] = new Vector2(
                centerX + MathF.Cos(angle) * radius,
                centerY + MathF.Sin(angle) * radius);
        }

        return v;
    }

    /// <summary>The corner radius of an icon square (hypot of the half extents) — overfills the square.</summary>
    public static float CornerRadius(float halfWidth, float halfHeight) =>
        MathF.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
}
