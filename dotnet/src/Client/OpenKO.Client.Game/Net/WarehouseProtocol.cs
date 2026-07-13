using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>WIZ_WAREHOUSE sub-commands (CUser::WarehouseProcess) — bank storage.</summary>
public static class WarehouseProtocol
{
    public const byte Open = 0x01;
    public const byte Input = 0x02;
    public const byte Output = 0x03;
    public const byte Move = 0x04;
    public const byte InvenMove = 0x05;
    public const byte Req = 0x10;

    /// <summary>Open the warehouse dialog.</summary>
    public static byte[] BuildOpen() => [(byte)GameOpcode.WIZ_WAREHOUSE, Open];

    /// <summary>Request the warehouse contents.</summary>
    public static byte[] BuildReq() => [(byte)GameOpcode.WIZ_WAREHOUSE, Req];

    /// <summary>Deposit an item.</summary>
    public static byte[] BuildInput(int itemId, byte page, byte srcPos, byte destPos, int count)
        => BuildMove(Input, itemId, page, srcPos, destPos, count);

    /// <summary>Withdraw an item.</summary>
    public static byte[] BuildOutput(int itemId, byte page, byte srcPos, byte destPos, int count)
        => BuildMove(Output, itemId, page, srcPos, destPos, count);

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    private static byte[] BuildMove(byte cmd, int itemId, byte page, byte srcPos, byte destPos, int count)
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_WAREHOUSE);
        w.SetByte(cmd);
        w.SetDWord((uint)itemId);
        w.SetByte(page);
        w.SetByte(srcPos);
        w.SetByte(destPos);
        w.SetDWord((uint)count);
        return w.Written.ToArray();
    }
}
