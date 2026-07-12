using OpenKO.Data.Models;

namespace OpenKO.Servers.Ebenezer;

/// <summary>One [ZONE_INFO] SERVER_XX entry (_ZONE_SERVERINFO).</summary>
public sealed record ZoneServerInfo(short ServerNo, string ServerIp, short Port);

/// <summary>_PARTY_GROUP (GameDefine.h): up to eight members per party.</summary>
public sealed class PartyGroup
{
    public ushort Index;
    public readonly short[] Uid = [-1, -1, -1, -1, -1, -1, -1, -1];
    public readonly short[] MaxHp = new short[8];
    public readonly short[] Hp = new short[8];
    public readonly byte[] Level = new byte[8];
    public readonly short[] Class = new short[8];
    public byte ItemRouting;
}

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

    /// <summary>m_PartyMap keyed by the party index.</summary>
    public readonly Dictionary<int, PartyGroup> Parties = [];

    /// <summary>m_KnightsMap keyed by the clan index.</summary>
    public readonly Dictionary<int, KnightsClan> Knights = [];

    /// <summary>m_nServerGroup ([ZONE_INFO] GROUP_INFO; 2 blocks clan creation).</summary>
    public int ServerGroup;

    /// <summary>EbenezerApp::GetKnightsGrade — clan grade from the point total.</summary>
    public static byte GetKnightsGrade(int points)
    {
        int clanPoints = points / 24;

        return clanPoints switch
        {
            >= 20000 => 1,
            >= 10000 => 2,
            >= 5000 => 3,
            >= 2000 => 4,
            _ => 5,
        };
    }

    /// <summary>EbenezerApp::Send_KnightsMember (zone 100 = every zone).</summary>
    public void SendKnightsMember(int index, ReadOnlySpan<byte> buf, int zone = 100)
    {
        if (index <= 0 || !Knights.ContainsKey(index))
            return;

        foreach (GameUser? user in Users)
        {
            if (user?.UserData is not { } data || data.Knights != index)
                continue;

            if (zone != 100 && data.Zone != zone)
                continue;

            user.Send(buf);
        }
    }

    /// <summary>CKnightsManager::AddKnightsUser — claim the first free member slot.</summary>
    public bool AddKnightsUser(int knightsId, string charId)
    {
        KnightsClan? clan = Knights.GetValueOrDefault(knightsId);
        if (clan is null)
            return false;

        for (int i = 0; i < KnightsClan.MaxClan; i++)
        {
            if (clan.Users[i].Used != 0)
                continue;

            clan.Users[i].Used = 1;
            clan.Users[i].UserName = charId;
            return true;
        }

        return false;
    }

    /// <summary>CKnightsManager::RemoveKnightsUser.</summary>
    public bool RemoveKnightsUser(int knightsId, string charId)
    {
        KnightsClan? clan = Knights.GetValueOrDefault(knightsId);
        if (clan is null)
            return false;

        for (int i = 0; i < KnightsClan.MaxClan; i++)
        {
            if (clan.Users[i].Used == 0)
                continue;

            if (string.Equals(clan.Users[i].UserName, charId, StringComparison.Ordinal))
            {
                clan.Users[i].Used = 0;
                clan.Users[i].UserName = string.Empty;
                return true;
            }
        }

        return false;
    }

    /// <summary>CKnightsManager::SetKnightsUser — add unless already present.</summary>
    public void SetKnightsUser(int knightsId, string charId)
    {
        KnightsClan? clan = Knights.GetValueOrDefault(knightsId);
        if (clan is null)
            return;

        for (int i = 0; i < KnightsClan.MaxClan; i++)
        {
            if (clan.Users[i].Used == 0)
                continue;

            if (string.Equals(clan.Users[i].UserName, charId, StringComparison.Ordinal))
                return;
        }

        AddKnightsUser(knightsId, charId);
    }

    /// <summary>m_sPartyIndex — the next party id (wraps at 32767).</summary>
    public short NextPartyIndex;

    /// <summary>The recurring WIZ_PARTY/PARTY_STATUSCHANGE broadcast (type 1 = DoT, 2 = buff).</summary>
    public void SendPartyStatusChange(int party, short uid, byte type, byte flag)
    {
        if (party == -1)
            return;

        var buffer = new byte[8];
        var writer = new OpenKO.Network.PacketWriter(buffer);
        writer.SetByte(0x2F); // WIZ_PARTY
        writer.SetByte(0x09); // PARTY_STATUSCHANGE
        writer.SetShort(uid);
        writer.SetByte(type);
        writer.SetByte(flag);
        SendPartyMember(party, writer.Written);
    }

    /// <summary>EbenezerApp::Send_PartyMember.</summary>
    public void SendPartyMember(int party, ReadOnlySpan<byte> buf)
    {
        if (party < 0)
            return;

        PartyGroup? group = Parties.GetValueOrDefault(party);
        if (group is null)
            return;

        for (int i = 0; i < 8; i++)
        {
            GameUser? user = group.Uid[i] >= 0 && group.Uid[i] < Users.Length ? Users[group.Uid[i]] : null;
            user?.Send(buf);
        }
    }

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

    /// <summary>m_StartPositionTableMap (START_POSITION, keyed by zone).</summary>
    public Dictionary<short, StartPosition> StartPositionTable = [];

    /// <summary>g_serverdown_flag: blocks zone changes during shutdown.</summary>
    public bool ServerDownFlag;

    /// <summary>m_sDiscount: 1 = winner discount, 2 = everyone.</summary>
    public short Discount;

    /// <summary>m_bVictory: winning nation of the running battle event.</summary>
    public byte Victory;

    /// <summary>m_bKarusFlag / m_bElmoradFlag: captured battle-zone flags.</summary>
    public byte KarusFlag;
    public byte ElmoradFlag;

    /// <summary>m_byKarusOpenFlag / m_byElmoradOpenFlag: invasion gates.</summary>
    public byte KarusOpenFlag;
    public byte ElmoradOpenFlag;

    /// <summary>m_strKarusCaptain / m_strElmoradCaptain.</summary>
    public string KarusCaptain = string.Empty;
    public string ElmoradCaptain = string.Empty;

    // Battlezone announcement types (Ebenezer Define.h).
    public const byte BattlezoneOpen = 0x00;
    public const byte BattlezoneClose = 0x01;
    public const byte DeclareWinner = 0x02;
    public const byte DeclareLoser = 0x03;
    public const byte DeclareBan = 0x04;
    public const byte KarusCaptainNotify = 0x05;
    public const byte ElmoradCaptainNotify = 0x06;
    public const byte KarusCaptainDepriveNotify = 0x07;
    public const byte ElmoradCaptainDepriveNotify = 0x08;
    public const byte SnowBattlezoneOpen = 0x09;

    private const int NumFlagVictory = 4; // NUM_FLAG_VICTORY
    private const int AwardGold = 5000;   // AWARD_GOLD

    /// <summary>EbenezerApp::BattleZoneVictoryCheck — flags reach 4 → winner + gold.</summary>
    public void BattleZoneVictoryCheck()
    {
        if (KarusFlag >= NumFlagVictory)
            Victory = 1; // KARUS
        else if (ElmoradFlag >= NumFlagVictory)
            Victory = 2; // ELMORAD
        else
            return;

        Announcement(DeclareWinner);

        foreach (GameUser? user in Users)
        {
            if (user?.UserData is { } data
                && data.Nation == Victory
                && data.Zone == data.Nation)
                data.Gold += AwardGold;
        }
    }

    /// <summary>EbenezerApp::Announcement — resource-formatted WIZ_CHAT broadcast.</summary>
    public void Announcement(byte type, int nation = 0, byte chatType = 8)
    {
        string chat;
        switch (type)
        {
            case BattlezoneOpen:
            case SnowBattlezoneOpen:
                chat = FormatResource(105); // IDP_BATTLEZONE_OPEN
                break;

            case DeclareWinner:
                if (Victory == 1)
                    chat = FormatResource(106, ElmoradDead, KarusDead); // IDP_KARUS_VICTORY
                else if (Victory == 2)
                    chat = FormatResource(107, KarusDead, ElmoradDead); // IDP_ELMORAD_VICTORY
                else
                    return;
                break;

            case DeclareLoser:
                if (Victory == 1)
                    chat = FormatResource(130, KarusDead, ElmoradDead); // IDS_ELMORAD_LOSER
                else if (Victory == 2)
                    chat = FormatResource(131, ElmoradDead, KarusDead); // IDS_KARUS_LOSER
                else
                    return;
                break;

            case DeclareBan:
                chat = FormatResource(132); // IDS_BANISH_USER
                break;

            case BattlezoneClose:
                chat = FormatResource(133); // IDS_BATTLE_CLOSE
                break;

            case KarusCaptainNotify:
                chat = FormatResource(140, KarusCaptain); // IDS_KARUS_CAPTAIN
                break;

            case ElmoradCaptainNotify:
                chat = FormatResource(141, ElmoradCaptain); // IDS_ELMO_CAPTAIN
                break;

            case KarusCaptainDepriveNotify:
                chat = FormatResource(142, KarusCaptain); // IDS_KARUS_CAPTAIN_DEPRIVE
                break;

            case ElmoradCaptainDepriveNotify:
                chat = FormatResource(143, ElmoradCaptain); // IDS_ELMO_CAPTAIN_DEPRIVE
                break;

            default:
                return;
        }

        chat = FormatResource(126, chat); // IDP_ANNOUNCEMENT

        byte[] text = System.Text.Encoding.Latin1.GetBytes(chat);
        var buffer = new byte[10 + text.Length];
        var writer = new OpenKO.Network.PacketWriter(buffer);
        writer.SetByte(0x10); // WIZ_CHAT
        writer.SetByte(chatType);
        writer.SetByte(1);
        writer.SetShort(-1);
        writer.SetByte(0); // sender name length
        writer.SetString2(text);

        foreach (GameUser? user in Users)
        {
            if (user is null || user.State != ConnectionState.GameStart)
                continue;

            if (nation == 0 || nation == user.UserData?.Nation)
                user.Send(writer.Written);
        }
    }

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

    /// <summary>
    /// The WIZ_DATASAVE trigger the C++ pushed onto the Aujard queue: the host
    /// wires this to IDbAgent.UpdateUserAsync(PacketSave) on the game loop.
    /// </summary>
    public Action<GameUser>? SaveUserData;

    /// <summary>
    /// The WIZ_KICKOUT forward for accounts not on this server (the C++ asked
    /// Aujard to log the account out); wired to IDbAgent.AccountLogoutAsync.
    /// </summary>
    public Action<string>? KickOutRequested;

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
