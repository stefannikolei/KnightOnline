namespace OpenKO.Client.Engine.Sky;

/// <summary>A UV sub-rectangle (0..1) of a texture atlas — inclusive corners.</summary>
public readonly record struct UvRect(float U0, float V0, float U1, float V1);

/// <summary>The three additive sun billboards and their half-sizes (disk, glow, flare).</summary>
public readonly record struct SunPartLayout(float DiskHalfSize, float GlowHalfSize, float FlareHalfSize);

/// <summary>
/// Pure geometry for the sun (CN3Sun, 3-part disk+glow+flare) and the moon
/// (CN3Moon, a 6×4 phase strip). Kept device-free so the phase→UV math and the
/// sun part sizing are headless-testable.
/// </summary>
public static class SkyBodies
{
    // ---- Moon phase strip (misc\sky\phases.tga) --------------------------
    // CN3Moon::SetMoonPhase: iIndex %= 24; a 6-column, 4-row grid of sub-images.
    /// <summary>Number of distinct moon phases in the strip (CN3Moon: iIndex %= 24).</summary>
    public const int MoonPhaseCount = 24;

    /// <summary>Columns of the phase grid (fOffsetX = 1/6).</summary>
    public const int MoonPhaseColumns = 6;

    /// <summary>Rows of the phase grid (fOffsetY = 1/4).</summary>
    public const int MoonPhaseRows = 4;

    // ---- Sun parts (CN3Sun::Init deltas, relative to the viewport) --------
    /// <summary>SUNPART_SUN delta (disk core) — CN3Sun::Init ChangeDelta(0.1f).</summary>
    public const float SunDiskDelta = 0.1f;

    /// <summary>SUNPART_GLOW delta (soft glow) — ChangeDelta(0.25f).</summary>
    public const float SunGlowDelta = 0.25f;

    /// <summary>SUNPART_FLARE delta (lens flare) — ChangeDelta(0.13f).</summary>
    public const float SunFlareDelta = 0.13f;

    /// <summary>
    /// The moon phase index for a game date: <c>(month*30 + day) mod 24</c>
    /// (CN3SkyMng::SetGameTime → CN3Moon::SetMoonPhase), normalised to 0..23.
    /// </summary>
    public static int MoonPhaseIndex(int month, int day)
    {
        int raw = (month * 30) + day;
        return ((raw % MoonPhaseCount) + MoonPhaseCount) % MoonPhaseCount;
    }

    /// <summary>
    /// The UV sub-rect for a moon phase index in the 6×4 strip
    /// (CN3Moon::SetMoonPhase: row = i/6, col = i%6, cell = 1/6 × 1/4).
    /// </summary>
    public static UvRect MoonPhaseUv(int phaseIndex)
    {
        int i = ((phaseIndex % MoonPhaseCount) + MoonPhaseCount) % MoonPhaseCount;
        int row = i / MoonPhaseColumns;
        int col = i % MoonPhaseColumns;
        float ox = 1.0f / MoonPhaseColumns;
        float oy = 1.0f / MoonPhaseRows;
        return new UvRect(ox * col, oy * row, ox * (col + 1), oy * (row + 1));
    }

    /// <summary>
    /// The three sun billboard half-sizes, scaled from the C++ deltas so the
    /// disk matches <paramref name="diskHalfSize"/> and glow/flare keep their
    /// 0.25/0.13-to-0.1 proportions (CN3Sun::Init).
    /// </summary>
    public static SunPartLayout SunLayout(float diskHalfSize)
    {
        float k = diskHalfSize / SunDiskDelta;
        return new SunPartLayout(
            SunDiskDelta * k,
            SunGlowDelta * k,
            SunFlareDelta * k);
    }
}
