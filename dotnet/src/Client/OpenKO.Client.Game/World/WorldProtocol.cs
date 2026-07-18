using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.World;

/// <summary>A WIZ_MOVE update (world coordinates, already divided by the ×10 wire scale).</summary>
public readonly record struct MoveUpdate(short Id, float X, float Y, float Z, short Speed, byte Echo);

/// <summary>A WIZ_CHAT broadcast.</summary>
public readonly record struct ChatMessage(byte Type, byte Nation, short Id, string Name, string Text);

/// <summary>
/// Parsers/builders for the core in-world packets (CUser::MoveProcess, UserInOut,
/// Chat, SendMyInfo). Field order is pinned against the C# Ebenezer send side;
/// wire positions are ×10 fixed-point, converted to world floats here.
/// </summary>
public static class WorldProtocol
{
    private static readonly Encoding Ascii = Encoding.Latin1;

    private const float CoordScale = 0.1f; // wire is position * 10

    public static MoveUpdate ParseMove(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short id = r.GetShort();
        float x = (ushort)r.GetShort() * CoordScale;
        float z = (ushort)r.GetShort() * CoordScale;
        float y = r.GetShort() * CoordScale;
        short speed = r.GetShort();
        byte echo = r.GetByte();
        return new MoveUpdate(id, x, y, z, speed, echo);
    }

    /// <summary>Move flags (CGameProcMain::MsgSend_Move byMoveFlag).</summary>
    public const byte MoveFlagMoving = 0x01;

    public const byte MoveFlagContinuous = 0x02;

