using System.Numerics;
using System.Runtime.InteropServices;
using OpenKO.Core.Text;

namespace OpenKO.Client.Assets;

/// <summary>
/// One terrain cell (__MapData in WarFare/N3TerrainDef.h): the height plus a
/// packed bitfield (MSVC packs LSB-first: bIsTileFull:1, Tex1Dir:5,
/// Tex2Dir:5, Tex1Idx:10, Tex2Idx:10). 8 bytes on disk.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3MapData
{
    public float Height;
    public uint Attr;

    public readonly bool IsTileFull => (Attr & 0x1) != 0;

    public readonly int Tex1Dir => (int)((Attr >> 1) & 0x1F);

    public readonly int Tex2Dir => (int)((Attr >> 6) & 0x1F);

    public readonly int Tex1Idx => (int)((Attr >> 11) & 0x3FF);

    public readonly int Tex2Idx => (int)((Attr >> 21) & 0x3FF);
}

/// <summary>__VertexRiver (N3River.h): position, normal, color, two UV sets — 44 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct N3VertexRiver
{
    public Vector3 Position;
    public Vector3 Normal;
    public uint Color;
    public float U;
    public float V;
    public float U2;
    public float V2;
}

public sealed class N3RiverInfo
{
    public N3VertexRiver[] Vertices { get; set; } = [];

    /// <summary>iIC — the index count; the indices themselves are generated, not stored.</summary>
    public int IndexCount { get; set; }

    public string TextureName { get; set; } = string.Empty;
}

public sealed class N3TerrainLightMap
{
    public short X { get; set; }

    public short Z { get; set; }

    public N3Texture Texture { get; set; } = new();
}

/// <summary>
/// Port of <c>CN3Terrain::Load</c> (Client/WarFare/N3Terrain.cpp) — the .gtd
/// terrain file: map cells, patch bounding data, grass attributes, the tile
/// texture table (indices into external .gtt files), embedded lightmaps (the
/// C++ loads and discards them; the port keeps them), rivers and the pond
/// count. Quirks kept verbatim: the header is a custom [idk][len][name] block
/// only for format version >= 1264, and pond loading is disabled upstream —
/// only the count is read, any pond payload stays unread.
/// </summary>
public sealed class N3Terrain : N3BaseFile
{
    public const int PatchTileSize = 8;
    private const int MaxPath = 260;

    /// <summary>The unexplained leading int of the >= 1264 header (iIdk0).</summary>
    public int HeaderIdk0 { get; set; }

    /// <summary>m_ti_MapSize — the map is MapSize x MapSize cells.</summary>
    public int MapSize { get; private set; }

    public int PatchMapSize { get; private set; }

    /// <summary>Row-major [x * MapSize + z], like the C++ indexing.</summary>
    public N3MapData[] MapData { get; private set; } = [];

    /// <summary>[x * PatchMapSize + z]</summary>
    public float[] PatchMiddleY { get; private set; } = [];

    /// <summary>[x * PatchMapSize + z]</summary>
    public float[] PatchRadius { get; private set; } = [];

    /// <summary>[x * MapSize + z]</summary>
    public byte[] GrassAttr { get; private set; } = [];

    /// <summary>The .grs base name (external file under misc\grass).</summary>
    public string GrassFileName { get; set; } = string.Empty;

    /// <summary>Source .gtt file names of the tile texture table.</summary>
    public List<string> TileTexSources { get; } = [];

    /// <summary>Per tile texture: (source file index, texture index inside that .gtt).</summary>
    public List<(short SrcIdx, short TileIdx)> TileTextures { get; } = [];

    public List<N3TerrainLightMap> LightMaps { get; } = [];

    public List<N3RiverInfo> Rivers { get; } = [];

    /// <summary>
    /// The pond mesh count. CN3Pond::Load is disabled upstream (it reads the
    /// count and returns), so any pond payload after it stays unread.
    /// </summary>
    public int PondCount { get; private set; }

    public override void Load(BinaryReader reader)
    {
        // Custom header instead of base.Load: only for >= 1264 formats, and
        // with an extra leading int (CN3Terrain::Load).
        if (FileFormatVersion >= N3FormatVersion.V1264)
        {
            HeaderIdk0 = reader.ReadInt32();
            int nameLength = reader.ReadInt32();
            NameBytes = nameLength > 0 ? ReadExactly(reader, nameLength) : [];
        }

        MapSize = reader.ReadInt32();
        PatchMapSize = (MapSize - 1) / PatchTileSize;

        MapData = reader.ReadStructs<N3MapData>(MapSize * MapSize);

        PatchMiddleY = new float[PatchMapSize * PatchMapSize];
        PatchRadius = new float[PatchMapSize * PatchMapSize];
        for (int x = 0; x < PatchMapSize; x++)
        {
            for (int z = 0; z < PatchMapSize; z++)
            {
                PatchMiddleY[x * PatchMapSize + z] = reader.ReadSingle();
                PatchRadius[x * PatchMapSize + z] = reader.ReadSingle();
            }
        }

        GrassAttr = ReadExactly(reader, MapSize * MapSize);
        GrassFileName = ReadFixedString(reader, MaxPath);

        LoadTileInfo(reader);
        LoadLightMaps(reader);
        LoadRivers(reader);

        PondCount = reader.ReadInt32(); // CN3Pond::Load stops here upstream
    }

