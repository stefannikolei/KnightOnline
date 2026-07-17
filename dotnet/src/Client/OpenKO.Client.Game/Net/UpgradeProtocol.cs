using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_ITEM_UPGRADE / ITEM_UPGRADE_REQ reply
/// (CGameProcMain::MsgRecv_ItemUpgrade): the anvil NPC that triggered the upgrade-select
/// window, so the follow-up upgrade packet can be tagged with its id.
/// </summary>
public readonly record struct UpgradeRequest(short NpcId);

/// <summary>
/// WIZ_ITEM_UPGRADE (0x5B) sub-opcodes (shared/packets.h e_ItemUpgradeOpcode) and the parse
/// for the ITEM_UPGRADE_REQ reply that opens the upgrade-select window
/// (CUIUpgradeSelect).
///
/// <para><b>Deferred:</b> the actual item-upgrade / accessory-upgrade SEND
/// (ITEM_UPGRADE_PROCESS / ITEM_UPGRADE_ACCESSORIES — placing an item + scroll on the anvil)
/// is <em>not</em> ported here because the upstream C++ dialogs it originates from
/// (CUIItemUpgrade / CUIRingUpgrade) are unimplemented stubs in this tree —
/// CUIUpgradeSelect::ReceiveMessage only pops a "needs to be implemented" message box, and
/// CGameProcMain::MsgRecv_ItemUpgrade guards the PROCESS/ACCESSORIES branches behind
/// <c>#if 0</c>. There is no MsgSend_* to port byte-exact, so no BuildUpgrade is invented.
/// The repair half of the anvil flow is fully ported in <see cref="RepairProtocol"/>.</para>
/// </summary>
public static class UpgradeProtocol
{
    /// <summary>e_ItemUpgradeOpcode values (shared/packets.h).</summary>
    public enum Opcode : byte
    {
        Req = 1,             // ITEM_UPGRADE_REQ — open the upgrade-select window
        Process = 2,         // ITEM_UPGRADE_PROCESS — item upgrade (send deferred, see class remarks)
        Accessories = 3,     // ITEM_UPGRADE_ACCESSORIES — ring upgrade (send deferred)
        BifrostReq = 4,
        BifrostExchange = 5,
    }

    /// <summary>The sub-opcode byte (opcode then this).</summary>
    public static byte Subcommand(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>
    /// Parse the ITEM_UPGRADE_REQ reply: <c>[opcode][u8 sub=1][i16 npcId]</c>
    /// (CGameProcMain::MsgRecv_ItemUpgrade).
    /// </summary>
    public static UpgradeRequest ParseRequest(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_ITEM_UPGRADE
        r.GetByte(); // sub-opcode ITEM_UPGRADE_REQ
        short npcId = r.GetShort();
        return new UpgradeRequest(npcId);
    }
}
