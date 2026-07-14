namespace OpenKO.Client.Game.World;

/// <summary>The local player (from WIZ_MYINFO): identity, placement and the full stat block.</summary>
public sealed class LocalPlayer
{
    /// <summary>MAX_SKILLS — the nine skill-tree master levels in the MyInfo block.</summary>
    public const int SkillCount = 9;

    public short SocketId { get; set; }

    public string Name { get; set; } = string.Empty;

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public byte Nation { get; set; }

    public byte Race { get; set; }

    public short Class { get; set; }

    public byte Face { get; set; }

    public byte Hair { get; set; }

    public byte Rank { get; set; }

    public byte Title { get; set; }

    public byte Level { get; set; }

    public byte Points { get; set; }

    public uint MaxExp { get; set; }

    public uint Exp { get; set; }

    public uint Loyalty { get; set; }

    public uint LoyaltyMonthly { get; set; }

    public byte City { get; set; }

    public short Knights { get; set; }

    public byte Fame { get; set; }

    public short MaxHp { get; set; }

    public short Hp { get; set; }

    public short MaxMp { get; set; }

    public short Mp { get; set; }

    public short MaxWeight { get; set; }

    public short CurWeight { get; set; }

    public byte Str { get; set; }

    public byte ItemStr { get; set; }

    public byte Sta { get; set; }

    public byte ItemSta { get; set; }

    public byte Dex { get; set; }

    public byte ItemDex { get; set; }

    public byte Intel { get; set; }

    public byte ItemIntel { get; set; }

    public byte Cha { get; set; }

    public byte ItemCha { get; set; }

    public short TotalHit { get; set; }

    public short TotalAc { get; set; }

    public byte FireResist { get; set; }

    public byte ColdResist { get; set; }

    public byte LightningResist { get; set; }

    public byte MagicResist { get; set; }

    public byte DiseaseResist { get; set; }

    public byte PoisonResist { get; set; }

    public int Gold { get; set; }

    public byte Authority { get; set; }

    public byte PremiumType { get; set; }

    public short PremiumTime { get; set; }

    public uint MannerPoint { get; set; }

    /// <summary>The nine skill-master levels (WIZ_MYINFO skill array).</summary>
    public byte[] Skills { get; } = new byte[SkillCount];
}

/// <summary>A visible remote player (from WIZ_USER_INOUT / WIZ_REQ_USERIN).</summary>
public sealed class RemotePlayer
{
    public short Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte Nation { get; set; }

    public byte Level { get; set; }

    public byte Race { get; set; }

    public short Class { get; set; }

    public byte Face { get; set; }

    public byte Hair { get; set; }

    public short Direction { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    /// <summary>
    /// The eight visible-equipment item ids in CPlayerOther::Init slot order
    /// (upper, lower, head, hands, feet, cloak, right hand, left hand) — enough
    /// to assemble the character's appearance.
    /// </summary>
    public uint[] Items { get; } = new uint[8];
}

/// <summary>
/// The client-side world roster (the CPlayerOtherMgr analog): the local player
/// plus the region-visible remote players keyed by their server socket id.
/// In-game packet handlers populate it; the renderer walks it.
/// </summary>
public sealed class WorldEntities
{
    private readonly Dictionary<short, RemotePlayer> _players = [];

    public LocalPlayer Local { get; } = new();

    public IReadOnlyDictionary<short, RemotePlayer> Players => _players;

    /// <summary>WIZ_USER_INOUT (in) — add or refresh a visible player.</summary>
    public void AddOrUpdate(RemotePlayer player) => _players[player.Id] = player;

    /// <summary>WIZ_USER_INOUT (out) — drop a player that left the region.</summary>
    public bool Remove(short id) => _players.Remove(id);

    public bool TryGet(short id, out RemotePlayer player) => _players.TryGetValue(id, out player!);

    /// <summary>WIZ_MOVE — apply a position update to the local or a remote entity.</summary>
    public void Move(short id, float x, float y, float z)
    {
        if (id == Local.SocketId)
        {
            Local.X = x;
            Local.Y = y;
            Local.Z = z;
        }
        else if (_players.TryGetValue(id, out RemotePlayer? player))
        {
            player.X = x;
            player.Y = y;
            player.Z = z;
        }
    }

    public void Clear() => _players.Clear();
}
