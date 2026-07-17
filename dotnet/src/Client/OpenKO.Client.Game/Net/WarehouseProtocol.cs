using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// One warehouse slot occupant parsed from the WIZ_WAREHOUSE open reply
/// (CGameProcMain::MsgRecv_WareHouseOpen → CUIWareHouseDlg::AddItemInWare). The
/// <see cref="Index"/> is the flat slot number (page * <see cref="WarehouseProtocol.ItemsPerPage"/>
/// + slot); empty rows (item id 0) are dropped.
/// </summary>
public readonly record struct WarehouseItem(int Index, uint ItemId, short Durability, short Count);

/// <summary>
/// The parsed WIZ_WAREHOUSE open reply — the stored gold plus every occupied slot
/// (CGameProcMain::MsgRecv_WareHouseOpen).
/// </summary>
public readonly record struct WarehouseContents(int Gold, IReadOnlyList<WarehouseItem> Items);

/// <summary>WIZ_WAREHOUSE sub-commands (CUser::WarehouseProcess, PacketDef.h e_SubPacket_WareHouse) — bank storage.</summary>
public static class WarehouseProtocol
{
    public const byte Open = 0x01;        // N3_SP_WARE_OPEN
    public const byte Input = 0x02;       // N3_SP_WARE_GET_IN
    public const byte Output = 0x03;      // N3_SP_WARE_GET_OUT
    public const byte Move = 0x04;        // N3_SP_WARE_WARE_MOVE
    public const byte InvenMove = 0x05;   // N3_SP_WARE_INV_MOVE
    public const byte Inn = 0x10;         // N3_SP_WARE_INN (the inn-keeper NPC event / warehouse-keeper reply)
    public const byte Req = Inn;          // kept as the historical alias (same 0x10 value)

    /// <summary>MAX_ITEM_TRADE — slots per warehouse page (GameDef.h).</summary>
    public const int ItemsPerPage = 24;

    /// <summary>MAX_ITEM_WARE_PAGE — warehouse pages (GameDef.h).</summary>
    public const int PageCount = 8;

    /// <summary>MAX_ITEM_WARE_PAGE * MAX_ITEM_TRADE — the full slot count.</summary>
    public const int SlotCount = PageCount * ItemsPerPage;

    /// <summary>
    /// The dwGold pseudo-item id (SubProcPerTrade.h) the warehouse gold in/out packets carry in
    /// place of a real item id, mirroring <see cref="ItemProtocol.GoldItemId"/>.
    /// </summary>
    public const uint GoldItemId = ItemProtocol.GoldItemId;

    /// <summary>Open the warehouse dialog (CUIInn::MsgSend_OpenWareHouse).</summary>
    public static byte[] BuildOpen() => [(byte)GameOpcode.WIZ_WAREHOUSE, Open];

    /// <summary>Request the warehouse contents.</summary>
    public static byte[] BuildReq() => [(byte)GameOpcode.WIZ_WAREHOUSE, Req];

    /// <summary>Deposit an item (CUIWareHouseDlg::SendToServerToWareMsg, N3_SP_WARE_GET_IN).</summary>
    public static byte[] BuildInput(int itemId, byte page, byte srcPos, byte destPos, int count)
        => BuildMove(Input, itemId, page, srcPos, destPos, count);

    /// <summary>Withdraw an item (CUIWareHouseDlg::SendToServerFromWareMsg, N3_SP_WARE_GET_OUT).</summary>
    public static byte[] BuildOutput(int itemId, byte page, byte srcPos, byte destPos, int count)
        => BuildMove(Output, itemId, page, srcPos, destPos, count);

    /// <summary>
    /// Deposit gold into the warehouse (CUIWareHouseDlg::GoldCountToWareOK →
    /// SendToServerToWareMsg(dwGold, 0xff, 0xff, 0xff, iGold)).
    /// </summary>
    public static byte[] BuildGoldInput(int gold) => BuildInput((int)GoldItemId, 0xff, 0xff, 0xff, gold);

    /// <summary>
    /// Withdraw gold from the warehouse (CUIWareHouseDlg::GoldCountFromWareOK →
    /// SendToServerFromWareMsg(dwGold, 0xff, 0xff, 0xff, iGold)).
    /// </summary>
    public static byte[] BuildGoldOutput(int gold) => BuildOutput((int)GoldItemId, 0xff, 0xff, 0xff, gold);

    /// <summary>
    /// Move an item within the warehouse (CUIWareHouseDlg::SendToServerWareToWareMsg,
    /// N3_SP_WARE_WARE_MOVE) — note the intra-warehouse/intra-inventory moves carry no count.
    /// </summary>
    public static byte[] BuildWareMove(int itemId, byte page, byte srcPos, byte destPos)
        => BuildShortMove(Move, itemId, page, srcPos, destPos);

    /// <summary>
    /// Move an item within the warehouse's inventory grid
    /// (CUIWareHouseDlg::SendToServerInvToInvMsg, N3_SP_WARE_INV_MOVE).
    /// </summary>
    public static byte[] BuildInvMove(int itemId, byte page, byte srcPos, byte destPos)
        => BuildShortMove(InvenMove, itemId, page, srcPos, destPos);

    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>
    /// Parse the WIZ_WAREHOUSE open reply (CGameProcMain::MsgRecv_WareHouseOpen): after the
    /// opcode + N3_SP_WARE_OPEN sub-command comes a spare byte, the u32 stored gold, then
    /// <see cref="SlotCount"/> rows of <c>[u32 itemId][i16 durability][i16 count]</c>. Empty
    /// rows (item id 0) are dropped, keeping the flat slot index on each surviving row.
    /// </summary>
    public static WarehouseContents ParseOpen(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_WAREHOUSE
        r.GetByte(); // sub-command N3_SP_WARE_OPEN
        r.GetByte(); // spare (idk)
        int gold = (int)r.GetDWord();

        var items = new List<WarehouseItem>();
        for (int i = 0; i < SlotCount; i++)
        {
            uint itemId = r.GetDWord();
            short durability = r.GetShort();
            short count = r.GetShort();
            if (itemId != 0)
                items.Add(new WarehouseItem(i, itemId, durability, count));
        }

        return new WarehouseContents(gold, items);
    }

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

    private static byte[] BuildShortMove(byte cmd, int itemId, byte page, byte srcPos, byte destPos)
    {
        var buffer = new byte[9];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_WAREHOUSE);
        w.SetByte(cmd);
        w.SetDWord((uint)itemId);
        w.SetByte(page);
        w.SetByte(srcPos);
        w.SetByte(destPos);
        return w.Written.ToArray();
    }
}
