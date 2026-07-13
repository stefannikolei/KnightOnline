using Microsoft.Xna.Framework;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Sky;
using OpenKO.Client.Engine.Terrain;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.7 pins: the pure terrain and sky geometry builders.</summary>
public class TerrainAndSkyTests
{
    private static uint PackAttr(bool tileFull, int tex1Dir, int tex2Dir, int tex1Idx, int tex2Idx)
        => (tileFull ? 1u : 0u)
           | ((uint)tex1Dir << 1)
           | ((uint)tex2Dir << 6)
           | ((uint)tex1Idx << 11)
           | ((uint)tex2Idx << 21);

    private static N3Terrain MakeTerrain(int mapSize, Func<int, int, N3MapData> cell)
    {
        var data = new N3MapData[mapSize * mapSize];
        for (int x = 0; x < mapSize; x++)
            for (int z = 0; z < mapSize; z++)
                data[x * mapSize + z] = cell(x, z);
        var terrain = new N3Terrain();
        terrain.Initialize(mapSize, data, new byte[mapSize * mapSize]);
        return terrain;
    }

    [Fact]
    public void MapDataDefault_MatchesCppConstructor()
    {
        // __MapData(): bIsTileFull=1, Tex1Idx=Tex2Idx=1023, dirs 0.
        N3MapData d = N3MapData.Default;
        Assert.True(d.IsTileFull);
        Assert.Equal(0, d.Tex1Dir);
        Assert.Equal(0, d.Tex2Dir);
        Assert.Equal(1023, d.Tex1Idx);
        Assert.Equal(1023, d.Tex2Idx);
    }

    [Fact]
    public void BuildLevel1_ProducesFourVerticesPerTile()
    {
        // A 9x9 map is exactly one patch (8x8 tiles) with valid +1 corners.
        N3Terrain terrain = MakeTerrain(9, (x, z) => new N3MapData
        {
            Height = x + z,
            Attr = PackAttr(tileFull: true, 0, 0, 0, 1023),
        });

        TerrainPatchMesh mesh = TerrainVertexBuilder.BuildLevel1(terrain, 0, 0);

        Assert.Equal(64 * 4, mesh.Vertices.Length);
        Assert.Equal(64, mesh.Tiles.Length);
        // Tile index = ix*8 + iz; base vertex = tileIndex*4.
        Assert.Equal(0, mesh.Tiles[0].BaseVertex);
        Assert.Equal(4, mesh.Tiles[1].BaseVertex);
        Assert.Equal(63 * 4, mesh.Tiles[63].BaseVertex);
    }

    [Fact]
    public void BuildLevel1_EvenTileUsesTileDirUv_AndCppWinding()
    {
        N3Terrain terrain = MakeTerrain(9, (x, z) => new N3MapData
        {
            Height = 0f,
            Attr = PackAttr(tileFull: true, tex1Dir: 0, tex2Dir: 0, tex1Idx: 0, tex2Idx: 1023),
        });

        TerrainPatchMesh mesh = TerrainVertexBuilder.BuildLevel1(terrain, 0, 0);

        // Tile (ix=0, iz=0): even parity → LT, LB, RB, RT order.
        TerrainVertex v0 = mesh.Vertices[0];
        TerrainVertex v1 = mesh.Vertices[1];
        TerrainVertex v2 = mesh.Vertices[2];
        TerrainVertex v3 = mesh.Vertices[3];

        const float t = TerrainVertexBuilder.TileSize;
        Assert.Equal(new Vector3(0, 0, 0), v0.Position);      // LT
        Assert.Equal(new Vector3(0, 0, t), v1.Position);      // LB
        Assert.Equal(new Vector3(t, 0, t), v2.Position);      // RB
        Assert.Equal(new Vector3(t, 0, 0), v3.Position);      // RT

        // dir1=0 (up): u1={dirU[0][2],[0],[1],[3]} = {0,0,1,1}; v1={dirV[0][2],[0],[1],[3]} = {1,0,0,1}
        Assert.Equal(new Vector2(0, 1), v0.TexCoord0);
        Assert.Equal(new Vector2(0, 0), v1.TexCoord0);
        Assert.Equal(new Vector2(1, 0), v2.TexCoord0);
        Assert.Equal(new Vector2(1, 1), v3.TexCoord0);
    }

    [Fact]
    public void BuildLevel1_OddTileFlipsWinding()
    {
        N3Terrain terrain = MakeTerrain(9, (x, z) => new N3MapData
        {
            Height = 0f,
            Attr = PackAttr(tileFull: true, 0, 0, 0, 1023),
        });

        TerrainPatchMesh mesh = TerrainVertexBuilder.BuildLevel1(terrain, 0, 0);

        // Tile (ix=1, iz=0) → tileIndex 8, base 32; odd parity → LB, RB, RT, LT.
        int b = mesh.Tiles[8].BaseVertex;
        const float t = TerrainVertexBuilder.TileSize;
        Assert.Equal(new Vector3(t, 0, t), mesh.Vertices[b + 0].Position);       // LB
        Assert.Equal(new Vector3(2 * t, 0, t), mesh.Vertices[b + 1].Position);   // RB
        Assert.Equal(new Vector3(2 * t, 0, 0), mesh.Vertices[b + 2].Position);   // RT
        Assert.Equal(new Vector3(t, 0, 0), mesh.Vertices[b + 3].Position);       // LT
    }

