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
        // Remaining (chicken flag, ranks, 8 visible items) is not needed here.

        return new RemotePlayer
        {
            Id = id, Name = name, Nation = nation, Level = level, Race = race,
            Class = cls, Face = face, Hair = hair, Direction = direction, X = x, Y = y, Z = z,
        };
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
    /// The stable prefix of WIZ_MYINFO (SendMyInfo): id, name, position and
    /// identity. The full stat/skill/inventory block that follows is parsed in a
    /// later slice; the prefix is enough to place the local player.
    /// </summary>
    public static void ParseMyInfoInto(ReadOnlySpan<byte> payload, LocalPlayer local)
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
        r.GetByte(); // rank
        r.GetByte(); // title
        local.Level = r.GetByte();
    }
}
