using OpenKO.Data.Models;

namespace OpenKO.Servers.Ebenezer;

/// <summary>One [ZONE_INFO] SERVER_XX entry (_ZONE_SERVERINFO).</summary>
public sealed record ZoneServerInfo(short ServerNo, string ServerIp, short Port);

/// <summary>
/// EbenezerApp world/user bookkeeping (stage-4 slices): the user slots by socket
/// id, account/character lookups, the zone/server topology and the startup
/// table caches.
/// </summary>
public sealed partial class EbenezerWorld
{
    public const int MaxUser = 3000; // MAX_USER (Ebenezer Define.h)

    /// <summary>User slots by socket id.</summary>
    public readonly GameUser?[] Users = new GameUser?[MaxUser];

    /// <summary>m_nServerNo ([ZONE_INFO] MY_INFO).</summary>
    public short ServerNo = 1;

    /// <summary>m_ServerArray ([ZONE_INFO] SERVER_XX, port = 15000 + no).</summary>
    public readonly Dictionary<short, ZoneServerInfo> ServerInfos = [];

    /// <summary>Loaded zones (m_ZoneArray).</summary>
    public readonly List<GameZone> Zones = [];

    /// <summary>m_NpcMap: the NPC mirror filled from the AI server.</summary>
    public readonly Dictionary<int, GameNpc> Npcs = [];

    /// <summary>m_bPointCheckFlag: NPC pointers valid (set after the AI sync).</summary>
    public bool PointCheckFlag;

    /// <summary>m_CoefficientTableMap (COEFFICIENT, keyed by class).</summary>
    public Dictionary<short, Coefficient> CoefficientTable = [];

    /// <summary>m_ItemTableMap (ITEM, keyed by item number).</summary>
    public Dictionary<int, Item> ItemTable = [];

    /// <summary>m_LevelUpTableArray (LEVEL_UP): required exp keyed by level.</summary>
    public Dictionary<int, int> LevelUpTable = [];

    /// <summary>m_MagicTableMap (MAGIC) — loaded with the magic slice.</summary>
    public Dictionary<int, Magic> MagicTable = [];

    /// <summary>m_MagicType1TableMap (MAGIC_TYPE1).</summary>
    public Dictionary<int, MagicType1> MagicType1Table = [];

    /// <summary>m_MagicType2TableMap (MAGIC_TYPE2).</summary>
    public Dictionary<int, MagicType2> MagicType2Table = [];

    /// <summary>m_MagicType3TableMap (MAGIC_TYPE3).</summary>
    public Dictionary<int, MagicType3> MagicType3Table = [];

    /// <summary>m_MagicType4TableMap (MAGIC_TYPE4).</summary>
    public Dictionary<int, MagicType4> MagicType4Table = [];

    /// <summary>m_MagicType5TableMap (MAGIC_TYPE5).</summary>
    public Dictionary<int, MagicType5> MagicType5Table = [];

    /// <summary>m_MagicType8TableMap (MAGIC_TYPE8).</summary>
    public Dictionary<int, MagicType8> MagicType8Table = [];

    /// <summary>myrand_generic(min, max) — inclusive, swaps a reversed range; injectable for deterministic tests.</summary>
    public Func<int, int, int> Rand = (min, max) =>
    {
        if (min == max)
            return min;

        if (min > max)
            (min, max) = (max, min);

        return Random.Shared.Next(min, max + 1);
    };

    /// <summary>
    /// CUser::GetHitRate — the banded 1..10000 hit roll. Lives on the world so
    /// the NPC-cast magic path (whose CMagicProcess has no source user) can use
    /// it too, exactly like the C++ calling through a null CUser pointer.
    /// </summary>
    public byte GetHitRate(float rate)
    {
        int random = Rand(1, 10000);

        (int great, int success, int normal) = rate switch
        {
            >= 5.0f => (3500, 7500, 9800),
            >= 3.0f => (2500, 6000, 9600),
            >= 2.0f => (2000, 5000, 9400),
            >= 1.25f => (1500, 4000, 9200),
            >= 0.8f => (1000, 3000, 9000),
            >= 0.5f => (800, 2500, 8000),
            >= 0.33f => (600, 2000, 7000),
            >= 0.2f => (400, 1500, 6000),
            _ => (200, 1000, 5000),
        };

        if (random <= great)
            return 1; // GREAT_SUCCESS
        if (random <= success)
            return 2; // SUCCESS
        if (random <= normal)
            return 3; // NORMAL

        return 4; // FAIL
    }

    /// <summary>m_sKarusDead / m_sElmoradDead (Wednesday battle-event counters).</summary>
    public short KarusDead;
    public short ElmoradDead;

    /// <summary>m_ServerResourceTableMap (SERVER_RESOURCE message templates).</summary>
    public Dictionary<int, string> ServerResources = [];

    private ushort _serialCounter;

