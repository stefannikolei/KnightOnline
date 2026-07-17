namespace OpenKO.Client.Assets;

/// <summary>
/// One lightmap texture inside a .tlt patch block: the tile coordinate local to
/// the patch (0..PATCH_TILE_SIZE) and the baked N3 texture that modulates it.
/// </summary>
public sealed class N3TerrainLightMapTile
{
    /// <summary>tx — tile column within the patch (CN3Terrain::SetLightMapPatch).</summary>
    public int TileX { get; set; }

    /// <summary>tz — tile row within the patch.</summary>
    public int TileZ { get; set; }

    public N3Texture Texture { get; set; } = new();
}

/// <summary>A .tlt patch block: the lightmap textures baked for one 8×8 patch.</summary>
public sealed class N3TerrainLightMapPatch
{
    /// <summary>The per-tile lightmap textures (TexCount entries in the file).</summary>
    public List<N3TerrainLightMapTile> Tiles { get; } = [];
}

/// <summary>
/// Port of the runtime-streamed lightmap file (<c>.tlt</c>,
/// <c>__TABLE_ZONE::szLightMapFN</c>) read by <c>CN3Terrain::SetLightMap</c> /
/// <c>SetLightMapPatch</c> (Client/WarFare/N3Terrain.cpp:1003-1236). Unlike the
/// embedded <see cref="N3TerrainLightMap"/> block inside the .gtd (which the
/// client loads and discards), this is the file the runtime pages a 3×3 patch
/// window from as the camera moves.
///
/// Binary layout:
///   int32 Version                       (ignored by the client; usually 0)
///   int32 Addr[PatchMapSize*PatchMapSize] — byte offset of each patch block,
///                                            indexed [px + PatchMapSize*pz];
///                                            &lt;= 0 means the patch has no lightmaps
///   per non-empty patch, at Addr[p]:
///     int32 TexCount
///     TexCount × { int32 tx; int32 tz; CN3Texture }
///
/// The PatchMapSize is NOT stored in the file — the caller supplies it from the
/// terrain (m_pat_MapSize = (MapSize-1)/PATCH_TILE_SIZE), exactly as the C++
/// sizes its Addr table.
/// </summary>
public sealed class N3TerrainLightMapFile
{
    public const int PatchTileSize = N3Terrain.PatchTileSize; // PATCH_TILE_SIZE

    /// <summary>iVersion — read then ignored by the client; kept for round trips.</summary>
    public int Version { get; set; }

    /// <summary>m_pat_MapSize — supplied at load, mirrored on save (not in file).</summary>
    public int PatchMapSize { get; private set; }

    /// <summary>Passed to every embedded lightmap texture (m_iFileFormatVersion).</summary>
    public uint FileFormatVersion { get; set; } = N3FormatVersion.Default;

    /// <summary>
    /// Patch blocks indexed [px + PatchMapSize*pz]; a null slot is an empty
    /// patch (its Addr entry was &lt;= 0).
    /// </summary>
    public N3TerrainLightMapPatch?[] Patches { get; private set; } = [];

    /// <summary>Reads the .tlt for a terrain whose patch grid is patchMapSize².</summary>
    public void Load(BinaryReader reader, int patchMapSize)
    {
        if (patchMapSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(patchMapSize));

        PatchMapSize = patchMapSize;
        Version = reader.ReadInt32();

        int count = patchMapSize * patchMapSize;
        var addr = new int[count];
        for (int i = 0; i < count; i++)
            addr[i] = reader.ReadInt32();

        Patches = new N3TerrainLightMapPatch?[count];
        for (int i = 0; i < count; i++)
        {
            if (addr[i] <= 0)
                continue; // empty patch (SetLightMapPatch: jump <= 0 returns)

            reader.BaseStream.Seek(addr[i], SeekOrigin.Begin);
            int texCount = reader.ReadInt32();
            var patch = new N3TerrainLightMapPatch();
            for (int t = 0; t < texCount; t++)
            {
                int tx = reader.ReadInt32();
                int tz = reader.ReadInt32();
                var texture = new N3Texture { FileFormatVersion = FileFormatVersion };
                texture.Load(reader);
                patch.Tiles.Add(new N3TerrainLightMapTile { TileX = tx, TileZ = tz, Texture = texture });
            }

            Patches[i] = patch;
        }
    }

    /// <summary>
    /// Mirror writer for round-trip fixtures. Reproduces the offset table by
    /// reserving it, streaming the patch blocks, then back-patching the real
    /// offsets — so a written file re-reads field-for-field. Requires a
    /// seekable stream (MemoryStream / FileStream).
    /// </summary>
    public void Save(BinaryWriter writer)
    {
        Stream stream = writer.BaseStream;
        writer.Write(Version);

        int count = PatchMapSize * PatchMapSize;
        long addrTablePos = stream.Position;
        var addr = new int[count];
        for (int i = 0; i < count; i++)
            writer.Write(0); // reserve the Addr table

        for (int i = 0; i < count; i++)
        {
            N3TerrainLightMapPatch? patch = i < Patches.Length ? Patches[i] : null;
            if (patch == null || patch.Tiles.Count == 0)
            {
                addr[i] = 0;
                continue;
            }

            addr[i] = (int)stream.Position;
            writer.Write(patch.Tiles.Count);
            foreach (N3TerrainLightMapTile tile in patch.Tiles)
            {
                writer.Write(tile.TileX);
                writer.Write(tile.TileZ);
                tile.Texture.Save(writer);
            }
        }

        long endPos = stream.Position;
        stream.Seek(addrTablePos, SeekOrigin.Begin);
        foreach (int a in addr)
            writer.Write(a);
        stream.Seek(endPos, SeekOrigin.Begin);
    }

    /// <summary>Test/tool helper: size the patch grid before populating it.</summary>
    public void Initialize(int patchMapSize)
    {
        if (patchMapSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(patchMapSize));

        PatchMapSize = patchMapSize;
        Patches = new N3TerrainLightMapPatch?[patchMapSize * patchMapSize];
    }

    /// <summary>
    /// Enumerates every lightmap keyed by its global tile coordinate
    /// (rtx = px*PATCH_TILE_SIZE + tx), matching CN3Terrain::GetLightMap's key.
    /// </summary>
    public IEnumerable<(int TileX, int TileZ, N3Texture Texture)> EnumerateGlobalTiles()
    {
        for (int i = 0; i < Patches.Length; i++)
        {
            N3TerrainLightMapPatch? patch = Patches[i];
            if (patch == null)
                continue;

            int px = i % PatchMapSize;
            int pz = i / PatchMapSize;
            foreach (N3TerrainLightMapTile tile in patch.Tiles)
                yield return (px * PatchTileSize + tile.TileX, pz * PatchTileSize + tile.TileZ, tile.Texture);
        }
    }
}
