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

    /// <summary>
    /// Unspent skill points — <c>Skills[0]</c> (the skill tree's <c>string_skillpoint</c>,
    /// <c>m_iSkillInfo[0]</c>). Read-only view over <see cref="Skills"/>.
    /// </summary>
    public byte UnspentSkillPoints => Skills[0];

    /// <summary>
    /// The mastery pool for a specialization tab (1st/2nd/3rd/master → tab 1..4),
    /// <c>Skills[4 + tab]</c> i.e. <c>Skills[5..8]</c> (the C++ <c>m_iSkillInfo[5..8]</c>).
    /// A skill in that tab unlocks when its required level is at or below this pool.
    /// Returns 0 for a tab outside 1..4.
    /// </summary>
    public byte TabMastery(int tab) => tab is >= 1 and <= 4 ? Skills[4 + tab] : (byte)0;

    /// <summary>Set by WIZ_DEAD until the player respawns.</summary>
    public bool IsDead { get; set; }
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

    /// <summary>Set by WIZ_DEAD until the player respawns.</summary>
    public bool IsDead { get; set; }
}

/// <summary>A visible NPC/monster (from WIZ_NPC_INOUT — CNpc::GetNpcInfo).</summary>
public sealed class NpcEntity
{
    public short Id { get; set; }

    /// <summary>Proto/model id — the NPC_Looks key.</summary>
    public short ProtoId { get; set; }

    public byte NpcType { get; set; }

    public int SellingGroup { get; set; }

    public short Size { get; set; }

    public int Weapon1 { get; set; }

    public int Weapon2 { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte Group { get; set; }

    public byte Level { get; set; }

    public uint GateOpen { get; set; }

    public byte ObjectType { get; set; }

    public byte Direction { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    /// <summary>Set by WIZ_DEAD when the NPC dies.</summary>
    public bool IsDead { get; set; }
}

/// <summary>
/// The client-side world roster (the CPlayerOtherMgr analog): the local player
/// plus the region-visible remote players (by socket id) and NPCs (by Nid).
/// In-game packet handlers populate it; the renderer walks it.
/// </summary>
public sealed class WorldEntities
{
    private readonly Dictionary<short, RemotePlayer> _players = [];
    private readonly Dictionary<short, NpcEntity> _npcs = [];

    public LocalPlayer Local { get; } = new();

    public IReadOnlyDictionary<short, RemotePlayer> Players => _players;

    public IReadOnlyDictionary<short, NpcEntity> Npcs => _npcs;

    /// <summary>WIZ_USER_INOUT (in) — add or refresh a visible player.</summary>
    public void AddOrUpdate(RemotePlayer player) => _players[player.Id] = player;

    /// <summary>WIZ_USER_INOUT (out) — drop a player that left the region.</summary>
    public bool Remove(short id) => _players.Remove(id);

    public bool TryGet(short id, out RemotePlayer player) => _players.TryGetValue(id, out player!);

    /// <summary>WIZ_NPC_INOUT (in) — add or refresh a visible NPC.</summary>
    public void AddOrUpdateNpc(NpcEntity npc) => _npcs[npc.Id] = npc;

    /// <summary>WIZ_NPC_INOUT (out) — drop an NPC that left the region.</summary>
    public bool RemoveNpc(short id) => _npcs.Remove(id);

    public bool TryGetNpc(short id, out NpcEntity npc) => _npcs.TryGetValue(id, out npc!);

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

    /// <summary>WIZ_ROTATE — update a remote player's facing (local rotation is client-driven).</summary>
    public void Rotate(short id, short direction)
    {
        if (_players.TryGetValue(id, out RemotePlayer? player))
            player.Direction = direction;
    }

    /// <summary>
    /// WIZ_DEAD — flag the matching player or NPC as dead. Returns true when an
    /// entity was found (players take priority; the id spaces don't overlap in play).
    /// </summary>
    public bool MarkDead(short id)
    {
        if (_players.TryGetValue(id, out RemotePlayer? player))
        {
            player.IsDead = true;
            return true;
        }

        if (id == Local.SocketId)
        {
            Local.IsDead = true;
            return true;
        }

        if (_npcs.TryGetValue(id, out NpcEntity? npc))
        {
            npc.IsDead = true;
            return true;
        }

        return false;
    }

    /// <summary>WIZ_NPC_MOVE — apply a position update to a visible NPC.</summary>
    public void MoveNpc(short id, float x, float y, float z)
    {
        if (_npcs.TryGetValue(id, out NpcEntity? npc))
        {
            npc.X = x;
            npc.Y = y;
            npc.Z = z;
        }
    }

    public void Clear()
    {
        _players.Clear();
        _npcs.Clear();
    }
}
