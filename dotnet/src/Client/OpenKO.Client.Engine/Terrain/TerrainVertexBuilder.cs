using Microsoft.Xna.Framework;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;

namespace OpenKO.Client.Engine.Terrain;

/// <summary>One tile inside a level-1 patch: its texture indices, the base
/// vertex of its four-vertex fan in the patch vertex buffer, and its global
/// tile coordinate on the map (used to key the per-tile lightmap).</summary>
public readonly record struct TerrainTile(
    int Tex1Idx, int Tex2Idx, bool IsTileFull, int BaseVertex, int TileX, int TileZ);

/// <summary>A built level-1 patch: the VNT2 vertices (4 per tile) plus the
/// per-tile metadata the renderer needs to pick textures/passes.</summary>
public sealed class TerrainPatchMesh
{
    public TerrainVertex[] Vertices { get; init; } = [];

    public TerrainTile[] Tiles { get; init; } = [];

    /// <summary>Left-bottom tile coordinate of the patch on the map.</summary>
    public int TileX { get; init; }

    public int TileZ { get; init; }
}

/// <summary>
/// Pure port of the level-1 branch of <c>CN3TerrainPatch::Tick</c>
/// (Client/WarFare/N3TerrainPatch.cpp): builds the four VNT2 vertices per tile
/// for an 8×8 patch, with the tile-texture UVs taken from the TileDir tables
/// and the colormap UVs for tile-less cells. The D3D triangle-fan winding
/// (parity by (ix+iz)) is preserved; the renderer turns each four-vertex fan
/// into a triangle list via <see cref="FanIndexer"/>.
/// </summary>
public static class TerrainVertexBuilder
{
    public const float TileSize = 4.0f;      // N3TerrainDef.h TILE_SIZE
    public const int PatchTileSize = 8;      // PATCH_TILE_SIZE
    public const int UnitUv = 32;            // UNITUV — tiles per colormap texture
    public const int LightMapTexSize = 16;   // LIGHTMAP_TEX_SIZE

    /// <summary>m_fTileDirU[8][4] — columns are [LT, RT, LB, RB] (N3Terrain.cpp:72).</summary>
    public static readonly float[][] TileDirU =
    [
        [0f, 1f, 0f, 1f], // up
        [0f, 0f, 1f, 1f], // right
        [1f, 0f, 1f, 0f], // left
        [1f, 1f, 0f, 0f], // bottom
        [1f, 0f, 1f, 0f], // up mirrored
        [0f, 0f, 1f, 1f], // right mirrored
        [0f, 1f, 0f, 1f], // left mirrored
        [1f, 1f, 0f, 0f], // bottom mirrored
    ];

    /// <summary>m_fTileDirV[8][4] (N3Terrain.cpp:85).</summary>
    public static readonly float[][] TileDirV =
    [
        [0f, 0f, 1f, 1f], // up
        [1f, 0f, 1f, 0f], // right
        [1f, 1f, 0f, 0f], // left
        [0f, 1f, 0f, 1f], // bottom
        [0f, 0f, 1f, 1f], // up mirrored
        [0f, 1f, 0f, 1f], // right mirrored
        [1f, 1f, 0f, 0f], // left mirrored
        [1f, 0f, 1f, 0f], // bottom mirrored
    ];

