using OpenKO.Core.Protocol;
using OpenKO.Core.Text;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// One selectable warp/zone-change destination (__WarpInfo, UIWarp.h) parsed from the
/// WIZ_WARP_LIST reply. The dialog shows <see cref="Name"/> in its list and
/// <see cref="Agreement"/> as the confirmation blurb; <see cref="Id"/> is echoed back on
/// confirm. Coordinates come off the wire in tenths (the C++ divides by 10) — kept raw here.
/// </summary>
public readonly record struct WarpInfo(
    int Id, string Name, string Agreement, int Zone, int MaxUser, uint Gold, short X, short Y, short Z);

/// <summary>
/// The parsed WIZ_WARP_LIST reply. <see cref="Kind"/> is 1 for a real list (populate the
/// dialog), 2 for an error/notification (no list rows). An empty list (zero rows) is a valid
/// "same zone" no-op the dialog leaves hidden.
/// </summary>
public readonly record struct WarpListReply(byte Kind, IReadOnlyList<WarpInfo> Warps);

/// <summary>
/// The WIZ_WARP_LIST client/server messages (CGameProcMain::MsgSend_Warp /
/// MsgRecv_WarpList) — the NPC/object teleport menu. The separate WIZ_WARP (0x1E) push
/// that actually relocates the player is handled elsewhere.
/// </summary>
public static class WarpProtocol
{
    /// <summary>Reply kind: a populated warp list.</summary>
    public const byte KindList = 1;

    /// <summary>Reply kind: an error / notification (MsgRecv_WarpList_Error).</summary>
    public const byte KindError = 2;

    /// <summary>
    /// CGameProcMain::MsgSend_Warp — confirm a chosen destination:
    /// <c>[WIZ_WARP_LIST=0x4B][i16 warpId]</c> (3 bytes).
    /// </summary>
    public static byte[] BuildWarp(int warpId)
    {
        var buffer = new byte[3];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_WARP_LIST);
        w.SetShort((short)warpId);
        return w.Written.ToArray();
    }

    /// <summary>The reply kind byte (opcode then this) without a full parse.</summary>
    public static byte Kind(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>
    /// Parse the WIZ_WARP_LIST reply (CGameProcMain::MsgRecv_WarpList). After the opcode comes
    /// the kind byte; a list (kind 1) then carries an i16 row count and, per row,
    /// <c>[i16 id][str2 name][str2 agreement][i16 zone][i16 maxUser][u32 gold][i16 x][i16 z][i16 y]</c>.
    /// A non-list kind yields an empty row set.
    /// </summary>
    public static WarpListReply ParseList(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_WARP_LIST
        byte kind = r.GetByte();
        if (kind != KindList)
            return new WarpListReply(kind, []);

        int count = r.GetShort();
        var warps = new List<WarpInfo>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            int id = r.GetShort();
            string name = KoEncoding.Cp949.GetString(r.GetVarString(2));
            string agreement = KoEncoding.Cp949.GetString(r.GetVarString(2));
            short zone = r.GetShort();
            short maxUser = r.GetShort();
            uint gold = r.GetDWord();
            short x = r.GetShort();
            short z = r.GetShort();
            short y = r.GetShort();
            warps.Add(new WarpInfo(id, name, agreement, zone, maxUser, gold, x, y, z));
        }

        return new WarpListReply(kind, warps);
    }
}