    [Fact]
    public void BuildLevel1_TileLessCellUsesColormapUv()
    {
        // IsTileFull=false forces the colormap UV branch.
        N3Terrain terrain = MakeTerrain(9, (x, z) => new N3MapData
        {
            Height = 0f,
            Attr = PackAttr(tileFull: false, 0, 0, 5, 1023),
        });

        TerrainPatchMesh mesh = TerrainVertexBuilder.BuildLevel1(terrain, 0, 0);

        // Tile (0,0): u1[0]=(0%32)/32=0, v1[0]=(32-0)/32=1; second UV set is the unit quad.
        TerrainVertex v0 = mesh.Vertices[0];
        Assert.Equal(0f, v0.TexCoord0.X, 5);
        Assert.Equal(1f, v0.TexCoord0.Y, 5);
        Assert.Equal(new Vector2(0, 0), v0.TexCoord1); // u2[0]=0, v2[0]=0
    }

    [Theory]
    // colormap (Tex1Idx >= numTileTex): one dual-texture pass.
    [InlineData(1023, 1023, true, 4, 1, TerrainTextureSource.ColorMap, TerrainPassBlend.Opaque)]
    // colormap (!IsTileFull) even with a valid tile index.
    [InlineData(0, 0, false, 4, 1, TerrainTextureSource.ColorMap, TerrainPassBlend.Opaque)]
    // one tile: single opaque tile0 pass.
    [InlineData(0, 1023, true, 4, 1, TerrainTextureSource.Tile0, TerrainPassBlend.Opaque)]
    public void TilePassPlanner_SingleCases(
        int tex1, int tex2, bool full, int numTileTex, int expectedCount,
        TerrainTextureSource primary, TerrainPassBlend blend)
    {
        IReadOnlyList<TerrainPass> passes = TilePassPlanner.Plan(tex1, tex2, full, numTileTex);
        Assert.Equal(expectedCount, passes.Count);
        Assert.Equal(primary, passes[0].Primary);
        Assert.Equal(blend, passes[0].Blend);
    }

    [Fact]
    public void TilePassPlanner_TwoTiles_OpaqueThenAdditive()
    {
        IReadOnlyList<TerrainPass> passes = TilePassPlanner.Plan(0, 1, isTileFull: true, numTileTex: 4);
        Assert.Equal(2, passes.Count);
        Assert.Equal(TerrainTextureSource.Tile0, passes[0].Primary);
        Assert.Equal(TerrainPassBlend.Opaque, passes[0].Blend);
        Assert.Equal(TerrainTextureSource.Tile1, passes[1].Primary);
        Assert.Equal(TerrainPassBlend.Additive, passes[1].Blend);
    }

    [Fact]
    public void TilePassPlanner_ColormapPassCarriesBaseDetailSecondary()
    {
        IReadOnlyList<TerrainPass> passes = TilePassPlanner.Plan(1023, 1023, isTileFull: true, numTileTex: 4);
        Assert.Single(passes);
        Assert.Equal(TerrainTextureSource.ColorMap, passes[0].Primary);
        Assert.Equal(TerrainTextureSource.BaseDetail, passes[0].Secondary);
    }

    [Fact]
    public void SkyFrontFan_TopVerticesAreTransparent_BottomOpaque()
    {
        SkyFanVertex[] front = SkyGeometry.BuildFrontFan(SkyGeometry.DefaultFogColor);
        Assert.Equal(4, front.Length);
        // [0],[3] top: alpha 0; [1],[2] bottom: fog alpha (0xFF).
        Assert.Equal(0u, front[0].Color >> 24);
        Assert.Equal(0u, front[3].Color >> 24);
        Assert.Equal(0xFFu, front[1].Color >> 24);
        Assert.Equal(0xFFu, front[2].Color >> 24);
        // RGB always the fog colour.
        Assert.Equal(SkyGeometry.DefaultFogColor & 0x00FFFFFFu, front[0].Color & 0x00FFFFFFu);
    }

    [Fact]
    public void SkyBottomFan_AllFogColor()
    {
        SkyFanVertex[] bottom = SkyGeometry.BuildBottomFan(SkyGeometry.DefaultFogColor);
        Assert.All(bottom, v => Assert.Equal(SkyGeometry.DefaultFogColor, v.Color));
    }

    [Fact]
    public void CloudDome_HasEightVerticesAndTenTriangles()
    {
        SkyCloudVertex[] dome = SkyGeometry.BuildCloudDome();
        Assert.Equal(8, dome.Length);
        Assert.Equal(30, SkyGeometry.CloudIndices.Length); // 10 triangles
        // Big square (verts 0-3) is transparent, small (4-7) opaque.
        Assert.All(dome[..4], v => Assert.Equal(0u, v.Color >> 24));
        Assert.All(dome[4..], v => Assert.Equal(0xFFu, v.Color >> 24));
        // The small tier sits above the big tier.
        Assert.True(dome[4].Position.Y > dome[0].Position.Y);
    }

    [Fact]
    public void CameraYaw_TurnsToFaceCameraDirection()
    {
        // Camera straight along +Z looking at origin: dir.X == 0 → identity.
        Matrix identity = SkyGeometry.CameraYaw(new System.Numerics.Vector3(0, 0, -10), System.Numerics.Vector3.Zero);
        Assert.Equal(Matrix.Identity, identity);

        // Camera offset in X yields a non-identity yaw.
        Matrix yawed = SkyGeometry.CameraYaw(new System.Numerics.Vector3(10, 0, 0), System.Numerics.Vector3.Zero);
        Assert.NotEqual(Matrix.Identity, yawed);
    }
}
