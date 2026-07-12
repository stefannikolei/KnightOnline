using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the quest slice (stage 4.14): the .evt parser and the
/// EVENT/EXEC/LOGIC interpreter behind WIZ_CLIENT_EVENT / WIZ_SELECT_MSG.
/// </summary>
public class GameUserQuestTests
{
    private const int PotionId = 389015000;

    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f) { Type = 1 });
        world.Rand = Math.Min;
        world.PointCheckFlag = true;
        world.CoefficientTable[105] = new Coefficient
        {
            ClassId = 105,
            ShortSword = 0,
            Sword = 0.005,
            Axe = 0,
            Club = 0,
            Spear = 0,
            Pole = 0,
            Staff = 0,
            Bow = 0,
            HitPoint = 0.1,
            ManaPoint = 0.05,
            Sp = 0,
            Armor = 0.5,
            HitRate = 0,
            EvasionRate = 0,
        };
        world.LevelUpTable[30] = 1000;
        world.ItemTable[PotionId] = new Item
        {
            ID = PotionId, Name = "potion", Kind = 91, Slot = 0, Race = 0, ClassId = 0,
            Damage = 0, Delay = 0, Range = 0, Weight = 1, Durability = 1,
            BuyPrice = 0, SellPrice = 0, Armor = 0, Countable = 1, MagicEffect = 0,
            SpecialEffect = 0, MinLevel = 1, MaxLevel = 83, RequiredRank = 0,
            RequiredTitle = 0, RequiredStrength = 0, RequiredStamina = 0,
            RequiredDexterity = 0, RequiredIntelligence = 0, RequiredCharisma = 0,
            SellingGroup = 0, Type = 0, HitRate = 0, EvasionRate = 0,
            DaggerArmor = 0, SwordArmor = 0, MaceArmor = 0, AxeArmor = 0,
            SpearArmor = 0, BowArmor = 0, FireDamage = 0, IceDamage = 0,
            LightningDamage = 0, PoisonDamage = 0, HpDrain = 0, MpDamage = 0,
            MpDrain = 0, MirrorDamage = 0, DropRate = 0, StrengthBonus = 0,
            StaminaBonus = 0, DexterityBonus = 0, IntelligenceBonus = 0,
            CharismaBonus = 0, MaxHpBonus = 0, MaxMpBonus = 0, FireResist = 0,
            ColdResist = 0, LightningResist = 0, MagicResist = 0, PoisonResist = 0,
            CurseResist = 0,
        };
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeUser(EbenezerWorld world, FakeDbAgent db)
    {
        var frames = new List<byte[]>();
        short id = world.Register(i => new GameUser(i, world, db, NullLogger.Instance));
        GameUser user = world.Users[id]!;
        user.Transmit = frame =>
        {
            frames.Add(frame);
            return true;
        };

        UserData data = db.Users.Get(id)!;
        data.AccountId = "acct";
        data.CharId = "quester";
        data.Zone = 21;
        data.Nation = 1;
        data.Race = 1;
        data.Class = 105;
        data.Level = 30;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.Gold = 10_000;
        data.CurX = 100;
        data.CurZ = 100;
        user.UserData = data;
        user.SetDetailData();
        user.State = ConnectionState.GameStart;
        return (user, frames);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    private static Dictionary<int, QuestEventData> ParseEvt(string content, int zone = 21)
    {
        string path = Path.Combine(Path.GetTempPath(), $"quest-{Guid.NewGuid():N}.evt");
        File.WriteAllText(path, content);
        try
        {
            Dictionary<int, QuestEventData>? events = QuestEventFile.Load(path, zone, NullLogger.Instance);
            Assert.NotNull(events);
            return events;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parser_ReadsEventsExecsAndLogic()
    {
        Dictionary<int, QuestEventData> events = ParseEvt("""
            ; comment line
            EVENT 7001
            A CHECK_LV 10 60
            E SAY -1 -1 100 0 0 0 0 0 0 0
            E GIVE_ITEM 389015000 3
            END

            EVENT 7002
            E RETURN
            END
            """);

        Assert.Equal(2, events.Count);

        QuestEventData first = events[7001];
        Assert.Single(first.Logics);
        Assert.Equal(QuestLogicOp.CheckLevel, first.Logics[0].LogicElse);
        Assert.Equal(10, first.Logics[0].Ints[0]);
        Assert.Equal(60, first.Logics[0].Ints[1]);

        Assert.Equal(2, first.Execs.Count);
        Assert.Equal(QuestExecOp.Say, first.Execs[0].Exec);
        Assert.Equal(-1, first.Execs[0].Ints[0]);
        Assert.Equal(100, first.Execs[0].Ints[2]);
        Assert.Equal(QuestExecOp.GiveItem, first.Execs[1].Exec);
        Assert.Equal(PotionId, first.Execs[1].Ints[0]);
        Assert.Equal(3, first.Execs[1].Ints[1]);

        Assert.Equal(QuestExecOp.Return, events[7002].Execs[0].Exec);
    }

    [Fact]
    public async Task ClientEvent_BlacksmithDialogue_SendsNpcSayAndGivesItem()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        world.QuestEvents[21] = ParseEvt("""
            EVENT 7001
            A CHECK_LV 10 60
            E SAY -1 -1 100 0 0 0 0 0 0 0
            E GIVE_ITEM 389015000 3
            END
            """);

        world.Npcs[1000] = new GameNpc { Nid = 1000, NpcType = 77, CurZone = 21 }; // NPC_BLACKSMITH

        frames.Clear();
        var packet = new byte[3];
        packet[0] = (byte)GameOpcode.WIZ_CLIENT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1000);
        await user.ParsingAsync(packet);

        byte[] say = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_NPC_SAY);
        Assert.Equal(unchecked((uint)-1), BinaryPrimitives.ReadUInt32LittleEndian(say.AsSpan(1)));
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(say.AsSpan(9)));

        // The potion landed in the first inventory slot.
        Assert.Equal(PotionId, user.UserData!.Items[GameConstants.SlotMax].Num);
        Assert.Equal(3, user.UserData.Items[GameConstants.SlotMax].Count);
        Assert.Equal(1000, user.EventNid);
    }

    [Fact]
    public async Task ClientEvent_LogicFails_NoDialogue()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        world.QuestEvents[21] = ParseEvt("""
            EVENT 7001
            A CHECK_LV 50 60
            E SAY -1 -1 100 0 0 0 0 0 0 0
            END
            """);
        world.Npcs[1000] = new GameNpc { Nid = 1000, NpcType = 77, CurZone = 21 };

        frames.Clear();
        var packet = new byte[3];
        packet[0] = (byte)GameOpcode.WIZ_CLIENT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1000);
        await user.ParsingAsync(packet);

        Assert.DoesNotContain(frames.Select(Unframe), p => p[0] == (byte)GameOpcode.WIZ_NPC_SAY);
    }

    [Fact]
    public async Task SelectMsgFlow_MenuChoiceRunsFollowUpEvent()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        world.QuestEvents[21] = ParseEvt("""
            EVENT 7001
            E SELECT_MSG 0 555 10 7002 11 7003 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
            END

            EVENT 7002
            E GIVE_NOAH 500
            END

            EVENT 7003
            E RETURN
            END
            """);
        world.Npcs[1000] = new GameNpc { Nid = 1000, NpcType = 77, CurZone = 21 };

        frames.Clear();
        var open = new byte[3];
        open[0] = (byte)GameOpcode.WIZ_CLIENT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(open.AsSpan(1), 1000);
        await user.ParsingAsync(open);

        byte[] menu = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_SELECT_MSG);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16LittleEndian(menu.AsSpan(1)));
        Assert.Equal(555u, BinaryPrimitives.ReadUInt32LittleEndian(menu.AsSpan(3)));
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(menu.AsSpan(7)));   // menu id 1
        Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(menu.AsSpan(11)));  // menu id 2

        Assert.Equal(7002, user.SelMsgEvent[0]);
        Assert.Equal(7003, user.SelMsgEvent[1]);

        // Choose the first entry → GIVE_NOAH 500.
        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_SELECT_MSG, 0]);

        Assert.Equal(10_500, user.UserData!.Gold);
        byte[] gold = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_GOLD_CHANGE);
        Assert.Equal(1, gold[1]); // GOLD_CHANGE_GAIN
        Assert.Equal(500u, BinaryPrimitives.ReadUInt32LittleEndian(gold.AsSpan(2)));
    }

    [Fact]
    public async Task SelectMsg_InvalidChoice_ResetsMenu()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeUser(world, db);

        user.SelMsgEvent[0] = 7002;
        await user.ParsingAsync([(byte)GameOpcode.WIZ_SELECT_MSG, 0]); // event 7002 unknown

        Assert.All(user.SelMsgEvent, e => Assert.Equal(-1, e));
    }

    [Fact]
    public void RobItem_RemovesCountAndNotifies()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        user.UserData!.Items[GameConstants.SlotMax + 2].Num = PotionId;
        user.UserData.Items[GameConstants.SlotMax + 2].Count = 5;

        frames.Clear();
        Assert.True(user.RobItem(PotionId, 2));

        Assert.Equal(3, user.UserData.Items[GameConstants.SlotMax + 2].Count);
        byte[] change = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_ITEM_COUNT_CHANGE);
        Assert.Equal(2, change[4]); // slot
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(change.AsSpan(9))); // remaining

        Assert.False(user.RobItem(PotionId, 99)); // not enough
    }

    [Fact]
    public void ComEvents_SaveAndExist()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeUser(world, db);

        Assert.False(user.ExistComEvent(42));
        user.SaveComEvent(42);
        Assert.True(user.ExistComEvent(42));

        // C++ quirk: the save loop writes the first slot that differs, so a
        // second distinct id overwrites slot 0.
        user.SaveComEvent(43);
        Assert.Equal(43, user.ComEvents[0]);
        Assert.False(user.ExistComEvent(42));
    }

    [Fact]
    public void CheckExistEvent_QuestStates()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeUser(world, db);

        Assert.True(user.CheckExistEvent(500, 0));  // not started
        Assert.False(user.CheckExistEvent(500, 1));

        user.UserData!.Quests[0].QuestId = 500;
        user.UserData.Quests[0].QuestState = 2;
        Assert.True(user.CheckExistEvent(500, 2));
        Assert.False(user.CheckExistEvent(500, 0));
    }

    [Fact]
    public void EventTrigger_KeyLookup()
    {
        EbenezerWorld world = MakeWorld();
        world.EventTriggers[((uint)28 << 16) | 5] = 12345;

        Assert.Equal(12345, world.GetEventTrigger(28, 5));
        Assert.Equal(-1, world.GetEventTrigger(28, 6));
    }
}
