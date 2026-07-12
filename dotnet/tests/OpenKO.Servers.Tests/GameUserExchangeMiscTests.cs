using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the stage-4.12 slice: WIZ_EXCHANGE, WIZ_POINT_CHANGE /
/// WIZ_SKILLPT_CHANGE / WIZ_CLASS_CHANGE, ZoneChange + WIZ_WARP_LIST and the
/// object events.
/// </summary>
public class GameUserExchangeMiscTests
{
    private const int SwordId = 810210000;
    private const int ArrowId = 391010000;
    private const int GoldId = 900000000;

    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f) { Type = 1 });
        world.ServerInfos[1] = new ZoneServerInfo(1, "127.0.0.1", 15001);
        world.Rand = Math.Min;
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
        world.CoefficientTable[101] = world.CoefficientTable[105] with { ClassId = 101 };
        world.LevelUpTable[10] = 1000;
        world.LevelUpTable[60] = 100000;
        world.ItemTable[SwordId] = new Item
        {
            ID = SwordId, Name = "sword", Kind = 21, Slot = 1, Race = 0, ClassId = 0,
            Damage = 100, Delay = 10, Range = 0, Weight = 10, Durability = 5000,
            BuyPrice = 0, SellPrice = 0, Armor = 0, Countable = 0, MagicEffect = 0,
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
        world.ItemTable[ArrowId] = world.ItemTable[SwordId] with { ID = ArrowId, Weight = 1, Countable = 1 };
        world.ItemTable[GoldId] = world.ItemTable[SwordId] with { ID = GoldId, Weight = 0, Countable = 1 };
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeUser(
        EbenezerWorld world, FakeDbAgent db, string charId, byte level = 10)
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
        data.AccountId = $"acct{id}";
        data.CharId = charId;
        data.Zone = 21;
        data.Nation = 1;
        data.Race = 1; // KARUS_BIG
        data.Class = 105;
        data.Level = level;
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
        world.Zones[0].RegionUserAdd(user.RegionX, user.RegionZ, user.SocketId);
        return (user, frames);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    private static async Task OpenExchange(GameUser a, GameUser b)
    {
        var req = new byte[4];
        req[0] = (byte)GameOpcode.WIZ_EXCHANGE;
        req[1] = GameUser.ExchangeReqCmd;
        BinaryPrimitives.WriteInt16LittleEndian(req.AsSpan(2), b.SocketId);
        await a.ParsingAsync(req);

        await b.ParsingAsync([(byte)GameOpcode.WIZ_EXCHANGE, GameUser.ExchangeAgreeCmd, 0x01]);
    }

    private static byte[] AddItemPacket(byte pos, int itemId, int count)
    {
        var packet = new byte[11];
        packet[0] = (byte)GameOpcode.WIZ_EXCHANGE;
        packet[1] = GameUser.ExchangeAddCmd;
        packet[2] = pos;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(3), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7), count);
        return packet;
    }

    // ---- exchange ----

    [Fact]
    public async Task Exchange_FullFlow_SwapsItemAndGold()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser alice, List<byte[]> aliceFrames) = MakeUser(world, db, "alice");
        (GameUser bob, List<byte[]> bobFrames) = MakeUser(world, db, "bob");

        alice.UserData!.Items[GameConstants.SlotMax].Num = SwordId;
        alice.UserData.Items[GameConstants.SlotMax].Duration = 5000;
        alice.UserData.Items[GameConstants.SlotMax].Count = 1;
        alice.UserData.Items[GameConstants.SlotMax].SerialNum = 777;
        alice.SetSlotItemValue();

        await OpenExchange(alice, bob);
        Assert.Equal(bob.SocketId, alice.ExchangeUser);
        Assert.Equal(alice.SocketId, bob.ExchangeUser);

        // Alice offers the sword; Bob offers 500 gold.
        await alice.ParsingAsync(AddItemPacket(0, SwordId, 1));
        await bob.ParsingAsync(AddItemPacket(0, GoldId, 500));
        Assert.Equal(9_500, bob.UserData!.Gold); // gold is deducted immediately

        aliceFrames.Clear();
        bobFrames.Clear();

        await alice.ParsingAsync([(byte)GameOpcode.WIZ_EXCHANGE, GameUser.ExchangeDecideCmd]);
        await bob.ParsingAsync([(byte)GameOpcode.WIZ_EXCHANGE, GameUser.ExchangeDecideCmd]);

        // Alice got the gold, Bob got the sword (serial preserved).
        Assert.Equal(10_500, alice.UserData!.Gold);
        Assert.Equal(0, alice.UserData.Items[GameConstants.SlotMax].Num);
        Assert.Equal(SwordId, bob.UserData.Items[GameConstants.SlotMax].Num);
        // C++ quirk: ExchangeAdd reads the serial AFTER clearing the mirror
        // slot, so serials never travel through a trade — the receiver's
        // ExchangeDone mints a fresh one.
        Assert.NotEqual(0, bob.UserData.Items[GameConstants.SlotMax].SerialNum);
        Assert.NotEqual(777, bob.UserData.Items[GameConstants.SlotMax].SerialNum);
        Assert.Equal(-1, alice.ExchangeUser);
        Assert.Equal(-1, bob.ExchangeUser);

        // Both got an EXCHANGE_DONE success packet.
        byte[] aliceDone = Unframe(aliceFrames.First(f => Unframe(f)[1] == GameUser.ExchangeDoneCmd));
        Assert.Equal(1, aliceDone[2]);
        Assert.Equal(10_500u, BinaryPrimitives.ReadUInt32LittleEndian(aliceDone.AsSpan(3)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(aliceDone.AsSpan(7))); // gold entry removed from the list

        byte[] bobDone = Unframe(bobFrames.First(f => Unframe(f)[1] == GameUser.ExchangeDoneCmd));
        Assert.Equal(1, bobDone[2]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bobDone.AsSpan(7)));
        Assert.Equal(SwordId, BinaryPrimitives.ReadInt32LittleEndian(bobDone.AsSpan(10)));
    }

    [Fact]
    public async Task Exchange_Cancel_RestoresOnlyGold()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser alice, _) = MakeUser(world, db, "alice");
        (GameUser bob, List<byte[]> bobFrames) = MakeUser(world, db, "bob");

        alice.UserData!.Items[GameConstants.SlotMax].Num = SwordId;
        alice.UserData.Items[GameConstants.SlotMax].Count = 1;
        alice.SetSlotItemValue();

        await OpenExchange(alice, bob);
        await alice.ParsingAsync(AddItemPacket(0, SwordId, 1));
        await alice.ParsingAsync(AddItemPacket(0, GoldId, 1000));
        Assert.Equal(9_000, alice.UserData.Gold);

        bobFrames.Clear();
        await alice.ParsingAsync([(byte)GameOpcode.WIZ_EXCHANGE, GameUser.ExchangeCancelCmd]);

        // The gold comes back; the sword does NOT (C++ quirk — the mirror is
        // dropped, the item was only removed from the mirror, so the live
        // inventory still has it).
        Assert.Equal(10_000, alice.UserData.Gold);
        Assert.Equal(SwordId, alice.UserData.Items[GameConstants.SlotMax].Num);
        Assert.Equal(-1, alice.ExchangeUser);
        Assert.Equal(-1, bob.ExchangeUser);

        byte[] cancel = Unframe(bobFrames.Last());
        Assert.Equal((byte)GameOpcode.WIZ_EXCHANGE, cancel[0]);
        Assert.Equal(GameUser.ExchangeCancelCmd, cancel[1]);
    }

    [Fact]
    public async Task Exchange_ReqToOtherNation_Fails()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser alice, List<byte[]> aliceFrames) = MakeUser(world, db, "alice");
        (GameUser bob, _) = MakeUser(world, db, "bob");
        bob.UserData!.Nation = 2;

        var req = new byte[4];
        req[0] = (byte)GameOpcode.WIZ_EXCHANGE;
        req[1] = GameUser.ExchangeReqCmd;
        BinaryPrimitives.WriteInt16LittleEndian(req.AsSpan(2), bob.SocketId);
        await alice.ParsingAsync(req);

        Assert.Equal(-1, alice.ExchangeUser);
        byte[] reply = Unframe(aliceFrames.Last());
        Assert.Equal(GameUser.ExchangeCancelCmd, reply[1]);
    }

    // ---- points / skills / class ----

    [Fact]
    public async Task PointChange_SpendsPointAndRaisesStat()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        user.UserData!.Points = 3;

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_POINT_CHANGE, 0x01, 0x01, 0x00]); // STR +1

        Assert.Equal(2, user.UserData.Points);
        Assert.Equal(71, user.UserData.Str);

        byte[] reply = Unframe(frames[0]);
        Assert.Equal((byte)GameOpcode.WIZ_POINT_CHANGE, reply[0]);
        Assert.Equal(0x01, reply[1]);
        Assert.Equal(71, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(2)));
    }

    [Fact]
    public async Task SkillPointChange_SuccessSendsNoPacket()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        user.UserData!.Skills[0] = 2;

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_SKILLPT_CHANGE, 0x05]);

        // Success is silent (the C++ returns without a reply).
        Assert.Equal(1, user.UserData.Skills[0]);
        Assert.Equal(1, user.UserData.Skills[5]);
        Assert.Empty(frames);

        // Draining the pool makes the next request fail loudly.
        user.UserData.Skills[0] = 0;
        await user.ParsingAsync([(byte)GameOpcode.WIZ_SKILLPT_CHANGE, 0x05]);
        byte[] reply = Unframe(frames.Single());
        Assert.Equal((byte)GameOpcode.WIZ_SKILLPT_CHANGE, reply[0]);
        Assert.Equal(0x05, reply[1]);
        Assert.Equal(1, reply[2]); // current skill value echoed back
    }

    [Fact]
    public async Task ClassChange_ValidPromotion_UpdatesClass()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        user.UserData!.Class = 101; // KARUWARRRIOR

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_CLASS_CHANGE, 0x02, 105]); // → BERSERKER

        Assert.Equal(105, user.UserData.Class);
        Assert.Empty(frames); // no party → no broadcast, success is silent

        await user.ParsingAsync([(byte)GameOpcode.WIZ_CLASS_CHANGE, 0x02, 205]); // BLADE is Elmorad
        byte[] reply = Unframe(frames.Single());
        Assert.Equal(0x02, reply[1]);
        Assert.Equal(0, reply[2]); // rejected
    }

    [Fact]
    public async Task AllPointChange_SuccessAlsoSendsFailTail()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice", level: 60);
        user.UserData!.Gold = 100_000_000;

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_CLASS_CHANGE, 0x03]);

        // Stats reset to the KARUS_BIG base line.
        Assert.Equal(65, user.UserData.Str);
        Assert.Equal(65, user.UserData.Sta);
        Assert.Equal(60, user.UserData.Dex);
        Assert.Equal((60 - 1) * 3 + 10, user.UserData.Points);

        // (level*2)^3.4 → 120^3.4 = 11_617_649.xx → /100*100 → 11_617_600 → *1.5 (60..90)
        // = 17_426_400... too expensive; recompute for level 60: (int)pow(120,3.4)=11_617_664?
        // The exact cost is asserted via the fail-tail packet instead of hand math:
        // the C++ success path falls through into fail_return, so we get BOTH packets.
        Assert.Equal(2, frames.Count);

        byte[] success = Unframe(frames[0]);
        Assert.Equal((byte)GameOpcode.WIZ_CLASS_CHANGE, success[0]);
        Assert.Equal(0x03, success[1]);
        Assert.Equal(1, success[2]);

        byte[] tail = Unframe(frames[1]);
        Assert.Equal((byte)GameOpcode.WIZ_CLASS_CHANGE, tail[0]);
        Assert.Equal(0x03, tail[1]);
        Assert.Equal(1, tail[2]); // type stays 1 in the tail packet

        // The gold in the success packet matches the debited amount from the tail.
        uint cost = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(3));
        uint gold = BinaryPrimitives.ReadUInt32LittleEndian(success.AsSpan(3));
        Assert.Equal(100_000_000u - cost, gold);
    }

    // ---- zone change / warp list ----

    [Fact]
    public async Task ZoneChange_SameServer_SendsTeleportAndResetsState()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        user.WhoKilledMe = 55;
        user.LostExp = 100;

        var aiSent = new List<byte[]>();
        world.SendToAiServer = (_, buf) => aiSent.Add(buf);

        frames.Clear();
        user.ZoneChange(21, 200f, 300f);

        Assert.Equal(200f, user.UserData!.CurX);
        Assert.Equal(300f, user.UserData.CurZ);
        Assert.Equal(-1, user.UserData.Bind);
        Assert.Equal(-1, user.WhoKilledMe);
        Assert.Equal(0, user.LostExp);
        Assert.Equal(0x01, user.Warp);
        Assert.False(user.ZoneChangeFlag);

        byte[] teleport = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_ZONE_CHANGE);
        Assert.Equal(3, teleport[1]); // ZONE_CHANGE_TELEPORT
        Assert.Equal(21, teleport[2]);
        Assert.Equal(2000, BinaryPrimitives.ReadUInt16LittleEndian(teleport.AsSpan(4)));
        Assert.Equal(3000, BinaryPrimitives.ReadUInt16LittleEndian(teleport.AsSpan(6)));

        byte[] ai = Assert.Single(aiSent, b => b[0] == 57); // AG_ZONE_CHANGE
        Assert.Equal(user.SocketId, BinaryPrimitives.ReadInt16LittleEndian(ai.AsSpan(1)));
        Assert.Equal(21, ai[4]);
    }

    [Fact]
    public void SelectWarpList_SameZone_SetsSameZoneFlagAndWarps()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        world.Zones[0].Warps[11] = new WarpInfo
        {
            WarpId = 11, Zone = 21, X = 240f, Z = 240f, R = 0f, Nation = 0,
            WarpName = "town\0"u8.ToArray(), Announce = "gate\0"u8.ToArray(),
        };

        frames.Clear();
        var packet = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(packet, 11);
        user.SelectWarpList(packet);

        Assert.Equal(240f, user.UserData!.CurX);

        // Same-zone gates reply [WIZ_WARP_LIST][2][1] before the teleport.
        byte[] listReply = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_WARP_LIST);
        Assert.Equal(2, listReply[1]);
        Assert.Equal(1, listReply[2]);
    }

    [Fact]
    public void GetWarpList_FiltersByGroupAndFormatsEntries()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        world.Zones[0].Warps[11] = new WarpInfo
        {
            WarpId = 11, Zone = 21, X = 100f, Z = 150f, Y = 5f, Pay = 1000, Nation = 0,
            WarpName = "town\0garbage"u8.ToArray(), Announce = "gate\0"u8.ToArray(),
        };
        world.Zones[0].Warps[25] = new WarpInfo
        {
            WarpId = 25, Zone = 21, X = 0f, Z = 0f, Nation = 0,
            WarpName = "other\0"u8.ToArray(), Announce = "\0"u8.ToArray(),
        };

        frames.Clear();
        Assert.True(user.GetWarpList(1)); // group 1 → warp ids 10..19

        byte[] reply = Unframe(frames.Single());
        Assert.Equal((byte)GameOpcode.WIZ_WARP_LIST, reply[0]);
        Assert.Equal(1, reply[1]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(2))); // one entry

        // Entry: [warpId][name str2][announce str2][zone][maxUser][pay][x*10][z*10][y*10]
        int offset = 4;
        Assert.Equal(11, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset)));
        offset += 2;
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset));
        Assert.Equal(4, nameLen); // trimmed at the NUL
        offset += 2 + nameLen;
        short announceLen = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset));
        offset += 2 + announceLen;
        Assert.Equal(21, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset)));
        offset += 2;
        Assert.Equal(150, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset))); // MaxUsers default
        offset += 2;
        Assert.Equal(1000u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(offset)));
        offset += 4;
        Assert.Equal(1000, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset)));  // x*10
        Assert.Equal(1500, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset + 2))); // z*10
        Assert.Equal(50, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset + 4)));   // y*10
    }

    // ---- object events ----

    [Fact]
    public async Task BindObjectEvent_SetsBindPoint()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        world.Zones[0].ObjectEvents[7] = new OpenKO.Servers.Ebenezer.ObjectEvent
        {
            Index = 7, Type = 0, Belong = 0, Life = 1,
        };

        frames.Clear();
        var packet = new byte[5];
        packet[0] = (byte)GameOpcode.WIZ_OBJECT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 7);
        await user.ParsingAsync(packet);

        Assert.Equal(7, user.UserData!.Bind);
        byte[] reply = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_OBJECT_EVENT);
        Assert.Equal(0, reply[1]);
        Assert.Equal(1, reply[2]);
    }

    [Fact]
    public async Task BindObjectEvent_WrongNation_Rejected()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        world.Zones[0].ObjectEvents[7] = new OpenKO.Servers.Ebenezer.ObjectEvent
        {
            Index = 7, Type = 0, Belong = 2, Life = 1, // El Morad bind point
        };

        frames.Clear();
        var packet = new byte[5];
        packet[0] = (byte)GameOpcode.WIZ_OBJECT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 7);
        await user.ParsingAsync(packet);

        Assert.Equal(0, user.UserData!.Bind);
        byte[] reply = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_OBJECT_EVENT);
        Assert.Equal(0, reply[2]); // result 0, but the packet still comes from BindObjectEvent
    }

    [Fact]
    public async Task ObjectEvent_UnknownIndex_SendsTypelessFail()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        frames.Clear();
        var packet = new byte[5];
        packet[0] = (byte)GameOpcode.WIZ_OBJECT_EVENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 99);
        await user.ParsingAsync(packet);

        byte[] reply = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_OBJECT_EVENT);
        Assert.Equal(0, reply[1]); // objectType stays 0 for a missing event
        Assert.Equal(0, reply[2]);
    }

    // ---- misc ----

    [Fact]
    public async Task Home_WarpsIntoStartPositionBox()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");

        world.StartPositionTable[21] = new StartPosition
        {
            ZoneId = 21, KarusX = 200, KarusZ = 300, ElmoX = 50, ElmoZ = 60, RangeX = 10, RangeZ = 10,
        };

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_HOME]);

        // world.Rand = Math.Min → rand(0, range) = 0 → exactly the Karus corner.
        Assert.Equal(200f, user.UserData!.CurX);
        Assert.Equal(300f, user.UserData.CurZ);
    }

    [Fact]
    public async Task TargetHp_RequestEchoesHpForUserTarget()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        (GameUser target, _) = MakeUser(world, db, "bob");

        frames.Clear();
        var packet = new byte[4];
        packet[0] = (byte)GameOpcode.WIZ_TARGET_HP;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), target.SocketId);
        packet[3] = 0x01; // echo
        await user.ParsingAsync(packet);

        byte[] reply = Unframe(frames.Single());
        Assert.Equal((byte)GameOpcode.WIZ_TARGET_HP, reply[0]);
        Assert.Equal(target.SocketId, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(1)));
        Assert.Equal(1, reply[3]);
        Assert.Equal((uint)target.MaxHp, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(4)));
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(8)));
    }

    [Fact]
    public async Task UserDataSave_InvokesHookAndItemLog()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeUser(world, db, "alice");

        GameUser? saved = null;
        var logs = new List<byte[]>();
        world.SaveUserData = u => saved = u;
        world.ItemLogSink = logs.Add;

        await user.ParsingAsync([(byte)GameOpcode.WIZ_DATASAVE]);

        Assert.Same(user, saved);
        byte[] log = Assert.Single(logs);
        Assert.Equal((byte)GameOpcode.WIZ_DATASAVE, log[0]);
        // [acct str2][char str2][0x02][level][exp][loyalty][gold]
        short acctLen = BinaryPrimitives.ReadInt16LittleEndian(log.AsSpan(1));
        short charLen = BinaryPrimitives.ReadInt16LittleEndian(log.AsSpan(3 + acctLen));
        int offset = 5 + acctLen + charLen;
        Assert.Equal(0x02, log[offset]);
        Assert.Equal(user.UserData!.Level, log[offset + 1]);
    }

    [Fact]
    public async Task KickOut_OnlineUser_SavedAndClosed()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser gm, _) = MakeUser(world, db, "gm");
        (GameUser victim, _) = MakeUser(world, db, "victim");

        bool closed = false;
        GameUser? saved = null;
        victim.AccountId = victim.UserData!.AccountId; // set at login in the real flow
        victim.Close = () => closed = true;
        world.SaveUserData = u => saved = u;

        byte[] account = System.Text.Encoding.Latin1.GetBytes(victim.UserData.AccountId);
        var packet = new byte[3 + account.Length];
        packet[0] = (byte)GameOpcode.WIZ_KICKOUT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), (short)account.Length);
        account.CopyTo(packet.AsSpan(3));
        await gm.ParsingAsync(packet);

        Assert.True(closed);
        Assert.Same(victim, saved);
    }

    [Fact]
    public async Task ZoneConcurrentUsers_CountsZoneNationPairs()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "alice");
        (GameUser other, _) = MakeUser(world, db, "bob");
        other.UserData!.Nation = 2;

        frames.Clear();
        var packet = new byte[4];
        packet[0] = (byte)GameOpcode.WIZ_ZONE_CONCURRENT;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 21);
        packet[3] = 1; // KARUS
        await user.ParsingAsync(packet);

        byte[] reply = Unframe(frames.Single());
        Assert.Equal((byte)GameOpcode.WIZ_ZONE_CONCURRENT, reply[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(1)));
    }
}