    /// <summary>
    /// CGameProcMain::MsgSend_Move request (client→server, verbatim):
    /// [WIZ_MOVE][word x*10][word z*10][short y*10][word speed*10][byte moveFlag]
    /// — no id (the server uses the session). x/z/speed are unsigned words, y is
    /// a signed short; the move flag is 0x01 (moving) | 0x02 (continuous).
    /// </summary>
    public static byte[] BuildMove(float x, float y, float z, float speed, byte moveFlag)
    {
        var buffer = new byte[12];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MOVE);
        w.SetShort((short)(ushort)(x * 10f));     // MP_AddWord
        w.SetShort((short)(ushort)(z * 10f));     // MP_AddWord
        w.SetShort((short)(y * 10f));             // MP_AddShort (signed)
        w.SetShort((short)(ushort)(speed * 10f)); // MP_AddWord speed*10
        w.SetByte(moveFlag);
        return w.Written.ToArray();
    }

    /// <summary>
    /// CGameProcMain::MsgSend_Rotation request: [WIZ_ROTATE][short yaw*100].
    /// </summary>
    public static byte[] BuildRotate(float yaw)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ROTATE);
        w.SetShort((short)(yaw * 100f));
        return w.Written.ToArray();
    }

    /// <summary>WIZ_ROTATE broadcast — [opcode][short id][short direction].</summary>
    public static (short Id, short Direction) ParseRotate(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short id = r.GetShort();
        short dir = r.GetShort();
        return (id, dir);
    }

    /// <summary>WIZ_HP_CHANGE (self) — [opcode][short maxHp][short hp].</summary>
    public static (short MaxHp, short Hp) ParseHpChange(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short maxHp = r.GetShort();
        short hp = r.GetShort();
        return (maxHp, hp);
    }

    /// <summary>WIZ_DEAD — [opcode][short id] (user or NPC).</summary>
    public static short ParseDeadId(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        return r.GetShort();
    }

    /// <summary>WIZ_USER_INOUT type byte (in vs out).</summary>
    public static byte ParseInOutType(ReadOnlySpan<byte> payload) => payload[1];

    public static short ParseInOutId(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        r.GetByte(); // type
        return r.GetShort();
    }

    /// <summary>WIZ_USER_INOUT (in) — the CUser::GetUserInfo blob.</summary>
    public static RemotePlayer ParseUserIn(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        r.GetByte(); // type
        short id = r.GetShort();

        string name = Ascii.GetString(r.GetVarString(1));
        byte nation = r.GetByte();
        r.GetShort();          // knights (clan id)
        r.GetByte();           // fame
        r.GetShort();          // alliance knights
        r.GetVarString(1);     // clan name (empty when clan-less)
        r.GetByte();           // clan grade
        r.GetByte();           // clan ranking
        r.GetShort();          // mark version
        r.GetShort();          // cape
        byte level = r.GetByte();
        byte race = r.GetByte();
        short cls = r.GetShort();
        float x = (ushort)r.GetShort() * CoordScale;
        float z = (ushort)r.GetShort() * CoordScale;
        float y = r.GetShort() * CoordScale;
        byte face = r.GetByte();
        byte hair = r.GetByte();
        r.GetByte();           // res hp type
        r.GetDWord();          // abnormal type
        r.GetByte();           // need party
        r.GetByte();           // authority
        r.GetByte();           // party leader
        r.GetByte();           // invisibility
        short direction = r.GetShort();
        r.GetByte();           // chicken flag
        r.GetByte();           // rank
        r.GetByte();           // knights rank
        r.GetByte();           // personal rank

        var player = new RemotePlayer
        {
            Id = id, Name = name, Nation = nation, Level = level, Race = race,
            Class = cls, Face = face, Hair = hair, Direction = direction, X = x, Y = y, Z = z,
        };

        // Eight visible-equipment slots (CUser::GetUserInfo): [dword id][short
        // duration][byte flag] each, in CPlayerOther::Init slot order.
        for (int i = 0; i < player.Items.Length; i++)
        {
            player.Items[i] = r.GetDWord();
            r.GetShort(); // durability
            r.GetByte();  // flag
        }

        return player;
    }

    public static ChatMessage ParseChat(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte type = r.GetByte();
        byte nation = r.GetByte();
        short id = r.GetShort();
        string name = Ascii.GetString(r.GetVarString(1));
        string text = Ascii.GetString(r.GetVarString(2));
        return new ChatMessage(type, nation, id, name, text);
    }

    /// <summary>CUser::Chat request: [WIZ_CHAT][type][text string2].</summary>
    public static byte[] BuildChat(byte type, string text)
    {
        var buffer = new byte[4 + text.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_CHAT);
        w.SetByte(type);
        w.SetString2(Ascii.GetBytes(text));
        return w.Written.ToArray();
    }

    /// <summary>
    /// CGameProcMain::MsgSend_ChatSelectTarget — pick a 1:1 whisper target:
    /// <c>[WIZ_CHAT_TARGET=0x35][0x01][s16 len][name]</c>. Names longer than 20 chars are ignored.
    /// </summary>
    public static byte[]? BuildChatTarget(string name)
    {
        if (name.Length == 0 || name.Length > 20)
            return null;

        var buffer = new byte[4 + name.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_CHAT_TARGET);
        w.SetByte(0x01);
        w.SetString2(Ascii.GetBytes(name));
        return w.Written.ToArray();
    }

    /// <summary>Equip + backpack slots in the WIZ_MYINFO item array (SLOT_MAX + HAVE_MAX).</summary>
    public const int InventorySlotCount = 42;

    /// <summary>
    /// The full WIZ_MYINFO detail blob (CUser::SendMyInfo): identity + position,
    /// the complete stat block (level/exp/HP-MP/weight/stats+item bonuses/
    /// resistances/gold), the nine skill-master levels and the 42-slot item
    /// array. Field order pinned against the C# Ebenezer send side.
    /// </summary>
    public static void ParseMyInfoInto(ReadOnlySpan<byte> payload, LocalPlayer local, Inventory? inventory = null)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        local.SocketId = r.GetShort();
        local.Name = Ascii.GetString(r.GetVarString(1));
        local.X = (ushort)r.GetShort() * CoordScale;
        local.Z = (ushort)r.GetShort() * CoordScale;
        local.Y = r.GetShort() * CoordScale;
        local.Nation = r.GetByte();
        local.Race = r.GetByte();
        local.Class = r.GetShort();
        local.Face = r.GetByte();
        local.Hair = r.GetByte();
        local.Rank = r.GetByte();
        local.Title = r.GetByte();
        local.Level = r.GetByte();
        local.Points = r.GetByte();
        local.MaxExp = r.GetDWord();
        local.Exp = r.GetDWord();
        local.Loyalty = r.GetDWord();
        local.LoyaltyMonthly = r.GetDWord();
        local.City = r.GetByte();
        local.Knights = r.GetShort();
        local.Fame = r.GetByte();

        // Clan block (always present; zeros/empty when clan-less).
        r.GetShort();          // alliance knights
        r.GetByte();           // flag
        r.GetVarString(1);     // clan name
        r.GetByte();           // grade
        r.GetByte();           // ranking
        r.GetShort();          // mark version
        r.GetShort();          // cape

        local.MaxHp = r.GetShort();
        local.Hp = r.GetShort();
        local.MaxMp = r.GetShort();
        local.Mp = r.GetShort();
        local.MaxWeight = r.GetShort();
        local.CurWeight = r.GetShort();
        local.Str = r.GetByte();
        local.ItemStr = r.GetByte();
        local.Sta = r.GetByte();
        local.ItemSta = r.GetByte();
        local.Dex = r.GetByte();
        local.ItemDex = r.GetByte();
        local.Intel = r.GetByte();
        local.ItemIntel = r.GetByte();
        local.Cha = r.GetByte();
        local.ItemCha = r.GetByte();
        local.TotalHit = r.GetShort();
        local.TotalAc = r.GetShort();
        local.FireResist = r.GetByte();
        local.ColdResist = r.GetByte();
        local.LightningResist = r.GetByte();
        local.MagicResist = r.GetByte();
        local.DiseaseResist = r.GetByte();
        local.PoisonResist = r.GetByte();
        local.Gold = (int)r.GetDWord();
        local.Authority = r.GetByte();
        r.GetByte(); // knights rank
        r.GetByte(); // personal rank

        for (int i = 0; i < local.Skills.Length; i++)
            local.Skills[i] = r.GetByte();

        for (int i = 0; i < InventorySlotCount; i++)
        {
            int num = (int)r.GetDWord();
            short duration = r.GetShort();
            short count = r.GetShort();
            byte flag = r.GetByte();               // rental/bound flag
            short timeRemaining = r.GetShort();    // rental seconds left
            if (num != 0)
                inventory?.Set(i, new InventoryItem(num, count, duration, flag, timeRemaining));
        }

        r.GetByte();                   // account status
        local.PremiumType = r.GetByte();
        local.PremiumTime = r.GetShort();
        r.GetByte();                   // is chicken
        local.MannerPoint = r.GetDWord();
    }
}