    private void LoadTileInfo(BinaryReader reader)
    {
        TileTexSources.Clear();
        TileTextures.Clear();

        uint numTileTex = reader.ReadUInt32();
        if (numTileTex == 0)
            return;

        int numSources = reader.ReadInt32();
        if (numSources == 0)
            return; // C++ returns without reading the tile table

        for (int i = 0; i < numSources; i++)
            TileTexSources.Add(ReadFixedString(reader, MaxPath));

        for (uint i = 0; i < numTileTex; i++)
        {
            short srcIdx = reader.ReadInt16();
            short tileIdx = reader.ReadInt16();
            TileTextures.Add((srcIdx, tileIdx)); // texture data lives in the external .gtt
        }
    }

    private void LoadLightMaps(BinaryReader reader)
    {
        LightMaps.Clear();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var lightMap = new N3TerrainLightMap
            {
                X = reader.ReadInt16(),
                Z = reader.ReadInt16(),
            };
            lightMap.Texture.FileFormatVersion = FileFormatVersion;
            lightMap.Texture.Load(reader); // the C++ loads (and discards) it
            LightMaps.Add(lightMap);
        }
    }

    private void LoadRivers(BinaryReader reader)
    {
        Rivers.Clear();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var river = new N3RiverInfo();
            int vertexCount = reader.ReadInt32();
            river.Vertices = reader.ReadStructs<N3VertexRiver>(vertexCount);
            river.IndexCount = reader.ReadInt32();

            int texNameLength = reader.ReadInt32();
            if (texNameLength > 0)
                river.TextureName = KoEncoding.Cp949.GetString(ReadExactly(reader, texNameLength));

            Rivers.Add(river);
        }
    }

    /// <summary>Mirror writer for round-trip fixtures (pond payload not emitted).</summary>
    public override void Save(BinaryWriter writer)
    {
        if (FileFormatVersion >= N3FormatVersion.V1264)
        {
            writer.Write(HeaderIdk0);
            writer.Write(NameBytes.Length);
            writer.Write(NameBytes);
        }

        writer.Write(MapSize);
        writer.WriteStructs<N3MapData>(MapData);

        for (int x = 0; x < PatchMapSize; x++)
        {
            for (int z = 0; z < PatchMapSize; z++)
            {
                writer.Write(PatchMiddleY[x * PatchMapSize + z]);
                writer.Write(PatchRadius[x * PatchMapSize + z]);
            }
        }

        writer.Write(GrassAttr);
        WriteFixedString(writer, GrassFileName, MaxPath);

        writer.Write((uint)TileTextures.Count);
        if (TileTextures.Count > 0)
        {
            writer.Write(TileTexSources.Count);
            foreach (string source in TileTexSources)
                WriteFixedString(writer, source, MaxPath);
            foreach ((short srcIdx, short tileIdx) in TileTextures)
            {
                writer.Write(srcIdx);
                writer.Write(tileIdx);
            }
        }

        writer.Write(LightMaps.Count);
        foreach (N3TerrainLightMap lightMap in LightMaps)
        {
            writer.Write(lightMap.X);
            writer.Write(lightMap.Z);
            lightMap.Texture.Save(writer);
        }

        writer.Write(Rivers.Count);
        foreach (N3RiverInfo river in Rivers)
        {
            writer.Write(river.Vertices.Length);
            writer.WriteStructs<N3VertexRiver>(river.Vertices);
            writer.Write(river.IndexCount);
            byte[] texName = KoEncoding.Cp949.GetBytes(river.TextureName);
            writer.Write(texName.Length);
            writer.Write(texName);
        }

        writer.Write(PondCount);
    }

    /// <summary>Test helper.</summary>
    public void Initialize(int mapSize, N3MapData[] mapData, byte[] grassAttr)
    {
        MapSize = mapSize;
        PatchMapSize = (mapSize - 1) / PatchTileSize;
        MapData = mapData;
        GrassAttr = grassAttr;
        PatchMiddleY = new float[PatchMapSize * PatchMapSize];
        PatchRadius = new float[PatchMapSize * PatchMapSize];
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count)
            throw new EndOfStreamException($"Terrain block is truncated ({data.Length}/{count} bytes)");
        return data;
    }

    private static string ReadFixedString(BinaryReader reader, int size)
    {
        byte[] raw = ReadExactly(reader, size);
        int nul = Array.IndexOf(raw, (byte)0);
        return KoEncoding.Cp949.GetString(raw, 0, nul < 0 ? raw.Length : nul);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int size)
    {
        var raw = new byte[size];
        byte[] bytes = KoEncoding.Cp949.GetBytes(value);
        bytes.AsSpan(0, System.Math.Min(bytes.Length, size - 1)).CopyTo(raw);
        writer.Write(raw);
    }
}
