using System.Numerics;
using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Stage-5.6 pins (terrain): the .gtd reader.</summary>
public class N3TerrainTests
{
    [Fact]
    public void MapData_BitfieldUnpacking_MatchesMsvcLayout()
    {
        // bIsTileFull:1 | Tex1Dir:5 | Tex2Dir:5 | Tex1Idx:10 | Tex2Idx:10 (LSB first)
        var data = new N3MapData
        {
            Height = 12.5f,
            Attr = 1u | (9u << 1) | (5u << 6) | (700u << 11) | (1023u << 21),
        };

        Assert.True(data.IsTileFull);
        Assert.Equal(9, data.Tex1Dir);
        Assert.Equal(5, data.Tex2Dir);
        Assert.Equal(700, data.Tex1Idx);
        Assert.Equal(1023, data.Tex2Idx);
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.SizeOf<N3MapData>());
        Assert.Equal(44, System.Runtime.InteropServices.Marshal.SizeOf<N3VertexRiver>());
    }

    [Fact]
    public void Terrain_RoundTrips()
    {
        const int mapSize = 17; // patch map size 2
        var mapData = new N3MapData[mapSize * mapSize];
        for (int i = 0; i < mapData.Length; i++)
            mapData[i] = new N3MapData { Height = i * 0.25f, Attr = (uint)i };
        var grass = new byte[mapSize * mapSize];
        grass[5] = 42;

        var original = new N3Terrain
        {
            Name = "karus_terrain",
            FileFormatVersion = N3FormatVersion.V1264,
            HeaderIdk0 = 3,
            GrassFileName = "karus",
        };
        original.Initialize(mapSize, mapData, grass);
        original.PatchMiddleY[3] = 7.5f;
        original.PatchRadius[3] = 30f;

        original.TileTexSources.Add(@"misc\tile_karus.gtt");
        original.TileTextures.Add((0, 4));
        original.TileTextures.Add((0, 9));

        // Mip-mapped 8x8 (two levels) so the embedded texture consumes its
        // bytes exactly — a non-mip DXT lightmap would desync the terrain
        // stream via the preserved under-skip quirk (in the C++ just the same).
        var lightTex = new N3Texture();
        lightTex.Initialize(8, 8, N3PixelFormat.Dxt1, mipMaps: true);
        lightTex.MipLevels.Add(new byte[N3Texture.GetLevelSize(8, 8, N3PixelFormat.Dxt1)]);
        lightTex.MipLevels.Add(new byte[N3Texture.GetLevelSize(4, 4, N3PixelFormat.Dxt1)]);
        original.LightMaps.Add(new N3TerrainLightMap { X = 2, Z = 3, Texture = lightTex });

        original.Rivers.Add(new N3RiverInfo
        {
            Vertices =
            [
                new N3VertexRiver { Position = new Vector3(0, 1, 0), Color = 0x80FFFFFF, U2 = 0.5f },
                new N3VertexRiver { Position = new Vector3(4, 1, 0) },
                new N3VertexRiver { Position = new Vector3(0, 1, 4) },
                new N3VertexRiver { Position = new Vector3(4, 1, 4) },
            ],
            IndexCount = 18,
            TextureName = "wave00.dxt",
        });

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            original.Save(writer);
        }

        stream.Position = 0;
        var loaded = new N3Terrain { FileFormatVersion = N3FormatVersion.V1264 };
        loaded.Load(new BinaryReader(stream));

        Assert.Equal(stream.Length, stream.Position);

        Assert.Equal("karus_terrain", loaded.Name);
        Assert.Equal(3, loaded.HeaderIdk0);
        Assert.Equal(17, loaded.MapSize);
        Assert.Equal(2, loaded.PatchMapSize);
        Assert.Equal(12f * 0.25f, loaded.MapData[12].Height);
        Assert.Equal(42, loaded.GrassAttr[5]);
        Assert.Equal("karus", loaded.GrassFileName);
        Assert.Equal(@"misc\tile_karus.gtt", Assert.Single(loaded.TileTexSources));
        Assert.Equal(2, loaded.TileTextures.Count);
        Assert.Equal((short)9, loaded.TileTextures[1].TileIdx);
        N3TerrainLightMap lm = Assert.Single(loaded.LightMaps);
        Assert.Equal(3, lm.Z);
        Assert.Equal(8, lm.Texture.Width);
        N3RiverInfo river = Assert.Single(loaded.Rivers);
        Assert.Equal(4, river.Vertices.Length);
        Assert.Equal(0.5f, river.Vertices[0].U2);
        Assert.Equal("wave00.dxt", river.TextureName);
        Assert.Equal(7.5f, loaded.PatchMiddleY[3]);
    }
}
