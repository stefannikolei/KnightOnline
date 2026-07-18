using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_ITEM_TRADE reply (CGameProcMain::MsgRecv_ItemTradeResult, GameProcMain.cpp:4281).
/// <see cref="Result"/> is the server's leading result byte: 0x01 buy/sell success (carrying the
/// authoritative <see cref="Money"/>), 0x00 failure (carrying <see cref="FailType"/> bfType),
/// 0x03 quickslot-move success, 0x04 quickslot-move failure. Only 0x01 has trailing bytes (the
/// gold dword); 0x00 has the single bfType byte; 0x03/0x04 carry nothing.
/// </summary>
public readonly record struct TransactionResult(byte Result, uint Money, byte FailType)
{
    public bool Success => Result == TransactionProtocol.ResultSuccess;

    public bool MoveSuccess => Result == TransactionProtocol.ResultMoveSuccess;

    public bool MoveFail => Result == TransactionProtocol.ResultMoveFail;
}

/// <summary>
/// The client NPC-vendor packets (CUITransactionDlg::SendToServer* , UITransactionDlg.cpp:740-779)
/// and their WIZ_ITEM_TRADE reply, plus the WIZ_TRADE_NPC open push. All little-endian; the field
/// order is pinned against the C# Ebenezer's <c>ItemTrade</c> reader (GameUser.Items.cs:395) which
/// is the wire authority.
/// </summary>
public static class TransactionProtocol
{
    // ---- N3_SP_TRADE_* sub-ops (Client/WarFare/PacketDef.h:23) --------------

    /// <summary>N3_SP_TRADE_BUY — purchase from the vendor.</summary>
    public const byte Buy = 0x01;

    /// <summary>N3_SP_TRADE_SELL — sell an inventory item to the vendor.</summary>
    public const byte Sell = 0x02;

    /// <summary>N3_SP_TRADE_MOVE — inventory-to-inventory quickslot swap through the vendor grid.</summary>
    public const byte Move = 0x03;

    // ---- WIZ_ITEM_TRADE reply result codes ---------------------------------

    /// <summary>0x00 — buy/sell failed; a single bfType byte follows.</summary>
    public const byte ResultFail = 0x00;

    /// <summary>0x01 — buy/sell succeeded; the new gold total (u32) follows.</summary>
    public const byte ResultSuccess = 0x01;

    /// <summary>0x03 — quickslot move succeeded (no trailing bytes).</summary>
    public const byte ResultMoveSuccess = 0x03;

    /// <summary>0x04 — quickslot move failed (no trailing bytes).</summary>
    public const byte ResultMoveFail = 0x04;

    /// <summary>
    /// CUITransactionDlg::SendToServerSellMsg: <c>[0x21][0x02][u32 itemId][u8 pos][s16 count]</c>
    /// (9 bytes). <paramref name="pos"/> is the source backpack slot order.
    /// </summary>
    public static byte[] BuildSell(int itemId, byte pos, short count)
    {
        var buffer = new byte[9];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
        w.SetByte(Sell);
        w.SetDWord((uint)itemId);
        w.SetByte(pos);
        w.SetShort(count);
        return w.Written.ToArray();
    }

    /// <summary>
    /// CUITransactionDlg::SendToServerBuyMsg: <c>[0x21][0x01][u32 tradeId][s16 npcId][u32 itemId]
    /// [u8 pos][s16 count]</c> (15 bytes). <paramref name="pos"/> is the destination backpack slot
    /// order; <paramref name="npcId"/> is the runtime target NPC id.
    /// </summary>
    public static byte[] BuildBuy(int tradeId, short npcId, int itemId, byte pos, short count)
    {
        var buffer = new byte[15];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
        w.SetByte(Buy);
        w.SetDWord((uint)tradeId);
        w.SetShort(npcId);
        w.SetDWord((uint)itemId);
        w.SetByte(pos);
        w.SetShort(count);
        return w.Written.ToArray();
    }

    /// <summary>
    /// CUITransactionDlg::SendToServerMoveMsg: <c>[0x21][0x03][u32 itemId][u8 startPos][u8 destPos]</c>
    /// (8 bytes). Both positions are backpack slot orders (the server swaps user.Items[SlotMax+pos]).
    /// </summary>
    public static byte[] BuildMove(int itemId, byte startPos, byte destPos)
    {
        var buffer = new byte[8];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_TRADE);
        w.SetByte(Move);
        w.SetDWord((uint)itemId);
        w.SetByte(startPos);
        w.SetByte(destPos);
        return w.Written.ToArray();
    }

    /// <summary>
    /// Parse the WIZ_ITEM_TRADE reply. <paramref name="payload"/> is the full packet
    /// (<c>[0x21][result]…</c>). Trailing fields depend on the result byte.
    /// </summary>
    public static TransactionResult ParseResult(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();

        uint money = 0;
        byte failType = 0;

        switch (result)
        {
            case ResultSuccess:
                money = r.GetDWord();
                break;

            case ResultFail:
                failType = r.GetByte();
                break;
        }

        return new TransactionResult(result, money, failType);
    }

    /// <summary>
    /// Parse the WIZ_TRADE_NPC open push (CGameProcMain::MsgRecv_ItemTradeStart): <c>[0x25]
    /// [u32 tradeId]</c>. Returns the vendor's selling-group trade id.
    /// </summary>
    public static uint ParseTradeStart(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        return r.GetDWord();
    }
}
