using System.Numerics;
using OpenKO.GameData.Math;

namespace OpenKO.GameData.Maps;

/// <summary>Port of <c>_OBJECT_EVENT</c> (Server/AIServer/Define.h).</summary>
public sealed record ObjectEvent(
    int Belong,
    short Index,
    short Type,
    short ControlNpcId,
    short Status,
    float PosX,
    float PosY,
    float PosZ);

/// <summary>Port of <c>_REGENE_EVENT</c> (spawn area, Ebenezer Map).</summary>
public sealed record RegeneEvent(
    int RegenePoint,
    float PosX,
    float PosY,
    float PosZ,
    float AreaZ,
    float AreaX);

/// <summary>Port of <c>_WARP_INFO</c> (Ebenezer GameDefine.h).</summary>
public sealed record WarpInfo(
    short WarpId,
    byte[] WarpName,     // char[32], CP949, NUL-padded
    byte[] Announce,     // char[256]
    uint Pay,
    short Zone,
    float X,
    float Y,
    float Z,
    float R,
    short Nation);

/// <summary>
/// Port of the server-side <c>MAP</c> class (AIServer/Ebenezer Map.cpp): reads the
/// .smd map files from Server/bin/MAP — terrain heightmap, N3 collision data,
/// object events and the tile/event grid — and provides height/collision queries.
/// </summary>
public sealed class GameMap
{
    public int MapSize { get; private set; }

    public float UnitDistance { get; private set; }

    /// <summary>Terrain heightmap, [x, z], MapSize x MapSize.</summary>
    public float[,] TerrainHeight { get; private set; } = new float[0, 0];

    public N3ShapeManager ShapeManager { get; } = new();

    public List<ObjectEvent> ObjectEvents { get; } = [];

    public List<RegeneEvent> RegeneEvents { get; } = [];

    public List<WarpInfo> Warps { get; } = [];

    /// <summary>Tile event grid [x, z] as stored in the .smd (raw values).</summary>
    public short[,] RawTileEvents { get; private set; } = new short[0, 0];

    /// <summary>
    /// Effective tile event grid: forced to 1 for every tile like the C++
    /// LoadMapTile, because the shipped SMDs don't carry trustworthy event data.
    /// </summary>
    public short[,] TileEvents { get; private set; } = new short[0, 0];

