using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-9.11b pins: the runtime .tlt lightmap file reader/writer.</summary>
public class N3TerrainLightMapTests
{
    private static N3Texture MakeLightMapTexture(byte fill)
    {
        // Mip-mapped 8x8 DXT1 (two levels) so Save/Load consume identical bytes
        // — a non-mip DXT texture trips CN3Texture's preserved under-skip quirk.
        var tex = new N3Texture();
        tex.Initialize(8, 8, N3PixelFormat.Dxt1, mipMaps: true);
        byte[] l0 = new byte[N3Texture.GetLevelSize(8, 8, N3PixelFormat.Dxt1)];
        byte[] l1 = new byte[N3Texture.GetLevelSize(4, 4, N3PixelFormat.Dxt1)];
        Array.Fill(l0, fill);
        Array.Fill(l1, fill);
        tex.MipLevels.Add(l0);
        tex.MipLevels.Add(l1);
        return tex;
    }

    [Fact]
    public void LightMapFile_RoundTrips()
    {
        const int patchMapSize = 2; // 2x2 = 4 patch slots
        var original = new N3TerrainLightMapFile { Version = 0, FileFormatVersion = N3FormatVersion.V1264 };
        original.Initialize(patchMapSize);

        // Patch [px=0,pz=0] (index 0): two lightmap tiles.
        var p00 = new N3TerrainLightMapPatch();
        p00.Tiles.Add(new N3TerrainLightMapTile { TileX = 1, TileZ = 2, Texture = MakeLightMapTexture(0x11) });
        p00.Tiles.Add(new N3TerrainLightMapTile { TileX = 3, TileZ = 4, Texture = MakeLightMapTexture(0x22) });
        original.Patches[0] = p00;

        // Patch index 1 stays empty (Addr <= 0). Patch [px=1,pz=1] (index 3).
        var p11 = new N3TerrainLightMapPatch();
        p11.Tiles.Add(new N3TerrainLightMapTile { TileX = 0, TileZ = 5, Texture = MakeLightMapTexture(0x33) });
        original.Patches[3] = p11;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3TerrainLightMapFile { FileFormatVersion = N3FormatVersion.V1264 };
        loaded.Load(new BinaryReader(stream), patchMapSize);

        Assert.Equal(0, loaded.Version);
        Assert.Equal(patchMapSize, loaded.PatchMapSize);
        Assert.Equal(4, loaded.Patches.Length);

        // Empty patches read back null (their Addr entry was 0).
        Assert.Null(loaded.Patches[1]);
        Assert.Null(loaded.Patches[2]);

        N3TerrainLightMapPatch? lp00 = loaded.Patches[0];
        Assert.NotNull(lp00);
        Assert.Equal(2, lp00.Tiles.Count);
        Assert.Equal(1, lp00.Tiles[0].TileX);
        Assert.Equal(2, lp00.Tiles[0].TileZ);
        Assert.Equal(8, lp00.Tiles[0].Texture.Width);
        Assert.Equal(0x11, lp00.Tiles[0].Texture.MipLevels[0][0]);
        Assert.Equal(3, lp00.Tiles[1].TileX);
        Assert.Equal(0x22, lp00.Tiles[1].Texture.MipLevels[0][0]);

        N3TerrainLightMapPatch? lp11 = loaded.Patches[3];
        Assert.NotNull(lp11);
        N3TerrainLightMapTile only = Assert.Single(lp11.Tiles);
        Assert.Equal(5, only.TileZ);
        Assert.Equal(0x33, only.Texture.MipLevels[0][0]);
    }

    [Fact]
    public void EnumerateGlobalTiles_KeysByGlobalTileCoord()
    {
        // rtx = px*PATCH_TILE_SIZE + tx, rtz = pz*PATCH_TILE_SIZE + tz.
        const int patchMapSize = 2;
        var file = new N3TerrainLightMapFile();
        file.Initialize(patchMapSize);

        var p = new N3TerrainLightMapPatch();
        p.Tiles.Add(new N3TerrainLightMapTile { TileX = 1, TileZ = 2, Texture = MakeLightMapTexture(1) });
        file.Patches[3] = p; // index 3 => px=1, pz=1

        (int TileX, int TileZ, N3Texture Texture) entry = Assert.Single(file.EnumerateGlobalTiles());
        Assert.Equal(1 * N3TerrainLightMapFile.PatchTileSize + 1, entry.TileX); // 9
        Assert.Equal(1 * N3TerrainLightMapFile.PatchTileSize + 2, entry.TileZ); // 10
    }
}