    /// <summary>
    /// Builds the level-1 patch anchored at tile (<paramref name="lbTileX"/>,
    /// <paramref name="lbTileZ"/>). Mirrors the C++ tile loop exactly.
    /// </summary>
    public static TerrainPatchMesh BuildLevel1(N3Terrain terrain, int lbTileX, int lbTileZ)
    {
        var vertices = new TerrainVertex[PatchTileSize * PatchTileSize * 4];
        var tiles = new TerrainTile[PatchTileSize * PatchTileSize];

        int vertexIdx = 0;
        int tileCount = 0;
        var u1 = new float[4];
        var u2 = new float[4];
        var v1 = new float[4];
        var v2 = new float[4];

        for (int ix = 0; ix < PatchTileSize; ix++)
        {
            for (int iz = 0; iz < PatchTileSize; iz++)
            {
                int tx = ix + lbTileX;
                int tz = iz + lbTileZ;

                N3MapData map = GetMapData(terrain, tx, tz);
                int tex1Idx = map.Tex1Idx;
                int tex2Idx = map.Tex2Idx;
                bool isTileFull = map.IsTileFull;

                // Tile-less test uses '>' here (Tick), unlike Render's '>='.
                if (tex1Idx > terrain.TileTextures.Count || !isTileFull)
                {
                    u1[0] = u1[1] = (tx % UnitUv) / (float)UnitUv;
                    u1[2] = u1[3] = u1[0] + (1.0f / UnitUv);
                    v1[0] = v1[3] = (UnitUv - (tz % UnitUv)) / (float)UnitUv;
                    v1[1] = v1[2] = v1[0] - (1.0f / UnitUv);
                    u2[0] = u2[1] = 0f;
                    u2[2] = u2[3] = 1f;
                    v2[0] = v2[3] = 0f;
                    v2[1] = v2[2] = 1f;
                }
                else
                {
                    int dir1 = map.Tex1Dir;
                    int dir2 = map.Tex2Dir;
                    u1[0] = TileDirU[dir1][2];
                    u1[1] = TileDirU[dir1][0];
                    u1[2] = TileDirU[dir1][1];
                    u1[3] = TileDirU[dir1][3];
                    v1[0] = TileDirV[dir1][2];
                    v1[1] = TileDirV[dir1][0];
                    v1[2] = TileDirV[dir1][1];
                    v1[3] = TileDirV[dir1][3];
                    u2[0] = TileDirU[dir2][2];
                    u2[1] = TileDirU[dir2][0];
                    u2[2] = TileDirU[dir2][1];
                    u2[3] = TileDirU[dir2][3];
                    v2[0] = TileDirV[dir2][2];
                    v2[1] = TileDirV[dir2][0];
                    v2[2] = TileDirV[dir2][1];
                    v2[3] = TileDirV[dir2][3];
                }

                // The four tile corners, in the C++ order for this parity.
                var lt = new Vector3(tx * TileSize, Height(terrain, tx, tz), tz * TileSize);
                var lb = new Vector3(tx * TileSize, Height(terrain, tx, tz + 1), (tz + 1) * TileSize);
                var rb = new Vector3((tx + 1) * TileSize, Height(terrain, tx + 1, tz + 1), (tz + 1) * TileSize);
                var rt = new Vector3((tx + 1) * TileSize, Height(terrain, tx + 1, tz), tz * TileSize);
                var up = new Vector3(0f, 1f, 0f);

                int b = vertexIdx;
                if ((ix + iz) % 2 == 0)
                {
                    vertices[b + 0] = Make(lt, up, u1[0], v1[0], u2[0], v2[0]);
                    vertices[b + 1] = Make(lb, up, u1[1], v1[1], u2[1], v2[1]);
                    vertices[b + 2] = Make(rb, up, u1[2], v1[2], u2[2], v2[2]);
                    vertices[b + 3] = Make(rt, up, u1[3], v1[3], u2[3], v2[3]);
                }
                else
                {
                    vertices[b + 0] = Make(lb, up, u1[1], v1[1], u2[1], v2[1]);
                    vertices[b + 1] = Make(rb, up, u1[2], v1[2], u2[2], v2[2]);
                    vertices[b + 2] = Make(rt, up, u1[3], v1[3], u2[3], v2[3]);
                    vertices[b + 3] = Make(lt, up, u1[0], v1[0], u2[0], v2[0]);
                }

                tiles[tileCount] = new TerrainTile(tex1Idx, tex2Idx, isTileFull, b, tx, tz);
                vertexIdx += 4;
                tileCount++;
            }
        }

        return new TerrainPatchMesh
        {
            Vertices = vertices,
            Tiles = tiles,
            TileX = lbTileX,
            TileZ = lbTileZ,
        };
    }

    private static TerrainVertex Make(Vector3 p, Vector3 n, float u, float v, float u2, float v2)
        => new(p, n, new Vector2(u, v), new Vector2(u2, v2));

    /// <summary>CN3Terrain::GetMapData — out-of-range cells return defaults.</summary>
    public static N3MapData GetMapData(N3Terrain terrain, int x, int z)
    {
        if (x < 0 || x >= terrain.MapSize || z < 0 || z >= terrain.MapSize)
            return N3MapData.Default;
        return terrain.MapData[(x * terrain.MapSize) + z];
    }

    private static float Height(N3Terrain terrain, int x, int z) => GetMapData(terrain, x, z).Height;
}
