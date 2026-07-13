using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>WIZ_EXCHANGE sub-commands (CUser::ExchangeProcess) — player trade.</summary>
public static class ExchangeProtocol
{
    public const byte Request = 1;
    public const byte Agree = 2;
    public const byte Add = 3;
    public const byte OtherAdd = 4;
    public const byte Decide = 5;
    public const byte OtherDecide = 6;
    public const byte Done = 7;
    public const byte Cancel = 8;

    /// <summary>Ask the target player (by socket id) to trade.</summary>
    public static byte[] BuildRequest(short targetId)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        w.SetByte(Request);
        w.SetShort(targetId);
        return w.Written.ToArray();
    }

    /// <summary>Answer a trade request.</summary>
    public static byte[] BuildAgree(bool accept)
        => [(byte)GameOpcode.WIZ_EXCHANGE, Agree, (byte)(accept ? 1 : 0)];

    /// <summary>Put an item on the trade window.</summary>
    public static byte[] BuildAdd(byte position, int itemId, int count)
    {
        var buffer = new byte[12];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_EXCHANGE);
        w.SetByte(Add);
        w.SetByte(position);
        w.SetDWord((uint)itemId);
        w.SetDWord((uint)count);
        return w.Written.ToArray();
    }

    /// <summary>Confirm the trade.</summary>
    public static byte[] BuildDecide() => [(byte)GameOpcode.WIZ_EXCHANGE, Decide];

    /// <summary>Cancel the trade.</summary>
    public static byte[] BuildCancel() => [(byte)GameOpcode.WIZ_EXCHANGE, Cancel];

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];
}