    /// <summary>
    /// fmt::format_db_resource — resolves the sprintf template and substitutes
    /// %s/%d style placeholders; falls back to the resource id like the C++.
    /// </summary>
    public string FormatResource(int resourceId, params object?[] args)
    {
        if (!ServerResources.TryGetValue(resourceId, out string? template))
            return resourceId.ToString();

        var result = new System.Text.StringBuilder(template.Length + 32);
        int argIndex = 0;

        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '%' || i + 1 >= template.Length)
            {
                result.Append(template[i]);
                continue;
            }

            char spec = template[++i];
            if (spec == '%')
            {
                result.Append('%');
            }
            else if (spec is 's' or 'd' or 'i' or 'u' or 'c' or 'f')
            {
                if (argIndex >= args.Length)
                    return resourceId.ToString(); // invalid args, like the fmt catch

                result.Append(args[argIndex++]);
            }
            else
            {
                result.Append('%').Append(spec);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// EbenezerApp::GenerateItemSerial — server number + timestamp + counter
    /// packed into the 8 bytes. Injectable for deterministic tests.
    /// </summary>
    public Func<long> GenerateItemSerial;

    public EbenezerWorld()
    {
        GenerateItemSerial = () =>
        {
            DateTime now = DateTime.Now;
            ushort increase = _serialCounter++;

            Span<byte> bytes = stackalloc byte[8];
            bytes[7] = (byte)ServerNo;
            bytes[6] = (byte)(now.Year % 100);
            bytes[5] = (byte)now.Month;
            bytes[4] = (byte)now.Day;
            bytes[3] = (byte)now.Hour;
            bytes[2] = (byte)now.Minute;
            bytes[1] = (byte)(increase >> 8);
            bytes[0] = (byte)increase;

            return BitConverter.ToInt64(bytes);
        };
    }

    /// <summary>m_HomeTableMap (HOME, keyed by nation).</summary>
    public Dictionary<byte, Home> HomeTable = [];

    // ---- game time/weather ([TIMER] section + WIZ_TIME updates) ----
    public short Year = 1;
    public short Month = 1;
    public short Date = 1;
    public short Hour = 1;
    public short Minute;
    public short Weather = 1;
    public short WeatherAmount;

    /// <summary>m_ppNotice[20] (Notice.txt lines).</summary>
    public readonly string[] Notices = new string[20];

    /// <summary>Send_AIServer(zone, buf, len) — wired once the AISocket lands (stage 4.4).</summary>
    public Action<int, byte[]>? SendToAiServer;

    /// <summary>m_byOldVictory: winner of the last national war.</summary>
    public byte OldVictory;

    /// <summary>m_byBattleOpen: NO_BATTLE(0), NATION_BATTLE(1), SNOW_BATTLE(2).</summary>
    public byte BattleOpen;

    /// <summary>m_iPacketCount: sequence stamped into WIZ_SEL_CHAR agent requests.</summary>
    public int PacketCount;

    /// <summary>
    /// Sink for the WIZ_ITEM_LOG/WIZ_DATASAVE messages the C++ pushed onto the
    /// ItemManager's ITEMLOG_SEND queue (wired to an IItemLogSource by the host).
    /// </summary>
    public Action<byte[]>? ItemLogSink;

    /// <summary>EbenezerApp::GetUserPtr(name, NameType::Account) — case-insensitive.</summary>
    public GameUser? GetUserByAccount(string accountId)
    {
        foreach (GameUser? user in Users)
        {
            if (user is not null
                && user.AccountId.Length > 0
                && string.Equals(user.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                return user;
        }

        return null;
    }

    /// <summary>EbenezerApp::GetUserPtr(name, NameType::Character) — case-insensitive.</summary>
    public GameUser? GetUserByCharId(string charId)
    {
        foreach (GameUser? user in Users)
        {
            if (user is not null
                && user.UserData is { } data
                && data.CharId.Length > 0
                && string.Equals(data.CharId, charId, StringComparison.OrdinalIgnoreCase))
                return user;
        }

        return null;
    }

    /// <summary>EbenezerApp::GetMapByID.</summary>
    public GameZone? GetZoneById(int zoneId)
        => Zones.FirstOrDefault(z => z.ZoneNumber == zoneId);

    /// <summary>EbenezerApp::GetMapByIndex.</summary>
    public GameZone? GetZoneByIndex(int zoneIndex)
        => zoneIndex >= 0 && zoneIndex < Zones.Count ? Zones[zoneIndex] : null;

    /// <summary>EbenezerApp::GetZoneIndex (-1 when the zone is not on this server).</summary>
    public int GetZoneIndex(int zoneId)
    {
        for (int i = 0; i < Zones.Count; i++)
        {
            if (Zones[i].ZoneNumber == zoneId)
                return i;
        }

        return -1;
    }

    /// <summary>Claims the smallest free socket slot, -1 when the server is full.</summary>
    public short Register(Func<short, GameUser> factory)
    {
        for (short i = 0; i < Users.Length; i++)
        {
            if (Users[i] is null)
            {
                Users[i] = factory(i);
                return i;
            }
        }

        return -1;
    }

    public void Unregister(short socketId)
    {
        if (socketId >= 0 && socketId < Users.Length)
            Users[socketId] = null;
    }
}
