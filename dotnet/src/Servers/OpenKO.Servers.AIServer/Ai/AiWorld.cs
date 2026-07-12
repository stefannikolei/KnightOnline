using OpenKO.Data.Models;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// The AIServer world state that AIServerApp carried: user slots, the NPC map,
/// parties, zones and the startup table caches. All game-state mutation happens
/// on the zone/NPC single-writer loops (replacing the C++ mutex model).
/// </summary>
public sealed class AiWorld
{
    /// <summary>User slots by uid (MAX_USER), null when offline. _users[i].</summary>
    public readonly AiUser?[] Users = new AiUser?[AiConstants.MaxUser];

    /// <summary>NPCs by server serial (m_sNid). _npcMap.</summary>
    public readonly Dictionary<int, Npc> Npcs = [];

    /// <summary>Parties by party number. _partyMap.</summary>
    public readonly Dictionary<short, PartyGroup> Parties = [];

    /// <summary>Loaded zones in load order (zone index = list position). _zones.</summary>
    public readonly List<AiZone> Zones = [];

    // ---- startup table caches ----
    public Dictionary<int, Data.Models.Npc> NpcTable = [];
    public Dictionary<int, Data.Models.Npc> MonsterTable = [];
    public Dictionary<int, Magic> MagicTable = [];
    public Dictionary<int, MagicType1> MagicType1Table = [];
    public Dictionary<int, MagicType2> MagicType2Table = [];
    public Dictionary<int, MagicType3> MagicType3Table = [];
    public Dictionary<int, MagicType4> MagicType4Table = [];
    public Dictionary<int, MagicType7> MagicType7Table = [];
    public Dictionary<int, MakeItemGroup> MakeItemGroupTable = [];
    public Dictionary<int, MakeWeapon> MakeWeaponTable = [];
    public Dictionary<int, MakeDefensive> MakeDefensiveTable = [];
    public Dictionary<int, MakeItemGradeCode> MakeGradeItemTable = [];
    public Dictionary<int, MakeItemRareCode> MakeRareItemTable = [];
    public Dictionary<short, ZoneInfo> ZoneInfoTable = [];
    public List<MonsterItem> MonsterItemTable = [];

    /// <summary>myrand(min, max): inclusive random like the C++ helper.</summary>
    public Func<int, int, int> Rand { get; init; } = (min, max) => Random.Shared.Next(min, max + 1);

    /// <summary>TimeGet(): seconds with ms precision.</summary>
    public Func<double> Clock { get; init; } = () => Environment.TickCount64 / 1000.0;

    public AiZone? GetZoneByNumber(int zoneNumber)
        => Zones.FirstOrDefault(z => z.ZoneNumber == zoneNumber);

    public int GetZoneIndex(int zoneNumber)
    {
        for (int i = 0; i < Zones.Count; i++)
        {
            if (Zones[i].ZoneNumber == zoneNumber)
                return i;
        }

        return -1;
    }

    public AiUser? GetUser(int uid)
        => uid >= 0 && uid < Users.Length ? Users[uid] : null;
}
