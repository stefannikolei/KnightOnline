using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The central WIZ_MAGIC_PROCESS packet (CMagicProcess::MagicPacket): a command
/// flow byte, the spell id, source/target ids and six data slots. Both the
/// client's cast request and the server's broadcast share this layout.
/// </summary>
public readonly record struct MagicPacket(
    byte Command, int MagicId, short SourceId, short TargetId,
    short Data1, short Data2, short Data3, short Data4, short Data5, short Data6);

/// <summary>Builder/parser for WIZ_MAGIC_PROCESS. Field order pinned against the C# Ebenezer.</summary>
public static class MagicProtocol
{
    // e_MagicType command flow bytes (CMagicProcess): cast start → mid → end.
    public const byte Casting = 1;
    public const byte Flying = 2;
    public const byte Effecting = 3;
    public const byte Fail = 4;

    public static byte[] Build(MagicPacket packet)
    {
        var buffer = new byte[24];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        w.SetByte(packet.Command);
        w.SetDWord((uint)packet.MagicId);
        w.SetShort(packet.SourceId);
        w.SetShort(packet.TargetId);
        w.SetShort(packet.Data1);
        w.SetShort(packet.Data2);
        w.SetShort(packet.Data3);
        w.SetShort(packet.Data4);
        w.SetShort(packet.Data5);
        w.SetShort(packet.Data6);
        return w.Written.ToArray();
    }

    public static MagicPacket Parse(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte command = r.GetByte();
        var magicId = (int)r.GetDWord();
        short sid = r.GetShort();
        short tid = r.GetShort();
        return new MagicPacket(
            command, magicId, sid, tid,
            r.GetShort(), r.GetShort(), r.GetShort(), r.GetShort(), r.GetShort(), r.GetShort());
    }
}
