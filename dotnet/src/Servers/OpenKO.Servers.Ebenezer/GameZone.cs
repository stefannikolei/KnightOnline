namespace OpenKO.Servers.Ebenezer;

/// <summary>CRegion (Ebenezer Region.h): per-region user/NPC id sets (ordered like the C++ STLMap).</summary>
public sealed class ZoneRegion
{
    public readonly SortedSet<int> Users = [];
    public readonly SortedSet<int> Npcs = [];

    /// <summary>m_RegionItemArray: loot bundles keyed by bundle id.</summary>
    public readonly Dictionary<uint, ZoneItem> Items = [];
}

/// <summary>_OBJECT_EVENT slice the AISocket/respawn/object-event flows touch.</summary>
public sealed class ObjectEvent
{
    public short Index;
    public short Type;
    public int Belong;
    public short ControlNpcId;
    public byte Life;
    public float PosX;
    public float PosZ;
}

/// <summary>The _WARP_INFO fields the WIZ_WARP_LIST flow reads.</summary>
public sealed class WarpInfo
{
    public short WarpId;
    public byte[] WarpName = [];  // char[32], CP949, NUL-terminated
    public byte[] Announce = [];  // char[256]
    public uint Pay;
    public short Zone;
    public float X;
    public float Y;
    public float Z;
    public float R;
    public short Nation;
}

/// <summary>The _REGENE_EVENT fields KickOutZoneUser reads.</summary>
public sealed class RegeneEvent
{
    public float PosX;
    public float PosZ;
    public float AreaX;
    public float AreaZ;
}

/// <summary>_ZONE_ITEM: one dropped loot bundle (up to 6 stacks).</summary>
public sealed class ZoneItem
{
    public uint BundleIndex;
    public readonly int[] ItemId = new int[6];
    public readonly short[] Count = new short[6];
    public float X;
    public float Z;
    public float Y;
    public double Time;
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

    /// <summary>m_ObjectEventArray keyed by object index (filled by the SMD map loader).</summary>
    public readonly Dictionary<int, ObjectEvent> ObjectEvents = [];

    /// <summary>m_WarpArray keyed by warp id (filled by the SMD map loader).</summary>
    public readonly Dictionary<int, WarpInfo> Warps = [];

    /// <summary>m_ObjectRegeneArray in file order (filled by the SMD map loader).</summary>
    public readonly List<RegeneEvent> RegeneEvents = [];

    /// <summary>m_bType (ZONE_INFO): 1 common, 2 battle, 3 24h battle.</summary>
    public byte Type;

    /// <summary>m_sMaxUser (battle-zone capacity, C3DMap default).</summary>
    public short MaxUsers = 150;

    /// <summary>C3DMap::GetRegeneEvent.</summary>
    public RegeneEvent? GetRegeneEvent(int index)
        => index >= 0 && index < RegeneEvents.Count ? RegeneEvents[index] : null;

    /// <summary>m_wBundle: the next loot-bundle id (starts at 1, wraps at ZONEITEM_MAX).</summary>
    public uint Bundle = 1;

    /// <summary>m_fInitX / m_fInitZ (ZONE_INFO spawn point, warp fallback).</summary>
    public float InitX;
    public float InitZ;

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

    /// <summary>C3DMap::GetObjectEvent.</summary>
    public ObjectEvent? GetObjectEvent(int objectIndex)
        => ObjectEvents.GetValueOrDefault(objectIndex);

    /// <summary>C3DMap::RegionItemRemove — take one stack out of a bundle; drops empty bundles.</summary>
    public bool RegionItemRemove(int rx, int rz, uint bundleIndex, int itemId, int count)
    {
        if (!IsValidRegion(rx, rz))
            return false;

        ZoneItem? item = Regions[rx, rz].Items.GetValueOrDefault(bundleIndex);
        if (item is null)
            return false;

        bool found = false;
        for (int j = 0; j < 6; j++)
        {
            if (item.ItemId[j] == itemId && item.Count[j] == count)
            {
                item.ItemId[j] = 0;
                item.Count[j] = 0;
                found = true;
                break;
            }
        }

        if (found && item.ItemId.All(id => id == 0))
            Regions[rx, rz].Items.Remove(bundleIndex);

        return found;
    }

    /// <summary>C3DMap::RegionItemAdd — stores a loot bundle and advances the bundle counter.</summary>
    public bool RegionItemAdd(int rx, int rz, ZoneItem item)
    {
        if (!IsValidRegion(rx, rz))
            return false;

        Regions[rx, rz].Items[item.BundleIndex] = item;

        Bundle++;
        if (Bundle > 2_100_000_000) // ZONEITEM_MAX
            Bundle = 1;

        return true;
    }
}
