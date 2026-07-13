namespace OpenKO.Client.Engine.Terrain;

/// <summary>Which terrain texture a pass samples.</summary>
public enum TerrainTextureSource
{
    /// <summary>The 128×128 colormap texture covering this patch region.</summary>
    ColorMap,

    /// <summary>Misc\Terrain_Base.bmp, the shared detail overlay.</summary>
    BaseDetail,

    /// <summary>The first tile texture (Tex1Idx).</summary>
    Tile0,

    /// <summary>The second tile texture (Tex2Idx).</summary>
    Tile1,
}

/// <summary>How a pass blends onto the framebuffer / previous pass.</summary>
public enum TerrainPassBlend
{
    /// <summary>Overwrite (first pass / D3DTOP_SELECTARG1 or MODULATE base).</summary>
    Opaque,

    /// <summary>Additive — the D3DTOP_ADD second tile stage (exact).</summary>
    Additive,
}

/// <summary>
/// One draw of a tile. <see cref="Secondary"/> is set only for the dual-texture
/// colormap case (colormap modulated by the base detail via DualTextureEffect).
/// </summary>
public readonly record struct TerrainPass(
    TerrainTextureSource Primary,
    TerrainTextureSource? Secondary,
    TerrainPassBlend Blend);

/// <summary>
/// Pure port of the per-tile branch of <c>CN3TerrainPatch::Render</c> level 1
/// (Client/WarFare/N3TerrainPatch.cpp:810-901): maps a tile's texture
/// configuration to the multi-pass list the MonoGame renderer issues. The
/// hardware fallback pass (D3DBLEND_ZERO/SRCCOLOR when a card cannot blend
/// tiles) is omitted — modern GL always reports tile support (bAvailableTile),
/// so that branch never executes.
/// </summary>
public static class TilePassPlanner
{
    /// <summary>
    /// Returns the passes for one tile. <paramref name="numTileTex"/> is the
    /// count of loaded tile textures; the tile-less test uses '&gt;=' exactly as
    /// the C++ Render does (Tick uses '&gt;', kept in the vertex builder).
    /// </summary>
    public static IReadOnlyList<TerrainPass> Plan(int tex1Idx, int tex2Idx, bool isTileFull, int numTileTex)
    {
        // Tile-less: colormap × base detail × diffuse (one 3-stage draw).
        if (tex1Idx >= numTileTex || !isTileFull)
        {
            return
            [
                new TerrainPass(TerrainTextureSource.ColorMap, TerrainTextureSource.BaseDetail, TerrainPassBlend.Opaque),
            ];
        }

        // tex1Idx < numTileTex from here.
        if (tex2Idx < numTileTex)
        {
            // Two tiles: tile0 opaque, tile1 additive (D3DTOP_ADD).
            return
            [
                new TerrainPass(TerrainTextureSource.Tile0, null, TerrainPassBlend.Opaque),
                new TerrainPass(TerrainTextureSource.Tile1, null, TerrainPassBlend.Additive),
            ];
        }

        // One tile: tile0 modulated by diffuse.
        return
        [
            new TerrainPass(TerrainTextureSource.Tile0, null, TerrainPassBlend.Opaque),
        ];
    }
}
