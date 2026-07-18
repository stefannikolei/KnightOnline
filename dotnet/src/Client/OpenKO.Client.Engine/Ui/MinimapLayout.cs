using System.Numerics;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// A minimap dot — one <c>__PositionInfo</c> (party/NPC/enemy marker): a world
/// position and a packed ARGB colour (<c>crType</c>).
/// </summary>
public readonly record struct MinimapDot(Vector3 Position, uint ColorArgb);

/// <summary>The UV window into the whole-zone minimap texture (V already flipped).</summary>
public readonly record struct MinimapUv(float U0, float V0, float U1, float V1);

/// <summary>
/// Pure layout math for the state-bar minimap — a headless port of the geometry in
/// <c>CUIStateBar::TickMiniMap</c> / <c>CUIStateBar::Render</c>
/// (Client/WarFare/UIStateBar.cpp). Given the player world position/yaw, the map's
/// world size, the zoom factor and the on-screen <c>Img_MiniMap</c> rect (pixels), it
/// produces:
/// <list type="bullet">
///   <item>the clamped view centre (the map cannot scroll past its edges);</item>
///   <item>the V-flipped UV window that scrolls the whole-zone texture;</item>
///   <item>each dot's integer screen position + a cull flag;</item>
///   <item>the six screen vertices of the rotated green player arrow.</item>
/// </list>
/// No GraphicsDevice — the device layer (<see cref="OpenKO.Client.Game"/> StateBarDialog)
/// feeds these into the quad/primitive batchers.
/// </summary>
public static class MinimapLayout
{
    /// <summary>CUIStateBar::ZoomSet lower clamp.</summary>
    public const float MinZoom = 1.0f;

    /// <summary>CUIStateBar::ZoomSet upper clamp.</summary>
    public const float MaxZoom = 6.0f;

    /// <summary>m_fZoom default (CUIStateBar ctor).</summary>
    public const float DefaultZoom = 6.0f;

    /// <summary>CUIStateBar::ZoomSet — clamp to [1,6].</summary>
    public static float ClampZoom(float zoom) =>
        zoom < MinZoom ? MinZoom : zoom > MaxZoom ? MaxZoom : zoom;

    /// <summary>Btn_ZoomIn — <c>ZoomSet(m_fZoom * 1.1)</c>.</summary>
    public static float ZoomIn(float zoom) => ClampZoom(zoom * 1.1f);

    /// <summary>Btn_ZoomOut — <c>ZoomSet(m_fZoom * 0.9)</c>.</summary>
    public static float ZoomOut(float zoom) => ClampZoom(zoom * 0.9f);

    /// <summary>
    /// Clamp the view centre so the scrolled window stays inside the zone texture
    /// (TickMiniMap edge limiting). Returns (viewX, viewZ) in world units.
    /// </summary>
    public static Vector2 ClampView(
        float playerX, float playerZ,
        float mapSizeX, float mapSizeZ, float zoom,
        int left, int top, int right, int bottom)
    {
        int minimapWidth = right - left;
        int minimapWidth2 = minimapWidth / 2;
        int minimapHeight = bottom - top;
        int minimapHeight2 = minimapHeight / 2;

        float factorX = zoom * minimapWidth / mapSizeX;
        float factorY = zoom * minimapHeight / mapSizeZ;

        float vx = playerX;
        float vz = playerZ;

        if (minimapWidth2 > factorX * vx)
            vx = minimapWidth2 / factorX;
        if (zoom * minimapWidth - minimapWidth2 < factorX * vx)
            vx = (zoom * minimapWidth - minimapWidth2) / factorX;

        if (minimapHeight2 > factorY * vz)
            vz = minimapHeight2 / factorY;
        if (zoom * minimapHeight - minimapHeight2 < factorY * vz)
            vz = (zoom * minimapHeight - minimapHeight2) / factorY;

        return new Vector2(vx, vz);
    }

    /// <summary>
    /// The UV window for a clamped view centre: <c>fOffset = 0.5/zoom</c>, centred on
    /// (view/mapSize), with V flipped (<c>SetUVRect(x1, 1-y1, x2, 1-y2)</c>).
    /// </summary>
    public static MinimapUv ComputeUv(Vector2 view, float mapSizeX, float mapSizeZ, float zoom)
    {
        float fOffset = 0.5f / zoom;
        float fX = view.X / mapSizeX;
        float fY = view.Y / mapSizeZ;

        float x1 = fX - fOffset;
        float y1 = fY + fOffset;
        float x2 = fX + fOffset;
        float y2 = fY - fOffset;

        return new MinimapUv(x1, 1.0f - y1, x2, 1.0f - y2);
    }

    /// <summary>
    /// Project a dot's world position to an integer screen position (Render loop) and report
    /// whether it lies inside the map rect (inclusive) — the C++ culls dots outside it.
    /// </summary>
    public static bool TryDotScreen(
        Vector2 view, float mapSizeX, float mapSizeZ, float zoom,
        int left, int top, int right, int bottom,
        Vector3 dotPos, out Vector2 screen)
    {
        float width = right - left;
        float height = bottom - top;
        float centerX = left + width / 2.0f;
        float centerY = top + height / 2.0f;

        float dx = view.X - dotPos.X;
        float dz = view.Y - dotPos.Z;

        float sx = (int)(centerX - zoom * width * (dx / mapSizeX));
        float sy = (int)(centerY + zoom * height * (dz / mapSizeZ));
        screen = new Vector2(sx, sy);

        return sx >= left && sx <= right && sy >= top && sy <= bottom;
    }

    /// <summary>
    /// The six screen-space triangle vertices of the green player arrow, rotated by
    /// <paramref name="yaw"/> (RotationZ) about the arrow anchor. Two triangles
    /// (TRIANGLELIST) exactly as <c>m_vArrows[0..5]</c>.
    /// </summary>
    public static Vector2[] ArrowTriangles(
        Vector2 view, float playerX, float playerZ, float yaw,
        float mapSizeX, float mapSizeZ, float zoom,
        int left, int top, int right, int bottom)
    {
        int minimapWidth = right - left;
        int minimapWidth2 = minimapWidth / 2;
        int minimapHeight = bottom - top;
        int minimapHeight2 = minimapHeight / 2;

        float factorX = zoom * minimapWidth / mapSizeX;
        float factorY = zoom * minimapHeight / mapSizeZ;
        float fH = (bottom - top) / 30.0f;

        float posX = left + minimapWidth2 + factorX * (playerX - view.X);
        float posY = top + minimapHeight2 - factorY * (playerZ - view.Y);

        // Local arrow (before rotation): m_vArrows[0..2] + m_vArrows[3..5].
        Vector2[] local =
        [
            new(0f, -fH),        // [0]
            new(0f, fH / 2f),    // [1]
            new(-fH, fH),        // [2]
            new(0f, -fH),        // [3] = [0]
            new(fH, fH),         // [4] = [2] with x negated
            new(0f, fH / 2f),    // [5] = [1]
        ];

        float c = MathF.Cos(yaw);
        float s = MathF.Sin(yaw);

        var outv = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float x = local[i].X;
            float y = local[i].Y;
            // __Vector3 * RotationZ (row-vector) then + translation.
            outv[i] = new Vector2(x * c - y * s + posX, x * s + y * c + posY);
        }

        return outv;
    }
}
