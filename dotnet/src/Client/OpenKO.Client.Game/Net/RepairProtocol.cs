using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_ITEM_REPAIR reply (CGameProcMain::MsgRecv_ItemRepair):
/// <see cref="Success"/> is the 0x01 result byte and <see cref="Gold"/> is the player's
/// gold after the repair charge.
/// </summary>
public readonly record struct RepairResult(bool Success, uint Gold);

/// <summary>
/// The WIZ_ITEM_REPAIR client message (CItemRepairMgr::Tick) — the NPC blacksmith repair of
/// a worn item. <see cref="ArmEquip"/> repairs an equipped slot, <see cref="ArmInventory"/>
/// a backpack cell; the repair price shown before sending is composed by
/// <see cref="Ui.RepairTooltipControl"/>.
/// </summary>
public static class RepairProtocol
{
    /// <summary>iArm == 0x01 — the item lives in an equipment slot (m_pMySlot).</summary>
    public const byte ArmEquip = 0x01;

    /// <summary>iArm == 0x02 — the item lives in a backpack cell (m_pMyInvWnd).</summary>
    public const byte ArmInventory = 0x02;

    /// <summary>
    /// CItemRepairMgr::Tick repair send: <c>[WIZ_ITEM_REPAIR=0x3B][u8 arm][u8 order][u32 itemId]</c>
    /// (7 bytes). The item id is the full encoded id (basic + ext).
    /// </summary>
    public static byte[] BuildRepair(byte arm, byte order, uint itemId)
    {
        var buffer = new byte[7];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ITEM_REPAIR);
        w.SetByte(arm);
        w.SetByte(order);
        w.SetDWord(itemId);
        return w.Written.ToArray();
    }

    /// <summary>Parse the WIZ_ITEM_REPAIR reply: <c>[opcode][u8 result][u32 gold]</c>.</summary>
    public static RepairResult ParseResult(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();
        uint gold = r.GetDWord();
        return new RepairResult(result == 0x01, gold);
    }
}
