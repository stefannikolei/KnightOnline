using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser quest slice (User.cpp, "정애씨" quest procedures): WIZ_CLIENT_EVENT
/// NPC dialogue, the EVENT/EXEC/LOGIC_ELSE interpreter, WIZ_SELECT_MSG menu
/// replies and the WIZ_EDIT_BOX coupon input.
/// </summary>
public sealed partial class GameUser
{
    private const int MaxMessageEvent = 10;   // MAX_MESSAGE_EVENT
    private const int MaxCurrentEvent = 20;   // MAX_CURRENT_EVENT
    private const int MaxCouponIdLength = 20; // MAX_COUPON_ID_LENGTH

    /// <summary>m_sEventNid — the NPC the running dialogue belongs to.</summary>
    public short EventNid;

    /// <summary>m_iSelMsgEvent — the follow-up event per menu slot.</summary>
    public readonly int[] SelMsgEvent = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1];

    /// <summary>m_iEditBoxEvent.</summary>
    public int EditBoxEvent = -1;

    /// <summary>m_strCouponId.</summary>
    public string CouponId = string.Empty;

    /// <summary>m_byLastExchangeNum (item-exchange reward slot).</summary>
    public byte LastExchangeNum;

    /// <summary>m_sEvent — the per-session COM event list.</summary>
    public readonly short[] ComEvents = new short[MaxCurrentEvent];

    /// <summary>CUser::ClientEvent — WIZ_CLIENT_EVENT, keyed off the NPC type.</summary>
    public void ClientEvent(ReadOnlySpan<byte> body)
    {
        if (!world.PointCheckFlag || UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        short nid = reader.GetShort();

        GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
        if (npc is null)
            return;

        EventNid = nid;

        Dictionary<int, QuestEventData>? zoneEvents = world.QuestEvents.GetValueOrDefault(user.Zone);
        if (zoneEvents is null)
            return;

        // The NPC-type → entry-event table (User.cpp ClientEvent switch).
        int eventId = npc.NpcType switch
        {
            23 => 30001,   // NPC_SELITH
            24 => 8030,    // NPC_ANVIL
            26 => 31001,   // NPC_CLAN_MATCH_ADVISOR
            28 => world.GetEventTrigger(npc.NpcType, npc.TrapNumber), // NPC_TELEPORT_GATE
            29 => 35201,   // NPC_OPERATOR
            33 => 35001,   // NPC_ISAAC
            34 or 105 => 21001,  // NPC_KAISHAN / NPC_NPC_5
            35 => 15002,   // NPC_CAPTAIN
            36 or 71 => 1001,    // NPC_CLAN / NPC_MONK_ELMORAD → EVENT_LOGOS_ELMORAD
            37 or 134 => 1,      // NPC_CLERIC / NPC_SIEGE_2 → EVENT_POTION
            38 or 49 => 20501,   // NPC_LADY / NPC_PRIEST_IRIS
            39 or 137 => 22001,  // NPC_ATHIAN / NPC_MANAGER_BARREL
            43 => 15951,   // NPC_ARENA
            45 or 102 => 20701,  // NPC_TRAINER_KATE / NPC_NPC_2
            46 or 104 => 20901,  // NPC_GENERIC / NPC_NPC_4
            47 or 103 => 20801,  // NPC_SENTINEL_PATRICK / NPC_NPC_3
            48 or 101 => 20601,  // NPC_TRADER_KIM / NPC_NPC_1
            72 => 2001,    // NPC_MONK_KARUS → EVENT_LOGOS_KARUS
            73 => 11001,   // NPC_MASTER_WARRIOR
            74 => 12001,   // NPC_MASTER_ROGUE
            75 => 13001,   // NPC_MASTER_MAGE
            76 => 14001,   // NPC_MASTER_PRIEST
            77 => 7001,    // NPC_BLACKSMITH
            100 => 4001,   // NPC_COUPON → EVENT_COUPON
            106 or 109 => 31101, // NPC_HERO_STATUE_1 / NPC_KARUS_HERO_STATUE
            107 => 31131,  // NPC_HERO_STATUE_2
            108 => 31161,  // NPC_HERO_STATUE_3
            110 => 31171,  // NPC_ELMORAD_HERO_STATUE
            111 => 15801,  // NPC_KEY_QUEST_1
            112 => 15821,
            113 => 15841,
            114 => 15861,
            115 => 15881,
            116 => 15901,
            117 => 15921,
            118 => 35480,  // NPC_ROBOS
            123 => 35541,  // NPC_SERVER_TRANSFER
            124 => 35560,  // NPC_RANKING
            125 => 35553,  // NPC_LYONI
            126 => 35563,  // NPC_BEGINNER_HELPER_1
            127 => 35594,  // NPC_BEGINNER_HELPER_2
            128 => 35615,  // NPC_BEGINNER_HELPER_3
            129 => 20,     // NPC_FT_1 → EVENT_FT_1
            130 => 50,     // NPC_FT_2 → EVENT_FT_2
            131 => 36,     // NPC_FT_3 → EVENT_FT_3
            132 => 35550,  // NPC_PREMIUM_PC
            133 => 35624,  // NPC_KJWAR
            135 => 32000,  // NPC_CRAFTSMAN
            136 => 35640,  // NPC_COLISEUM_ARTES
            138 => 35650,  // NPC_UNK_138
            140 => 35662,  // NPC_LOVE_AGENT
            141 => 1100,   // NPC_SPY
            142 => 17000,  // NPC_ROYAL_GUARD
            143 => 17550,  // NPC_ROYAL_CHEF
            144 => 17590,  // NPC_ESLANT_WOMAN
            145 => 17600,  // NPC_FARMER
            146 => 17630,  // NPC_NAMELESS_WARRIOR
            147 => 17100,  // NPC_UNK_147
            148 => 17570,  // NPC_GATE_GUARD
            149 => 17520,  // NPC_ROYAL_ADVISOR
            150 => 17681,  // NPC_BIFROST_GATE
            151 => 15310,  // NPC_SANGDUF
            152 => 2901,   // NPC_UNK_152
            153 => 35212,  // NPC_ADELIA
            154 => 0,      // NPC_BIFROST_MONUMENT
            _ => -1,
        };

        if (eventId == -1 && npc.NpcType == 28)
            return; // teleport gates without a trigger row bail out early

        QuestEventData? eventData = zoneEvents.GetValueOrDefault(eventId);
        if (eventData is null)
            return;

        if (!CheckEventLogic(eventData))
            return;

        foreach (QuestExec exec in eventData.Execs)
        {
            if (!RunNpcEvent(npc, exec))
                return;
        }
    }

    /// <summary>CUser::CheckEventLogic — the 'A' condition chain (AND semantics).</summary>
    public bool CheckEventLogic(QuestEventData eventData)
    {
        if (UserData is not { } user)
            return false;

        bool exact = true;

        foreach (QuestLogic logic in eventData.Logics)
        {
            exact = false;

            switch (logic.LogicElse)
            {
                case QuestLogicOp.CheckUnderWeight:
                    if (logic.Ints[0] + ItemWeight >= MaxWeight)
                        exact = true;
                    break;

                case QuestLogicOp.CheckOverWeight:
                    if (logic.Ints[0] + ItemWeight < MaxWeight)
                        exact = true;
                    break;

                case QuestLogicOp.CheckSkillPoint:
                    if (CheckSkillPoint(logic.Ints[0], logic.Ints[1], logic.Ints[2]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckExistItem:
                    if (CheckExistItem(logic.Ints[0], (short)logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoExistItem:
                    if (!CheckExistItem(logic.Ints[0], (short)logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckClass:
                    if (CheckClass(logic.Ints[0], logic.Ints[1], logic.Ints[2], logic.Ints[3], logic.Ints[4], logic.Ints[5]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoClass:
                    if (!CheckClass(logic.Ints[0], logic.Ints[1], logic.Ints[2], logic.Ints[3], logic.Ints[4], logic.Ints[5]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckWeight:
                    // Inverted upstream: the 'A CHECK_WEIGHT' passes when the
                    // item would NOT fit.
                    if (!CheckWeight(logic.Ints[0], (short)logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckEditBox:
                    if (!CheckEditBox())
                        exact = true;
                    break;

                case QuestLogicOp.Rand:
                    if (CheckRandom((short)logic.Ints[0]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckLevel:
                    if (user.Level >= logic.Ints[0] && user.Level <= logic.Ints[1])
                        exact = true;
                    break;

                case QuestLogicOp.NoExistComEvent:
                    if (!ExistComEvent(logic.Ints[0]))
                        exact = true;
                    break;

                case QuestLogicOp.ExistComEvent:
                    if (ExistComEvent(logic.Ints[0]))
                        exact = true;
                    break;

                case QuestLogicOp.HowMuchItem:
                    if (CheckItemCount(logic.Ints[0], (short)logic.Ints[1], (short)logic.Ints[2]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoah:
                    if (user.Gold >= logic.Ints[0] && user.Gold <= logic.Ints[1])
                        exact = true;
                    break;

                case QuestLogicOp.CheckNation:
                    if (user.Nation == logic.Ints[0])
                        exact = true;
                    break;

                case QuestLogicOp.CheckLoyalty:
                    if (user.Loyalty >= logic.Ints[0] && user.Loyalty <= logic.Ints[1])
                        exact = true;
                    break;

                case QuestLogicOp.CheckChief:
                    if (user.Fame == FameChief)
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoChief:
                    if (user.Fame != FameChief)
                        exact = true;
                    break;

                case QuestLogicOp.CheckClanGrade:
                    if (CheckClanGrade(logic.Ints[0], logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckMiddleStatueCapture:
                    if (CheckMiddleStatueCapture())
                        exact = true;
                    break;

                case QuestLogicOp.CheckMiddleStatueNoCapture:
                    if (!CheckMiddleStatueCapture())
                        exact = true;
                    break;

                case QuestLogicOp.CheckEmptySlot:
                    if (GetNumberOfEmptySlots() >= logic.Ints[0])
                        exact = true;
                    break;

                case QuestLogicOp.CheckMonsterChallengeTime:
                    if (world.MonsterChallengeActiveType == logic.Ints[0] && world.MonsterChallengeState != 0)
                        exact = true;
                    break;

                case QuestLogicOp.CheckExistEvent:
                    if (CheckExistEvent((short)logic.Ints[0], (byte)logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoExistEvent:
                    if (!CheckExistEvent((short)logic.Ints[0], (byte)logic.Ints[1]))
                        exact = true;
                    break;

                case QuestLogicOp.CheckItemChangeNum:
                    if (LastExchangeNum == logic.Ints[0])
                        exact = true;
                    break;

                case QuestLogicOp.CheckKnight:
                    if (CheckKnight())
                        exact = true;
                    break;

                case QuestLogicOp.CheckPromotionEligible:
                    if (CheckPromotionEligible())
                        exact = true;
                    break;

                case QuestLogicOp.CheckNoCastle:
                    if (user.Knights != world.SiegeMasterKnights
                        || world.SiegeMasterKnights == 0
                        || user.Fame != FameChief)
                        exact = true;
                    break;

                case QuestLogicOp.CheckCastle:
                    if (user.Knights == world.SiegeMasterKnights
                        && world.SiegeMasterKnights > 0
                        && user.Fame == FameChief)
                        exact = true;
                    break;

                case QuestLogicOp.CheckMonsterChallengeUserCount:
                    if (world.MonsterChallengePlayerCount > logic.Ints[0])
                        exact = true;
                    break;

                case QuestLogicOp.CheckBeefRoastKarusVictory:
                    if (world.BeefRoastVictoryType == 1)
                        exact = true;
                    break;

                case QuestLogicOp.CheckBeefRoastElmoradVictory:
                    if (world.BeefRoastVictoryType == 2)
                        exact = true;
                    break;

                case QuestLogicOp.CheckBeefRoastNoVictory:
                    if (world.BeefRoastVictoryType is not 1 and not 2)
                        exact = true;
                    break;

                default:
                    return false;
            }

            if (!logic.And)
            {
                if (exact)
                    return true;
            }
            else
            {
                if (!exact)
                    return false;
            }
        }

        return exact;
    }

    /// <summary>CUser::RunNpcEvent — executes the 'E' lines of an NPC dialogue.</summary>
    public bool RunNpcEvent(GameNpc npc, QuestExec exec)
    {
        if (UserData is not { } user)
            return false;

        switch (exec.Exec)
        {
            case QuestExecOp.Say:
                SendNpcSay(exec);
                break;

            case QuestExecOp.SelectMsg:
                SelectMsg(exec);
                break;

            case QuestExecOp.RunEvent:
            {
                QuestEventData? next = world.QuestEvents.GetValueOrDefault(user.Zone)
                    ?.GetValueOrDefault(exec.Ints[0]);
                if (next is null)
                    break;

                if (!CheckEventLogic(next))
                    break;

                foreach (QuestExec nested in next.Execs)
                {
                    if (!RunNpcEvent(npc, nested))
                        return false;
                }
                break;
            }

            case QuestExecOp.GiveItem:
                if (!GiveItem(exec.Ints[0], (short)exec.Ints[1]))
                    return false;
                break;

            case QuestExecOp.RobItem:
                if (!RobItem(exec.Ints[0], (short)exec.Ints[1]))
                    return false;
                break;

            case QuestExecOp.OpenEditBox:
                OpenEditBox(exec.Ints[1], exec.Ints[2]);
                break;

            case QuestExecOp.GiveNoah:
                GoldGain(exec.Ints[0]);
                break;

            case QuestExecOp.LogCouponItem:
                LogCoupon(exec.Ints[0], exec.Ints[1]);
                break;

            case QuestExecOp.SaveComEvent:
                SaveComEvent((short)exec.Ints[0]);
                break;

            case QuestExecOp.Return:
                return false;
        }

        return true;
    }

    /// <summary>CUser::RunEvent — the menu/edit-box continuation (adds ROB_NOAH).</summary>
    public bool RunEvent(QuestEventData eventData)
    {
        if (UserData is not { } user)
            return false;

        foreach (QuestExec exec in eventData.Execs)
        {
            switch (exec.Exec)
            {
                case QuestExecOp.Say:
                    SendNpcSay(exec);
                    break;

                case QuestExecOp.SelectMsg:
                    SelectMsg(exec);
                    break;

                case QuestExecOp.RunEvent:
                {
                    QuestEventData? next = world.QuestEvents.GetValueOrDefault(user.Zone)
                        ?.GetValueOrDefault(exec.Ints[0]);
                    if (next is null)
                        break;

                    if (!CheckEventLogic(next))
                        break;

                    if (!RunEvent(next))
                        return false;
                    break;
                }

                case QuestExecOp.GiveItem:
                    if (!GiveItem(exec.Ints[0], (short)exec.Ints[1]))
                        return false;
                    break;

                case QuestExecOp.RobItem:
                    if (!RobItem(exec.Ints[0], (short)exec.Ints[1]))
                        return false;
                    break;

                case QuestExecOp.OpenEditBox:
                    OpenEditBox(exec.Ints[1], exec.Ints[2]);
                    break;

                case QuestExecOp.GiveNoah:
                    GoldGain(exec.Ints[0]);
                    break;

                case QuestExecOp.LogCouponItem:
                    LogCoupon(exec.Ints[0], exec.Ints[1]);
                    break;

                case QuestExecOp.SaveComEvent:
                    SaveComEvent((short)exec.Ints[0]);
                    break;

                case QuestExecOp.RobNoah:
                    GoldLose(exec.Ints[0]);
                    break;

                case QuestExecOp.Return:
                    return false;
            }
        }

        return true;
    }

    /// <summary>CUser::SendNpcSay — WIZ_NPC_SAY with the first ten EXEC ints.</summary>
    public void SendNpcSay(QuestExec exec)
    {
        var buffer = new byte[44];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_SAY);
        for (int i = 0; i < 10; i++)
            writer.SetDWord((uint)exec.Ints[i]);
        Send(writer.Written);
    }

    /// <summary>CUser::SendSay — a fixed WIZ_NPC_SAY (promotion dialogue).</summary>
    public void SendSay(short eventIdUp, short eventIdOk, short message1, short message2 = 0,
        short message3 = 0, short message4 = 0, short message5 = 0, short message6 = 0,
        short message7 = 0, short message8 = 0)
    {
        var buffer = new byte[44];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_SAY);
        writer.SetDWord((uint)eventIdUp);
        writer.SetDWord((uint)eventIdOk);
        writer.SetDWord((uint)message1);
        writer.SetDWord((uint)message2);
        writer.SetDWord((uint)message3);
        writer.SetDWord((uint)message4);
        writer.SetDWord((uint)message5);
        writer.SetDWord((uint)message6);
        writer.SetDWord((uint)message7);
        writer.SetDWord((uint)message8);
        Send(writer.Written);
    }

    /// <summary>CUser::SelectMsg — WIZ_SELECT_MSG menu + follow-up bookkeeping.</summary>
    public void SelectMsg(QuestExec exec)
    {
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SELECT_MSG);
        writer.SetShort(EventNid);
        writer.SetDWord((uint)exec.Ints[1]); // dialogue talk id

        int chat = 2;
        for (int i = 0; i < MaxMessageEvent; i++)
        {
            writer.SetDWord((uint)exec.Ints[chat]);
            chat += 2;
        }

        Send(writer.Written);

        for (int j = 0; j < MaxMessageEvent; j++)
            SelMsgEvent[j] = exec.Ints[2 * j + 3];
    }

    /// <summary>CUser::RecvSelectMsg — the client picked a menu entry.</summary>
    public void RecvSelectMsg(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        int selNum = reader.GetByte();

        bool ok = false;
        if (selNum is >= 0 and < MaxMessageEvent)
        {
            int selEvent = SelMsgEvent[selNum];
            QuestEventData? eventData = world.QuestEvents.GetValueOrDefault(user.Zone)
                ?.GetValueOrDefault(selEvent);

            if (eventData is not null && CheckEventLogic(eventData) && RunEvent(eventData))
                ok = true;
        }

        if (!ok)
        {
            for (int i = 0; i < MaxMessageEvent; i++)
                SelMsgEvent[i] = -1;
        }
    }

    /// <summary>CUser::RecvEditBox — the coupon input continuation.</summary>
    public void RecvEditBox(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        int couponLength = reader.GetShort();
        if (couponLength is < 0 or > MaxCouponIdLength)
            return;

        CouponId = System.Text.Encoding.Latin1.GetString(reader.GetString(couponLength));

        QuestEventData? eventData = world.QuestEvents.GetValueOrDefault(user.Zone)
            ?.GetValueOrDefault(EditBoxEvent);

        if (eventData is not null && CheckEventLogic(eventData) && RunEvent(eventData))
            return;

        EditBoxEvent = -1;
        CouponId = string.Empty;
    }

    /// <summary>
    /// CUser::OpenEditBox — the C++ queues a DB_COUPON_EVENT check whose Aujard
    /// stored procedures were never implemented upstream, so the flow ends here.
    /// </summary>
    public void OpenEditBox(int message, int eventId)
    {
        logger.LogDebug("OpenEditBox: coupon check not implemented upstream [message={Message} event={Event}]",
            message, eventId);
    }

    /// <summary>CUser::LogCoupon — same unimplemented upstream coupon path.</summary>
    public void LogCoupon(int itemId, int count)
    {
        logger.LogDebug("LogCoupon: coupon update not implemented upstream [itemId={ItemId} count={Count}]",
            itemId, count);
    }

    /// <summary>CUser::TestPacket — re-requests the NPC region list.</summary>
    public void TestPacket()
    {
        world.RegionNpcInfoForMe(this);
    }

    // ---- logic helpers ----

    /// <summary>CUser::CheckSkillPoint.</summary>
    public bool CheckSkillPoint(int skillNum, int min, int max)
    {
        if (UserData is not { } user || skillNum is < 5 or > 8)
            return false;

        return user.Skills[skillNum] >= min && user.Skills[skillNum] <= max;
    }

    /// <summary>CUser::CheckWeight — would the item fit (weight + free slot)?</summary>
    public bool CheckWeight(int itemId, short count)
    {
        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return false;

        if (table.Countable == 0)
        {
            if (ItemWeight + table.Weight <= MaxWeight && GetEmptySlot(itemId, 0) != 0xFF)
                return true;
        }
        else
        {
            if (table.Weight * count + ItemWeight <= MaxWeight
                && GetEmptySlot(itemId, table.Countable) != 0xFF)
                return true;
        }

        return false;
    }

    /// <summary>CUser::CheckExistItem — scans equip + inventory.</summary>
    public bool CheckExistItem(int itemId, short count)
    {
        if (UserData is not { } user)
            return false;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return false;

        for (int i = 0; i < GameConstants.SlotMax + GameConstants.HaveMax; i++)
        {
            if (user.Items[i].Num != itemId)
                continue;

            if (table.Countable == 0)
                return true;

            return user.Items[i].Count >= count;
        }

        return false;
    }

    /// <summary>CUser::CheckItemCount — inventory count within [min, max].</summary>
    public bool CheckItemCount(int itemId, short min, short max)
    {
        if (UserData is not { } user)
            return false;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return false;

        for (int i = GameConstants.SlotMax; i < GameConstants.SlotMax + GameConstants.HaveMax; i++)
        {
            if (user.Items[i].Num != itemId)
                continue;

            if (table.Countable == 0)
                return min != 0 || max != 0;

            if (user.Items[i].Count < min || user.Items[i].Count > max)
            {
                if (min == 0)
                    return false;

                if (max != 0)
                    return false;
            }

            return true;
        }

        return min == 0 || max == 0;
    }

    /// <summary>CUser::RobItem — remove the quest item from the inventory.</summary>
    public bool RobItem(int itemId, short count)
    {
        if (UserData is not { } user)
            return false;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return false;

        for (int i = GameConstants.SlotMax; i < GameConstants.SlotMax + GameConstants.HaveMax; i++)
        {
            if (user.Items[i].Num != itemId)
                continue;

            if (table.Countable == 0)
            {
                user.Items[i].Num = 0;
                user.Items[i].Count = 0;
                user.Items[i].Duration = 0;
            }
            else
            {
                if (user.Items[i].Count < count)
                    return false;

                user.Items[i].Count -= count;
                if (user.Items[i].Count == 0)
                {
                    user.Items[i].Num = 0;
                    user.Items[i].Count = 0;
                    user.Items[i].Duration = 0;
                }
            }

            SendItemWeight();

            var buffer = new byte[16];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_ITEM_COUNT_CHANGE);
            writer.SetShort(1);
            writer.SetByte(1);
            writer.SetByte((byte)(i - GameConstants.SlotMax));
            writer.SetDWord((uint)itemId);
            writer.SetDWord((uint)user.Items[i].Count);
            Send(writer.Written);
            return true;
        }

        return false;
    }

    /// <summary>CUser::CheckClass — up to six job groups / class codes.</summary>
    public bool CheckClass(int class1, int class2 = -1, int class3 = -1, int class4 = -1,
        int class5 = -1, int class6 = -1)
    {
        return JobGroupCheck((short)class1) || JobGroupCheck((short)class2)
            || JobGroupCheck((short)class3) || JobGroupCheck((short)class4)
            || JobGroupCheck((short)class5) || JobGroupCheck((short)class6);
    }

    /// <summary>CUser::CheckClanGrade.</summary>
    public bool CheckClanGrade(int min, int max)
    {
        if (UserData is not { } user || user.Knights == 0)
            return false;

        KnightsClan? clan = world.Knights.GetValueOrDefault(user.Knights);
        if (clan is null)
            return false;

        return clan.Grade >= min && clan.Grade <= max;
    }

    /// <summary>CUser::CheckKnight — member of a real knights order (flag 2).</summary>
    public bool CheckKnight()
    {
        if (UserData is not { } user)
            return false;

        KnightsClan? clan = world.Knights.GetValueOrDefault(user.Knights);
        return clan?.Flag == KnightsClan.KnightsType;
    }

    /// <summary>CUser::CheckRandom — percent of 1000.</summary>
    public bool CheckRandom(short percent)
    {
        if (percent is < 0 or > 1000)
            return false;

        return percent > world.Rand(0, 1000);
    }

    /// <summary>CUser::CheckEditBox — coupon id matches a SERVER_RESOURCE template.</summary>
    public bool CheckEditBox()
    {
        return CouponId == world.FormatResource(144)   // IDS_COUPON_NOTEPAD_ID
            || CouponId == world.FormatResource(145);  // IDS_COUPON_POSTIT_ID
    }

    /// <summary>CUser::CheckMiddleStatueCapture (Doda monument).</summary>
    public bool CheckMiddleStatueCapture()
    {
        if (UserData is not { } user)
            return false;

        byte lastCaptured = user.Nation switch
        {
            1 => world.ElmoradMonumentDodaNation,
            2 => world.KarusMonumentDodaNation,
            _ => 0,
        };

        return user.Nation is 1 or 2 && lastCaptured == user.Nation;
    }

    /// <summary>CUser::GetNumberOfEmptySlots.</summary>
    public int GetNumberOfEmptySlots()
    {
        if (UserData is not { } user)
            return 0;

        int count = 0;
        for (int i = GameConstants.SlotMax; i < GameConstants.SlotMax + GameConstants.HaveMax; i++)
        {
            if (user.Items[i].Num == 0)
                count++;
        }

        return count;
    }

    /// <summary>CUser::CheckExistEvent — quest state check.</summary>
    public bool CheckExistEvent(short questId, byte questState)
    {
        if (UserData is not { } user)
            return false;

        foreach (UserQuest quest in user.Quests)
        {
            if (quest.QuestId != questId)
                continue;

            return quest.QuestState == questState;
        }

        return questState == 0; // QUEST_STATE_NOT_STARTED
    }

    /// <summary>
    /// CUser::SaveComEvent — C++ quirk kept as-is: the loop writes the FIRST
    /// slot that does not already hold the id (usually slot 0).
    /// </summary>
    public void SaveComEvent(short eventId)
    {
        for (int i = 0; i < MaxCurrentEvent; i++)
        {
            if (ComEvents[i] != eventId)
            {
                ComEvents[i] = eventId;
                break;
            }
        }
    }

    /// <summary>CUser::ExistComEvent.</summary>
    public bool ExistComEvent(int eventId)
    {
        for (int i = 0; i < MaxCurrentEvent; i++)
        {
            if (ComEvents[i] == eventId)
                return true;
        }

        return false;
    }

    /// <summary>CUser::GoldLose — WIZ_GOLD_CHANGE loss (false when short).</summary>
    public bool GoldLose(int gold)
    {
        if (UserData is not { } user)
            return false;

        if (user.Gold < 0)
        {
            logger.LogError("GoldLose: user has negative gold [charId={CharId} gold={Gold}]",
                user.CharId, user.Gold);
            return false;
        }

        if (gold < 0)
            gold = 0;

        if (user.Gold < gold)
            return false;

        user.Gold -= gold;

        var buffer = new byte[12];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
        writer.SetByte(2); // GOLD_CHANGE_LOSE
        writer.SetDWord((uint)gold);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);

        return true;
    }

    /// <summary>CUser::CheckPromotionEligible — the level-60 master promotion.</summary>
    public bool CheckPromotionEligible()
    {
        if (UserData is not { } user)
            return false;

        GameNpc? npc = world.Npcs.GetValueOrDefault(EventNid);
        if (npc is null)
            return false;

        if (CheckClass(Guardian, Penetrator, Necromancer, DarkPriest)
            || CheckClass(Protector, Assassin, Enchanter, Druid))
        {
            switch (user.Class)
            {
                case Protector:
                case Guardian:
                    SendSay(-1, -1, 6006);
                    break;

                case Assassin:
                case Penetrator:
                    SendSay(-1, -1, 7006);
                    break;

                case Enchanter:
                case Necromancer:
                    SendSay(-1, -1, 8006);
                    break;

                case Druid:
                case DarkPriest:
                    SendSay(-1, -1, 9006);
                    break;
            }

            return false;
        }

        const int masterLevel = 60;

        switch (npc.NpcType)
        {
            case 73: // NPC_MASTER_WARRIOR
                if ((user.Class != Berserker && user.Class != Blade) || user.Level < masterLevel)
                {
                    SendSay(-1, -1, 6001);
                    return false;
                }
                return true;

            case 74: // NPC_MASTER_ROGUE
                if ((user.Class != Hunter && user.Class != Ranger) || user.Level < masterLevel)
                {
                    SendSay(-1, -1, 7001);
                    return false;
                }
                return true;

            case 75: // NPC_MASTER_MAGE
                if ((user.Class != Sorcerer && user.Class != Mage) || user.Level < masterLevel)
                {
                    SendSay(-1, -1, 8001);
                    return false;
                }
                return true;

            case 76: // NPC_MASTER_PRIEST
                if ((user.Class != Shaman && user.Class != Cleric) || user.Level < masterLevel)
                {
                    SendSay(-1, -1, 9001);
                    return false;
                }
                return true;
        }

        return false;
    }
}
