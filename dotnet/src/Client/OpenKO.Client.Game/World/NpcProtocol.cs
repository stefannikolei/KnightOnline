using System.Text;
using OpenKO.Network;

namespace OpenKO.Client.Game.World;

/// <summary>A WIZ_NPC_MOVE update (world coords, already de-scaled from ×10).</summary>
public readonly record struct NpcMoveUpdate(short Id, float X, float Y, float Z, short Speed);

/// <summary>
/// Parsers for the NPC stream (WIZ_NPC_INOUT / WIZ_NPC_MOVE). Field order is
/// pinned against the C# Ebenezer CNpc::GetNpcInfo send side; positions are the
/// ×10 fixed-point form converted to world floats.
/// </summary>
public static class NpcProtocol
{
    private static readonly Encoding Ascii = Encoding.Latin1;

    private const float CoordScale = 0.1f;

    /// <summary>WIZ_NPC_INOUT type byte (in vs out).</summary>
    public static byte ParseInOutType(ReadOnlySpan<byte> payload) => payload[1];

    public static short ParseInOutId(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        r.GetByte(); // type
        return r.GetShort();
    }

    /// <summary>WIZ_NPC_INOUT (in) — the CNpc::GetNpcInfo blob.</summary>
    public static NpcEntity ParseNpcIn(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        r.GetByte(); // type
        short id = r.GetShort();

        short protoId = r.GetShort();
        byte npcType = r.GetByte();
        int sellingGroup = (int)r.GetDWord();
        short size = r.GetShort();
        int weapon1 = (int)r.GetDWord();
        int weapon2 = (int)r.GetDWord();
        string name = Ascii.GetString(r.GetVarString(1));
        byte group = r.GetByte();
        byte level = r.GetByte();
        float x = (ushort)r.GetShort() * CoordScale;
        float z = (ushort)r.GetShort() * CoordScale;
        float y = r.GetShort() * CoordScale;
        uint gateOpen = r.GetDWord();
        byte objectType = r.GetByte();
        r.GetShort(); // sIDK0
        r.GetShort(); // sIDK1
        byte direction = r.GetByte();

        return new NpcEntity
        {
            Id = id, ProtoId = protoId, NpcType = npcType, SellingGroup = sellingGroup,
            Size = size, Weapon1 = weapon1, Weapon2 = weapon2, Name = name, Group = group,
            Level = level, GateOpen = gateOpen, ObjectType = objectType, Direction = direction,
            X = x, Y = y, Z = z,
        };
    }

    /// <summary>WIZ_NPC_MOVE — [opcode][short id][word x*10][word z*10][short y*10][short speed*10].</summary>
    public static NpcMoveUpdate ParseNpcMove(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short id = r.GetShort();
        float x = (ushort)r.GetShort() * CoordScale;
        float z = (ushort)r.GetShort() * CoordScale;
        float y = r.GetShort() * CoordScale;
        short speed = r.GetShort();
        return new NpcMoveUpdate(id, x, y, z, speed);
    }
}
