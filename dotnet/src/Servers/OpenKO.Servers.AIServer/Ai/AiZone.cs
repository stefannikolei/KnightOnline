using OpenKO.GameData.Maps;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>Port of <c>CRegion</c>: per-region user/NPC id sets.</summary>
public sealed class Region
{
    public readonly HashSet<int> Users = [];
    public readonly HashSet<int> Npcs = [];
    public byte Moving;
}

/// <summary>
/// One zone of the AIServer: the loaded map plus the region grid the C++ MAP
/// carried (region size = VIEW_DIST). Region mutations are serialized by the
/// zone's single-writer loop (replacing the C++ g_region_mutex).
/// </summary>
public sealed class AiZone
{
    public required int ServerNo { get; init; }

    public required int ZoneNumber { get; init; }

    public required GameMap Map { get; init; }

    public required Region[,] Regions { get; init; }

    public int RegionsX => Regions.GetLength(0);

    public int RegionsZ => Regions.GetLength(1);

    public static AiZone Create(int serverNo, int zoneNumber, GameMap map)
    {
        int mapWidth = (int)map.ShapeManager.MapWidth;
        int count = mapWidth / AiConstants.ViewDistance + 1;

        var regions = new Region[count, count];
        for (int x = 0; x < count; x++)
        {
            for (int z = 0; z < count; z++)
                regions[x, z] = new Region();
        }

        return new AiZone
        {
            ServerNo = serverNo,
            ZoneNumber = zoneNumber,
            Map = map,
            Regions = regions,
        };
    }

    public bool IsValidRegion(int rx, int rz)
        => rx >= 0 && rz >= 0 && rx < RegionsX && rz < RegionsZ;

    /// <summary>MAP::RegionUserAdd / RegionUserRemove.</summary>
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

    /// <summary>MAP::RegionNpcAdd / RegionNpcRemove.</summary>
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
