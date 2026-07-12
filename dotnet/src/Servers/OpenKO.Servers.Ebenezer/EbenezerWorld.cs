using OpenKO.Data.Models;

namespace OpenKO.Servers.Ebenezer;

/// <summary>One [ZONE_INFO] SERVER_XX entry (_ZONE_SERVERINFO).</summary>
public sealed record ZoneServerInfo(short ServerNo, string ServerIp, short Port);

/// <summary>Zone metadata of one loaded map (the C3DMap fields the pre-game flow needs).</summary>
public sealed record ZoneMeta(short ServerNo, short ZoneNumber);

/// <summary>
/// EbenezerApp world/user bookkeeping (stage-4 slices): the user slots by socket
/// id, account/character lookups, the zone/server topology and the startup
/// table caches.
/// </summary>
public sealed class EbenezerWorld
{
    public const int MaxUser = 3000; // MAX_USER (Ebenezer Define.h)

    /// <summary>User slots by socket id.</summary>
    public readonly GameUser?[] Users = new GameUser?[MaxUser];

    /// <summary>m_nServerNo ([ZONE_INFO] MY_INFO).</summary>
    public short ServerNo = 1;

    /// <summary>m_ServerArray ([ZONE_INFO] SERVER_XX, port = 15000 + no).</summary>
    public readonly Dictionary<short, ZoneServerInfo> ServerInfos = [];

    /// <summary>Loaded zones (m_ZoneArray metadata subset).</summary>
    public readonly List<ZoneMeta> Zones = [];

    /// <summary>m_CoefficientTableMap (COEFFICIENT, keyed by class).</summary>
    public Dictionary<short, Coefficient> CoefficientTable = [];

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
    public ZoneMeta? GetZoneById(int zoneId)
        => Zones.FirstOrDefault(z => z.ZoneNumber == zoneId);

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
