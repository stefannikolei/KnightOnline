using Microsoft.Extensions.Logging;
using OpenKO.Data.Models;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of <c>AIServerApp::LoadNpcPosTable</c>: expands every K_NPCPOS row into
/// <c>NumNPC</c> NPC instances, resolving the definition against the monster
/// table (ActType &lt; 100) or the NPC table (ActType &gt;= 100 → move type − 100),
/// randomizing spawn coordinates inside the spawn rect and parsing the path
/// blob (4+4 digit x/z pairs).
/// </summary>
public sealed class NpcSpawner(AiWorld world, ILogger logger)
{
    public const int UnifyZone = 0;

    /// <summary>Next NPC serial; the C++ seeds this with _mapEventNpcCount.</summary>
    public int NextSerial { get; set; }

    /// <summary>
    /// Returns false on the fatal conditions the C++ aborts startup with
    /// (missing path data, unknown zone).
    /// </summary>
    public bool SpawnAll(IEnumerable<NpcPos> rows, int serverZoneType, Func<short, int> getServerNumber)
    {
        foreach (NpcPos row in rows)
        {
            int pathSerial = 1;

            int serverNum = getServerNumber(row.ZoneId);
            if (serverZoneType != serverNum && serverZoneType != UnifyZone)
                continue;

            for (int j = 0; j < row.NumNpc; j++)
            {
                var npc = new Npc
                {
                    Nid = (short)NextSerial,
                    Sid = (short)row.NpcId,
                    MoveType = row.ActType,
                    InitMoveType = row.ActType,
                    Direction = (byte)row.Direction,
                };
                NextSerial++;

                Data.Models.Npc? table;
                if (row.ActType < 100)
                {
                    table = world.MonsterTable.GetValueOrDefault(npc.Sid);
                }
                else
                {
                    npc.MoveType = (byte)(row.ActType - 100);
                    table = world.NpcTable.GetValueOrDefault(npc.Sid);
                }

                npc.BattlePos = 0;
                if (npc.MoveType >= 2)
                {
                    npc.BattlePos = (byte)world.Rand(1, 3);
                    npc.PathCounter = (byte)pathSerial++;
                }

                if (table is null)
                {
                    logger.LogError("NpcSpawner: npc not found [serial={Serial}, npcId={NpcId}]", npc.Nid, npc.Sid);
                    break;
                }

                npc.Load(table, transformSpeeds: true);

                npc.CurZone = row.ZoneId;

                // Random position inside the spawn rect (LeftX/RightX, TopZ/BottomZ).
                npc.CurX = RandomCoordinate(row.LeftX, row.RightX);
                npc.CurY = 0;
                npc.CurZ = RandomCoordinate(row.TopZ, row.BottomZ);

                short respawnTime = row.RespawnTime;
                if (respawnTime < 15)
                {
                    logger.LogWarning(
                        "NpcSpawner: RegTime below minimum of 15s [npcId={NpcId}, serial={Serial}, npcName={Name}, RegTime={RegTime}]",
                        npc.Sid, npc.Nid, npc.Name, respawnTime);
                    respawnTime = 30; // C++ TODO notes this deviates from official 15
                }

                npc.RegenTime = respawnTime * 1000;
                npc.MaxPathCount = row.PathPointCount;

                if (npc.MoveType is 2 or 3 && (row.PathPointCount == 0 || row.Path.Length == 0))
                {
                    logger.LogError(
                        "NpcSpawner: path-moving NPC without path [zoneId={Zone} serial={Serial} npcId={NpcId} moveType={MoveType}]",
                        row.ZoneId, npc.Nid, npc.Sid, npc.MoveType);
                    return false;
                }

                if (row.PathPointCount != 0 && row.Path.Length > 0)
                {
                    // Path blob: PathPointCount points of ("%04d%04d", x, z).
                    if (row.PathPointCount * 8 > row.Path.Length)
                    {
                        logger.LogError(
                            "NpcSpawner: path shorter than PathPointCount [zoneId={Zone} serial={Serial} npcId={NpcId}]",
                            row.ZoneId, npc.Nid, npc.Sid);
                        return false;
                    }

                    for (int l = 0; l < row.PathPointCount; l++)
                    {
                        npc.PathList[l].X = ParsePathValue(row.Path, l * 8);
                        npc.PathList[l].Z = ParsePathValue(row.Path, l * 8 + 4);
                    }
                }

                npc.InitMinX = npc.LimitMinX = row.LeftX;
                npc.InitMinY = npc.LimitMinZ = row.TopZ;
                npc.InitMaxX = npc.LimitMaxX = row.RightX;
                npc.InitMaxY = npc.LimitMaxZ = row.BottomZ;

                npc.DungeonFamily = row.DungeonFamily;
                npc.SpecialType = row.SpecialType;
                npc.RegenType = row.RegenType;
                npc.TrapNumber = row.TrapNumber;

                if (npc.DungeonFamily > 0)
                {
                    npc.LimitMinX = row.LimitMinX;
                    npc.LimitMinZ = row.LimitMinZ;
                    npc.LimitMaxX = row.LimitMaxX;
                    npc.LimitMaxZ = row.LimitMaxZ;
                }

                int zoneIndex = world.GetZoneIndex(npc.CurZone);
                if (zoneIndex < 0)
                {
                    logger.LogError("NpcSpawner: NPC invalid zone [npcId={NpcId}, zoneId={Zone}]", npc.Sid, npc.CurZone);
                    return false;
                }

                npc.ZoneIndex = (short)zoneIndex;

                if (!world.Npcs.TryAdd(npc.Nid, npc))
                    logger.LogError("NpcSpawner: Npc PutData Fail [serial={Serial}]", npc.Nid);

                // Register dungeon-family NPCs with their zone room (fatal when
                // the .evt did not define the room, like the C++).
                AiZone zone = world.Zones[zoneIndex];
                if (zone.RoomEventFlag > 0 && npc.DungeonFamily > 0)
                {
                    RoomEvent? room = zone.Rooms.GetValueOrDefault(npc.DungeonFamily);
                    if (room is null)
                    {
                        logger.LogError(
                            "NpcSpawner: no RoomEvent for NPC dungeonFamily [serial={Serial} npcId={NpcId} npcName={Name} dungeonFamily={Family} zoneId={Zone}]",
                            npc.Nid + 10000, npc.Sid, npc.Name, npc.DungeonFamily, npc.ZoneIndex);
                        return false;
                    }

                    room.World ??= world;
                    room.RoomNpcs.Add(npc.Nid);
                }
            }
        }

        return true;
    }

    private float RandomCoordinate(int a, int b)
    {
        if (Math.Abs(a - b) <= 1)
            return a;

        return a < b ? world.Rand(a, b) : world.Rand(b, a);
    }

    /// <summary>atoi on a fixed 4-char slice (tolerates non-digit tails like the C++).</summary>
    private static short ParsePathValue(string path, int offset)
    {
        int value = 0;
        for (int i = offset; i < offset + 4 && i < path.Length; i++)
        {
            char c = path[i];
            if (c is < '0' or > '9')
                break;
            value = value * 10 + (c - '0');
        }

        return (short)value;
    }
}
