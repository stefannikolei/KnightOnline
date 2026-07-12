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

    // ------------------------------------------------------------------
    //  Dungeon room events (MAP room members + Load/SetRoomEvent etc.)
    // ------------------------------------------------------------------

    /// <summary>m_byRoomEvent: 1 when this zone hosts room events.</summary>
    public byte RoomEventFlag;

    /// <summary>m_byRoomType (zone level): 0 auto-reset, 1 war event.</summary>
    public byte RoomType;

    /// <summary>m_byRoomStatus: 1 running, 2 resetting, 3 reset done.</summary>
    public byte RoomStatus = 1;

    /// <summary>m_byInitRoomCount: reset-delay tick counter.</summary>
    public byte InitRoomCount;

    /// <summary>m_sKarusRoom / m_sElmoradRoom: fort counts from the NATION lines.</summary>
    public short KarusRooms;
    public short ElmoradRooms;

    /// <summary>m_arRoomEventArray, keyed by room number (1-based).</summary>
    public readonly Dictionary<int, RoomEvent> Rooms = [];

    /// <summary>
    /// MAP::LoadRoomEvent — parses MAP/&lt;zone&gt;.evt. A missing file is success
    /// (true), a duplicate ROOM or a directive before any ROOM aborts (false).
    /// </summary>
    public bool LoadRoomEvents(string mapDirectory, int zoneNumber)
    {
        string path = Path.Combine(mapDirectory, zoneNumber + ".evt");
        if (!File.Exists(path))
            return true;

        return LoadRoomEventLines(File.ReadLines(path));
    }

    /// <summary>Parses the .evt line format (same tokenizer semantics as ParseSpace/atoi).</summary>
    public bool LoadRoomEventLines(IEnumerable<string> lines)
    {
        RoomEvent? room = null;
        int logic = 0, exec = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length <= 1)
                continue;

            if (line[0] is ';' or '/')
                continue;

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            string first = tokens[0];
            switch (first)
            {
                case "ROOM":
                    logic = 0;
                    exec = 0;
                    int roomNumber = Atoi(tokens, 1);
                    if (Rooms.ContainsKey(roomNumber))
                        return false; // duplicate event definition

                    room = SetRoomEvent(roomNumber);
                    break;

                case "TYPE":
                    RoomType = (byte)Atoi(tokens, 1);
                    break;

                case "L":
                case "O":
                case "END":
                    if (room is null)
                        return false;
                    break;

                case "E":
                    if (room is null)
                        return false;

                    room.Exec[exec].Number = (short)Atoi(tokens, 1);
                    room.Exec[exec].Option1 = (short)Atoi(tokens, 2);
                    room.Exec[exec].Option2 = (short)Atoi(tokens, 3);
                    exec++;
                    break;

                case "A":
                    if (room is null)
                        return false;

                    room.Logic[logic].Number = (short)Atoi(tokens, 1);
                    room.Logic[logic].Option1 = (short)Atoi(tokens, 2);
                    room.Logic[logic].Option2 = (short)Atoi(tokens, 3);
                    logic++;
                    room.Check = (byte)logic;
                    break;

                case "NATION":
                    if (room is null)
                        return false;

                    int nation = Atoi(tokens, 1);
                    if (nation == 1) // KARUS_ZONE
                        KarusRooms++;
                    else if (nation == 2) // ELMORAD_ZONE
                        ElmoradRooms++;
                    break;

                case "POS":
                    if (room is null)
                        return false;

                    room.InitMinX = Atoi(tokens, 1);
                    room.InitMinZ = Atoi(tokens, 2);
                    room.InitMaxX = Atoi(tokens, 3);
                    room.InitMaxZ = Atoi(tokens, 4);
                    break;

                case "POSEND":
                    if (room is null)
                        return false;

                    room.EndMinX = Atoi(tokens, 1);
                    room.EndMinZ = Atoi(tokens, 2);
                    room.EndMaxX = Atoi(tokens, 3);
                    room.EndMaxZ = Atoi(tokens, 4);
                    break;

                default:
                    // Unhandled opcodes only log a warning in the C++.
                    break;
            }
        }

        return true;
    }

    /// <summary>atoi on an optional token (missing token → 0, like ParseSpace + atoi).</summary>
    private static int Atoi(string[] tokens, int index)
    {
        if (index >= tokens.Length)
            return 0;

        string s = tokens[index];
        int value = 0, i = 0;
        bool negative = false;

        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            negative = s[i++] == '-';

        for (; i < s.Length && s[i] is >= '0' and <= '9'; i++)
            value = value * 10 + (s[i] - '0');

        return negative ? -value : value;
    }

    /// <summary>MAP::SetRoomEvent — allocates room <paramref name="number"/> (null on duplicate).</summary>
    public RoomEvent? SetRoomEvent(int number)
    {
        if (Rooms.ContainsKey(number))
            return null;

        var room = new RoomEvent
        {
            ZoneNumber = ZoneNumber,
            RoomNumber = (short)number,
        };
        Rooms[number] = room;
        return room;
    }

    /// <summary>
    /// MAP::IsRoomCheck — activates an init room the position falls into
    /// (status 1 → 2) or clears a goal-type room (status 2 → 3 when the
    /// position reaches the end rect). Returns the activated room number, 0 otherwise.
    /// </summary>
    public int IsRoomCheck(float fx, float fz, Func<double>? clock = null)
    {
        int nX = (int)fx;
        int nZ = (int)fz;
        int roomNumber = 0;

        for (int i = 1; i < Rooms.Count + 1; i++)
        {
            RoomEvent? room = Rooms.GetValueOrDefault(i);
            if (room is null)
                continue;

            if (room.Status == 3)
                continue;

            int minX = 0, minZ = 0, maxX = 0, maxZ = 0;

            if (room.Status == 1)
            {
                minX = room.InitMinX;
                minZ = room.InitMinZ;
                maxX = room.InitMaxX;
                maxZ = room.InitMaxZ;
            }
            else if (room.Status == 2)
            {
                // Only goal-movement rooms (first condition 4) track the end rect.
                if (room.Logic[0].Number != 4)
                    continue;

                minX = room.EndMinX;
                minZ = room.EndMinZ;
                maxX = room.EndMaxX;
                maxZ = room.EndMaxZ;
            }

            bool flag1 = minX < maxX
                ? nX >= minX && nX < maxX
                : nX >= maxX && nX < minX;
            bool flag2 = minZ < maxZ
                ? nZ >= minZ && nZ < maxZ
                : nZ >= maxZ && nZ < minZ;

            if (!flag1 || !flag2)
                continue;

            if (room.Status == 1)
            {
                room.Status = 2;
                room.DelayTime = clock?.Invoke() ?? 0.0;
                roomNumber = i;
            }
            else if (room.Status == 2)
            {
                room.Status = 3;
            }

            return roomNumber;
        }

        return roomNumber;
    }

    /// <summary>
    /// MAP::IsRoomStatusCheck — drives the all-cleared → resetting → reset-done
    /// cycle. Returns true on each state transition (skipping MainRoom that tick).
    /// </summary>
    public bool IsRoomStatusCheck()
    {
        int totalRooms = Rooms.Count + 1;
        int clearRooms = 1;

        if (RoomStatus == 2)
            InitRoomCount++;

        for (int i = 1; i < totalRooms; i++)
        {
            RoomEvent? room = Rooms.GetValueOrDefault(i);
            if (room is null)
                continue;

            if (RoomStatus == 1)
            {
                if (room.Status == 3)
                    clearRooms++;

                // Auto-reset zones only (war-event zones reset via ResetBattleZone).
                if (RoomType == 0 && totalRooms == clearRooms)
                {
                    RoomStatus = 2;
                    return true;
                }
            }
            else if (RoomStatus == 2)
            {
                if (InitRoomCount >= 10)
                {
                    room.InitializeRoom();
                    clearRooms++;

                    if (totalRooms == clearRooms)
                    {
                        RoomStatus = 3;
                        return true;
                    }
                }
            }
            else if (RoomStatus == 3)
            {
                RoomStatus = 1;
                InitRoomCount = 0;
                return true;
            }
        }

        return false;
    }

    /// <summary>MAP::InitializeRoom — hard reset of every room (ResetBattleZone).</summary>
    public void InitializeRooms()
    {
        for (int i = 1; i < Rooms.Count + 1; i++)
        {
            RoomEvent? room = Rooms.GetValueOrDefault(i);
            if (room is null)
                continue;

            room.InitializeRoom();
            RoomStatus = 1;
            InitRoomCount = 0;
        }
    }
}