    public static GameMap Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return Load(reader);
    }

    public static GameMap Load(BinaryReader reader)
    {
        var map = new GameMap();
        map.LoadTerrain(reader);

        if (!map.ShapeManager.LoadCollisionData(reader))
            throw new InvalidDataException("invalid collision data");

        float expectedSize = (map.MapSize - 1) * map.UnitDistance;
        if (expectedSize != map.ShapeManager.MapWidth || expectedSize != map.ShapeManager.MapLength)
            throw new InvalidDataException(
                $"collision size mismatch: terrain {expectedSize}, collision {map.ShapeManager.MapWidth}x{map.ShapeManager.MapLength}");

        map.LoadObjectEvents(reader);
        map.LoadMapTiles(reader);
        // Ebenezer-only trailing sections (the AIServer loader simply stops here).
        map.LoadRegeneEvents(reader);
        map.LoadWarpList(reader);

        return map;
    }

    private void LoadTerrain(BinaryReader reader)
    {
        MapSize = reader.ReadInt32();
        UnitDistance = reader.ReadSingle();

        TerrainHeight = new float[MapSize, MapSize];
        for (int z = 0; z < MapSize; z++)
        {
            for (int x = 0; x < MapSize; x++)
                TerrainHeight[x, z] = reader.ReadSingle();
        }
    }

    private void LoadObjectEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ObjectEvents.Add(new ObjectEvent(
                Belong: reader.ReadInt32(),
                Index: reader.ReadInt16(),
                Type: reader.ReadInt16(),
                ControlNpcId: reader.ReadInt16(),
                Status: reader.ReadInt16(),
                PosX: reader.ReadSingle(),
                PosY: reader.ReadSingle(),
                PosZ: reader.ReadSingle()));
        }
    }

    private void LoadMapTiles(BinaryReader reader)
    {
        RawTileEvents = new short[MapSize, MapSize];
        TileEvents = new short[MapSize, MapSize];
        for (int x = 0; x < MapSize; x++)
        {
            for (int z = 0; z < MapSize; z++)
            {
                RawTileEvents[x, z] = reader.ReadInt16();
                TileEvents[x, z] = 1; // C++ forces every tile movable
            }
        }
    }

    private void LoadRegeneEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            RegeneEvents.Add(new RegeneEvent(
                RegenePoint: i,
                PosX: reader.ReadSingle(),
                PosY: reader.ReadSingle(),
                PosZ: reader.ReadSingle(),
                AreaZ: reader.ReadSingle(),
                AreaX: reader.ReadSingle()));
        }
    }

    private void LoadWarpList(BinaryReader reader)
    {
        // The C++ reads sizeof(_WARP_INFO) raw — 320 bytes including the struct's
        // alignment padding (2 bytes before dwPay, 2 before fX, 2 trailing).
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            short warpId = reader.ReadInt16();
            byte[] warpName = reader.ReadBytes(32);
            byte[] announce = reader.ReadBytes(256);
            reader.ReadBytes(2); // padding
            uint pay = reader.ReadUInt32();
            short zone = reader.ReadInt16();
            reader.ReadBytes(2); // padding
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float r = reader.ReadSingle();
            short nation = reader.ReadInt16();
            reader.ReadBytes(2); // trailing padding to 4-byte struct alignment

            Warps.Add(new WarpInfo(warpId, warpName, announce, pay, zone, x, y, z, r, nation));
        }
    }

    /// <summary>
    /// Literal port of MAP::IsMovable: true only when the tile event is 0.
    /// With the forced-1 grid this is false for every in-bounds tile — that IS
    /// the current C++ behavior (its only caller treats it as "is blocked").
    /// NPC movement uses <see cref="IsTileWalkable"/> (CNpc::IsMovable) instead.
    /// </summary>
    public bool IsMovable(int x, int z)
    {
        if (x < 0 || z < 0 || x >= MapSize || z >= MapSize)
            return false;

        return TileEvents[x, z] == 0;
    }

    /// <summary>Port of CNpc::IsMovable's tile check: walkable when the event is non-zero.</summary>
    public bool IsTileWalkable(int x, int z)
    {
        if (x < 0 || z < 0 || x >= MapSize || z >= MapSize)
            return false;

        return TileEvents[x, z] != 0;
    }

    /// <summary>MAP::GetHeight — terrain height by bilinear triangle interpolation.</summary>
    public float GetTerrainHeight(float x, float z)
    {
        int ix = (int)(x / UnitDistance);
        int iz = (int)(z / UnitDistance);

        float dx = (x - ix * UnitDistance) / UnitDistance;
        float dz = (z - iz * UnitDistance) / UnitDistance;

        if (!(dx >= 0.0f && dz >= 0.0f && dx < 1.0f && dz < 1.0f))
            return float.Epsilon; // FLT_MIN in the C++

        float h1, h2, h3;
        float y;

        if ((ix + iz) % 2 == 1)
        {
            if (dx + dz < 1.0f)
            {
                h1 = TerrainHeight[ix, iz + 1];
                h2 = TerrainHeight[ix + 1, iz];
                h3 = TerrainHeight[ix, iz];

                float h12 = h1 + (h2 - h1) * dx;
                float h32 = h3 + (h2 - h3) * dx;
                y = h32 + (h12 - h32) * (dz / (1.0f - dx));
            }
            else
            {
                h1 = TerrainHeight[ix, iz + 1];
                h2 = TerrainHeight[ix + 1, iz];
                h3 = TerrainHeight[ix + 1, iz + 1];

                if (dx == 0.0f)
                    return h1;

                float h12 = h1 + (h2 - h1) * dx;
                float h13 = h1 + (h3 - h1) * dx;
                y = h13 + (h12 - h13) * ((1.0f - dz) / dx);
            }
        }
        else
        {
            if (dz > dx)
            {
                h1 = TerrainHeight[ix, iz + 1];
                h2 = TerrainHeight[ix + 1, iz + 1];
                h3 = TerrainHeight[ix, iz];

                float h12 = h1 + (h2 - h1) * dx;
                float h32 = h3 + (h2 - h3) * dx;
                y = h12 + (h32 - h12) * ((1.0f - dz) / (1.0f - dx));
            }
            else
            {
                h1 = TerrainHeight[ix, iz];
                h2 = TerrainHeight[ix + 1, iz];
                h3 = TerrainHeight[ix + 1, iz + 1];

                if (dx == 0.0f)
                    return h1;

                float h12 = h1 + (h2 - h1) * dx;
                float h13 = h1 + (h3 - h1) * dx;
                y = h12 + (h13 - h12) * (dz / dx);
            }
        }

        return y;
    }

    /// <summary>MAP::ObjectIntersect — line-of-sight test against collision geometry.</summary>
    public bool ObjectIntersect(float x1, float z1, float y1, float x2, float z2, float y2)
    {
        var from = new Vector3(x1, y1, z1);
        var to = new Vector3(x2, y2, z2);
        Vector3 dir = to - from;
        float speed = KoMath.Magnitude(dir);
        dir = KoMath.Normalized(dir);

        return ShapeManager.CheckCollision(from, dir, speed);
    }
}
