using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_SELECT_MSG (0x55) payload — the NPC quest MENU. <see cref="NpcId"/> names the
/// speaking NPC, <see cref="MainTalkId"/> indexes <c>__TABLE_QUEST_TALK</c> for the window's main
/// blurb, and each entry of <see cref="MenuIds"/> indexes <c>__TABLE_QUEST_MENU</c> for a clickable
/// menu row. Empty slots (menu id -1 / 0xFFFFFFFF) are filtered out, mirroring
/// <c>CUIQuestMenu::Open</c> (Client/WarFare/UIQuestMenu.cpp).
/// </summary>
public readonly record struct QuestMenuData(short NpcId, uint MainTalkId, IReadOnlyList<uint> MenuIds);

/// <summary>
/// The parsed WIZ_NPC_SAY (0x56) payload — an NPC TALK sequence. Each id in <see cref="TalkIds"/>
/// indexes <c>__TABLE_QUEST_TALK</c> and becomes one page the dialog advances through, mirroring
/// <c>CUIQuestTalk::Open</c> (Client/WarFare/UIQuestTalk.cpp).
/// </summary>
public readonly record struct QuestTalkData(IReadOnlyList<uint> TalkIds);

/// <summary>
/// The quest/NPC-dialogue protocol: the WIZ_SELECT_MSG (0x55) quest menu, WIZ_NPC_SAY (0x56) talk
/// sequence and the WIZ_NPC_EVENT (0x20) NPC-click request. Ported from
/// <c>CUIQuestMenu</c>/<c>CUIQuestTalk</c> and <c>CGameProcMain::MsgSend_NPCEvent</c>.
/// </summary>
public static class QuestProtocol
{
    /// <summary>Menu/talk slot counts read by the C++ loops (MAX_STRING_MENU / MAX_STRING_TALK).</summary>
    public const int MaxStringMenu = 10;

    /// <summary>Number of talk-id slots the C++ loop reads (MAX_STRING_TALK).</summary>
    public const int MaxStringTalk = 10;

    /// <summary>
    /// Parse the WIZ_SELECT_MSG (0x55) quest-menu push (<c>CUIQuestMenu::Open</c>). Layout:
    /// <c>[0x55][i16 npcId][u32 mainTalkId][10 × u32 menuId]</c>. A menu id of -1 / 0xFFFFFFFF is an
    /// empty slot (the C++ keeps only <c>iMenu &gt;= 0</c> after widening the u32 to int), so those are
    /// dropped and the survivors kept in order.
    /// </summary>
    public static QuestMenuData ParseSelectMsg(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_SELECT_MSG
        short npcId = r.GetShort();
        uint mainTalkId = r.GetDWord();

        var menuIds = new List<uint>(MaxStringMenu);
        for (int i = 0; i < MaxStringMenu; i++)
        {
            uint v = r.Remaining >= 4 ? r.GetDWord() : 0xFFFFFFFF;
            if ((int)v >= 0) // -1 / high-bit ids are empty slots
                menuIds.Add(v);
        }

        return new QuestMenuData(npcId, mainTalkId, menuIds);
    }

    /// <summary>
    /// Parse the WIZ_NPC_SAY (0x56) talk push (<c>CUIQuestTalk::Open</c>). Layout:
    /// <c>[0x56][u32][u32][ up to 8 × u32 talkId ]</c> — the first two u32s are discarded ("two -1s
    /// before text ids"), then up to <see cref="MaxStringTalk"/> talk ids are read (reads past the end
    /// yield 0 in the C++). Non-zero talk ids are kept in order as pages.
    /// </summary>
    public static QuestTalkData ParseNpcSay(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_NPC_SAY
        _ = r.Remaining >= 4 ? r.GetDWord() : 0; // discarded (-1)
        _ = r.Remaining >= 4 ? r.GetDWord() : 0; // discarded (-1)

        var talkIds = new List<uint>(MaxStringTalk);
        for (int i = 0; i < MaxStringTalk; i++)
        {
            uint v = r.Remaining >= 4 ? r.GetDWord() : 0; // absent → 0 (no talk)
            if (v != 0)
                talkIds.Add(v);
        }

        return new QuestTalkData(talkIds);
    }

    /// <summary>
    /// <c>CUIQuestMenu::MsgSend_SelectMenu</c> — reply with the picked menu index:
    /// <c>[WIZ_SELECT_MSG=0x55][u8 index]</c> (2 bytes).
    /// </summary>
    public static byte[] BuildSelectMenu(byte index)
    {
        var buffer = new byte[2];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_SELECT_MSG);
        w.SetByte(index);
        return w.Written.ToArray();
    }

    /// <summary>
    /// <c>CGameProcMain::MsgSend_NPCEvent</c> — tell the server the player clicked an NPC:
    /// <c>[WIZ_NPC_EVENT=0x20][i16 targetId]</c> (3 bytes).
    /// </summary>
    public static byte[] BuildNpcEvent(short targetId)
    {
        var buffer = new byte[3];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_NPC_EVENT);
        w.SetShort(targetId);
        return w.Written.ToArray();
    }
}
