namespace OpenKO.Servers.Ebenezer;

/// <summary>CRegion (Ebenezer Region.h): per-region user/NPC id sets (ordered like the C++ STLMap).</summary>
public sealed class ZoneRegion
{
    public readonly SortedSet<int> Users = [];
    public readonly SortedSet<int> Npcs = [];
}

/// <summary>
/// Port of the C3DMap fields the game flow needs: zone/server numbers, the map
/// extent and the VIEW_DISTANCE region grid. The terrain/collision data
/// (N3ShapeMgr) attaches via OpenKO.GameData when maps are loaded from SMD.
/// </summary>
public sealed class GameZone
{
    public const int ViewDistance = 48; // VIEW_DISTANCE

    public short ServerNo { get; }

    public short ZoneNumber { get; }

    /// <summary>Walkable extent in meters ((mapSize-1)*unitDist); 0 skips position checks.</summary>
    public float MapSize { get; }

    public ZoneRegion[,] Regions { get; }

    public GameZone(short serverNo, short zoneNumber, float mapSize = 0f)
    {
        ServerNo = serverNo;
        ZoneNumber = zoneNumber;
        MapSize = mapSize;

        int count = mapSize > 0f ? (int)(mapSize / ViewDistance) + 1 : 1;
        Regions = new ZoneRegion[count, count];
        for (int x = 0; x < count; x++)
        {
            for (int z = 0; z < count; z++)
                Regions[x, z] = new ZoneRegion();
        }
    }

    public int XRegionMax => Regions.GetLength(0) - 1;

    public int ZRegionMax => Regions.GetLength(1) - 1;

    public bool IsValidRegion(int rx, int rz)
        => rx >= 0 && rz >= 0 && rx <= XRegionMax && rz <= ZRegionMax;

    /// <summary>C3DMap::IsValidPosition.</summary>
    public bool IsValidPosition(float x, float z)
        => MapSize <= 0f || (x >= 0f && x < MapSize && z >= 0f && z < MapSize);

    public void RegionUserAdd(int rx, int rz, int uid)
    {
        if (IsValidRegion(rx, rz))
            Regions[rx, rz].Users.Add(uid);
    }

    public void RegionUserRemove(int rx, int rz, int uid)
    {
        if (IsValidRegion(rx, rz))
            Regions[rx, rz].Users.Remove(uid);
    }

    public void RegionNpcAdd(int rx, int rz, int nid)
    {
        if (IsValidRegion(rx, rz))
            Regions[rx, rz].Npcs.Add(nid);
    }

    public void RegionNpcRemove(int rx, int rz, int nid)
    {
        if (IsValidRegion(rx, rz))
            Regions[rx, rz].Npcs.Remove(nid);
    }
}
