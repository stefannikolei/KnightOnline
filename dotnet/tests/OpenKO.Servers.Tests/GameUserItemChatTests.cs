using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer chat + inventory slice (stage 4.9): chat modes,
/// equip moves, the NPC merchant, loot pickup, destruction and repair.
/// </summary>
public class GameUserItemChatTests
{
    private const int SwordId = 810210000;
    private const int ArmorId = 220100000;

    private static Item MakeItem(int id, byte kind = 0, byte slot = 0, short damage = 0,
        int buyPrice = 0, byte sellType = 0, short weight = 10, short durability = 5000,
        byte countable = 0) => new()
    {
        ID = id,
        Name = $"item{id}",
        Kind = kind,
        Slot = slot,
        Race = 0,
        ClassId = 0,
        Damage = damage,
        Delay = 10,
        Range = 0,
        Weight = weight,
        Durability = durability,
        BuyPrice = buyPrice,
        SellPrice = sellType,
        Armor = 0,
        Countable = countable,
        MagicEffect = 0,
        SpecialEffect = 0,
        MinLevel = 1,
        MaxLevel = 83,
        RequiredRank = 0,
        RequiredTitle = 0,
        RequiredStrength = 0,
        RequiredStamina = 0,
        RequiredDexterity = 0,
        RequiredIntelligence = 0,
        RequiredCharisma = 0,
        SellingGroup = 0,
        Type = 0,
        HitRate = 0,
        EvasionRate = 0,
        DaggerArmor = 0,
        SwordArmor = 0,
        MaceArmor = 0,
        AxeArmor = 0,
        SpearArmor = 0,
        BowArmor = 0,
        FireDamage = 0,
        IceDamage = 0,
        LightningDamage = 0,
        PoisonDamage = 0,
        HpDrain = 0,
        MpDamage = 0,
        MpDrain = 0,
        MirrorDamage = 0,
        DropRate = 0,
        StrengthBonus = 0,
        StaminaBonus = 0,
        DexterityBonus = 0,
        IntelligenceBonus = 0,
        CharismaBonus = 0,
        MaxHpBonus = 0,
        MaxMpBonus = 0,
        FireResist = 0,
        ColdResist = 0,
        LightningResist = 0,
        MagicResist = 0,
        PoisonResist = 0,
        CurseResist = 0,
    };

    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f));
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
        world.LevelUpTable[10] = 1000;
        world.ItemTable[SwordId] = MakeItem(SwordId, kind: 21, slot: 1, damage: 100, buyPrice: 10000);
        world.ItemTable[ArmorId] = MakeItem(ArmorId, slot: 5, buyPrice: 5000); // breast piece
        world.ItemTable[900000000] = MakeItem(900000000, weight: 0, countable: 1); // Noah (gold)
        world.ServerResources[126] = "#### NOTICE : %s ####";
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeInGameUser(
        EbenezerWorld world, FakeDbAgent db, string charId, float x = 100, float z = 100)
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
        data.Class = 105;
        data.Level = 10;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.Gold = 100000;
        data.CurX = x;
        data.CurZ = z;
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

    private static byte[] ChatPacket(byte type, string text)
    {
        byte[] textBytes = System.Text.Encoding.Latin1.GetBytes(text);
        var packet = new byte[4 + textBytes.Length];
        packet[0] = 0x10; // WIZ_CHAT
        packet[1] = type;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2), (short)textBytes.Length);
        textBytes.CopyTo(packet.AsSpan(4));
        return packet;
    }

    [Fact]
    public async Task GeneralChat_ReachesOnlyNearbyListeners()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser speaker, _) = MakeInGameUser(world, db, "Speaker");
        (GameUser near, _) = MakeInGameUser(world, db, "Near", x: 110, z: 110);   // 14m away
        (GameUser far, _) = MakeInGameUser(world, db, "Far", x: 133, z: 133);     // same region, 46m away

        await speaker.ParsingAsync(ChatPacket(GameUser.GeneralChat, "hello"));

        byte[]? heard = near.RegionPacketClear();
        Assert.NotNull(heard);
        Assert.Equal(0x10, heard![5]); // WIZ_CHAT inside the region buffer
        Assert.Equal(GameUser.GeneralChat, heard[6]);

        Assert.Null(far.RegionPacketClear()); // outside the 32m radius
    }

    [Fact]
    public async Task PrivateChat_FlowsThroughTargetSelection()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser speaker, List<byte[]> speakerFrames) = MakeInGameUser(world, db, "Speaker");
        (GameUser friend, List<byte[]> friendFrames) = MakeInGameUser(world, db, "Friend", x: 300, z: 300);
        speakerFrames.Clear();

        // WIZ_CHAT_TARGET: select "Friend".
        byte[] name = "Friend"u8.ToArray();
        var select = new byte[3 + name.Length];
        select[0] = 0x35;
        BinaryPrimitives.WriteInt16LittleEndian(select.AsSpan(1), (short)name.Length);
        name.CopyTo(select.AsSpan(3));
        await speaker.ParsingAsync(select);

        Assert.Equal(friend.SocketId, speaker.PrivateChatUser);
        byte[] reply = Unframe(speakerFrames.Single(f => Unframe(f)[0] == 0x35));
        Assert.Equal(name.Length, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(1)));

        friendFrames.Clear();
        speakerFrames.Clear();
        await speaker.ParsingAsync(ChatPacket(GameUser.PrivateChat, "psst"));

        Assert.Single(friendFrames.Where(f => Unframe(f)[0] == 0x10));
        Assert.Single(speakerFrames.Where(f => Unframe(f)[0] == 0x10)); // echo to self
    }

    [Fact]
    public async Task PublicChat_RequiresManagerAndFormatsTheNotice()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser speaker, _) = MakeInGameUser(world, db, "GameMaster");
        (GameUser listener, List<byte[]> listenerFrames) = MakeInGameUser(world, db, "Player", x: 400, z: 400);
        listenerFrames.Clear();

        // A normal user cannot broadcast.
        await speaker.ParsingAsync(ChatPacket(GameUser.PublicChat, "spam"));
        Assert.Empty(listenerFrames);

        speaker.UserData!.Authority = GameConstants.AuthorityManager;
        await speaker.ParsingAsync(ChatPacket(GameUser.PublicChat, "maintenance"));

        byte[] payload = Unframe(Assert.Single(listenerFrames));
        Assert.Equal(0x10, payload[0]);
        string text = System.Text.Encoding.Latin1.GetString(payload[(7 + "GameMaster".Length)..]);
        Assert.Contains("#### NOTICE : maintenance ####", text);
    }

    [Fact]
    public async Task ShoutChat_CostsAFifthOfMaxMana()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser speaker, _) = MakeInGameUser(world, db, "Shouter");
        speaker.UserData!.Mp = speaker.MaxMp;

        await speaker.ParsingAsync(ChatPacket(GameUser.ShoutChat, "HEY"));

        Assert.Equal(speaker.MaxMp - speaker.MaxMp / 5, speaker.UserData.Mp);
    }

    [Fact]
    public void FormatResource_SubstitutesSprintfPlaceholders()
    {
        var world = MakeWorld();
        world.ServerResources[500] = "%s killed %s (%d points)";

        Assert.Equal("A killed B (42 points)", world.FormatResource(500, "A", "B", 42));
        Assert.Equal("999", world.FormatResource(999));            // unknown id
        Assert.Equal("500", world.FormatResource(500, "A"));       // missing args
    }

    [Fact]
    public async Task ItemMove_EquipsTheSwordAndRefreshesStats()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        user.UserData!.Items[GameConstants.SlotMax].Num = SwordId; // inventory slot 0
        user.UserData.Items[GameConstants.SlotMax].Duration = 5000;
        user.UserData.Items[GameConstants.SlotMax].Count = 1;
        frames.Clear();

        var packet = new byte[8];
        packet[0] = 0x1F; // WIZ_ITEM_MOVE
        packet[1] = 1;    // INVEN -> SLOT
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), SwordId);
        packet[6] = 0;    // src inventory pos
        packet[7] = GameConstants.SlotRightHand;
        await user.ParsingAsync(packet);

        Assert.Equal(SwordId, user.UserData.Items[GameConstants.SlotRightHand].Num);
        Assert.Equal(0, user.UserData.Items[GameConstants.SlotMax].Num);
        Assert.NotEqual(0L, user.UserData.Items[GameConstants.SlotRightHand].SerialNum);
        Assert.Equal(408, user.TotalHit); // ability recomputed with the sword

        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1F && p[1] == 0x01); // stat refresh
        Assert.Contains(payloads, p => p[0] == 0x54); // WIZ_WEIGHT_CHANGE
    }

    [Fact]
    public async Task ItemMove_WrongSlot_Fails()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        user.UserData!.Items[GameConstants.SlotMax].Num = ArmorId; // breast piece
        user.UserData.Items[GameConstants.SlotMax].Count = 1;
        frames.Clear();

        var packet = new byte[8];
        packet[0] = 0x1F;
        packet[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), ArmorId);
        packet[6] = 0;
        packet[7] = GameConstants.SlotRightHand; // armor cannot go there
        await user.ParsingAsync(packet);

        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x1F, payload[0]);
        Assert.Equal(0x00, payload[1]);
        Assert.Equal(ArmorId, user.UserData.Items[GameConstants.SlotMax].Num); // unchanged
    }

    [Fact]
    public async Task ItemTrade_BuysFromTheMerchant()
    {
        var world = MakeWorld();
        world.PointCheckFlag = true;
        world.Npcs[10005] = new GameNpc { Nid = 10005, Name = "Merchant", NpcType = 21, SellingGroup = 5000 };
        var itemLogs = new List<byte[]>();
        world.ItemLogSink = itemLogs.Add;

        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        frames.Clear();

        var packet = new byte[16];
        var offset = 0;
        packet[offset++] = 0x21; // WIZ_ITEM_TRADE
        packet[offset++] = 0x01; // buy
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 5000); offset += 4;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(offset), 10005); offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), SwordId); offset += 4;
        packet[offset++] = 3; // inventory pos
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(offset), 1);
        await user.ParsingAsync(packet);

        Assert.Equal(SwordId, user.UserData!.Items[GameConstants.SlotMax + 3].Num);
        Assert.Equal(90000, user.UserData.Gold); // -10000

        byte[] reply = Unframe(frames.Single(f => Unframe(f)[0] == 0x21));
        Assert.Equal(0x01, reply[1]);
        Assert.Equal(90000u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(2)));

        byte[] log = Assert.Single(itemLogs);
        Assert.Equal((byte)GameOpcode.WIZ_ITEM_LOG, log[0]);
    }

    [Fact]
    public async Task ItemTrade_SellsAtAQuarterOfTheBuyPrice()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeInGameUser(world, db, "Hero");
        user.UserData!.Items[GameConstants.SlotMax + 2].Num = SwordId;
        user.UserData.Items[GameConstants.SlotMax + 2].Count = 1;
        user.UserData.Items[GameConstants.SlotMax + 2].Duration = 4000;

        var packet = new byte[16];
        var offset = 0;
        packet[offset++] = 0x21;
        packet[offset++] = 0x02; // sell
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), SwordId); offset += 4;
        packet[offset++] = 2;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(offset), 1);
        await user.ParsingAsync(packet);

        Assert.Equal(0, user.UserData.Items[GameConstants.SlotMax + 2].Num);
        Assert.Equal(102500, user.UserData.Gold); // +10000/4
    }

    [Fact]
    public async Task ItemGet_PicksUpLootFromTheOwnRegion()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");

        var bundle = new ZoneItem { BundleIndex = 1 };
        bundle.ItemId[0] = SwordId;
        bundle.Count[0] = 1;
        world.Zones[0].RegionItemAdd(user.RegionX, user.RegionZ, bundle);
        frames.Clear();

        var packet = new byte[9];
        packet[0] = 0x26; // WIZ_ITEM_GET
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), SwordId);
        await user.ParsingAsync(packet);

        Assert.Equal(SwordId, user.UserData!.Items[GameConstants.SlotMax].Num);
        Assert.Empty(world.Zones[0].Regions[user.RegionX, user.RegionZ].Items); // bundle consumed

        byte[] reply = Unframe(frames.Single(f => Unframe(f)[0] == 0x26));
        Assert.Equal(0x01, reply[1]);
        Assert.Equal(0, reply[2]); // slot 0
    }

    [Fact]
    public async Task ItemGet_GoldGoesStraightToThePurse()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");

        var bundle = new ZoneItem { BundleIndex = 1 };
        bundle.ItemId[0] = 900000000; // ITEM_GOLD
        bundle.Count[0] = 500;
        world.Zones[0].RegionItemAdd(user.RegionX, user.RegionZ, bundle);
        frames.Clear();

        var packet = new byte[9];
        packet[0] = 0x26;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), 900000000);
        await user.ParsingAsync(packet);

        Assert.Equal(100500, user.UserData!.Gold);
        byte[] reply = Unframe(frames.Single(f => Unframe(f)[0] == 0x26));
        Assert.Equal(100500u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(9)));
    }

    [Fact]
    public async Task ItemRemove_DestroysAndLogs()
    {
        var world = MakeWorld();
        var itemLogs = new List<byte[]>();
        world.ItemLogSink = itemLogs.Add;
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        user.UserData!.Items[GameConstants.SlotMax + 1].Num = SwordId;
        user.UserData.Items[GameConstants.SlotMax + 1].Count = 1;
        frames.Clear();

        var packet = new byte[7];
        packet[0] = 0x3F; // WIZ_ITEM_REMOVE
        packet[1] = 2;    // inventory
        packet[2] = 1;    // pos
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), SwordId);
        await user.ParsingAsync(packet);

        Assert.Equal(0, user.UserData.Items[GameConstants.SlotMax + 1].Num);
        byte[] reply = Unframe(frames.Single(f => Unframe(f)[0] == 0x3F));
        Assert.Equal(0x01, reply[1]);
        Assert.Single(itemLogs);
    }

    [Fact]
    public async Task ItemRepair_ChargesTheFormulaPrice()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        user.UserData!.Items[GameConstants.SlotRightHand].Num = SwordId;
        user.UserData.Items[GameConstants.SlotRightHand].Duration = 4000;
        frames.Clear();

        var packet = new byte[7];
        packet[0] = 0x3B; // WIZ_ITEM_REPAIR
        packet[1] = 1;    // equip slot
        packet[2] = GameConstants.SlotRightHand;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), SwordId);
        await user.ParsingAsync(packet);

        // money = ((10000-10)/10000 + 10000^0.75) * 1000 / 5000 = 200.
        Assert.Equal(5000, user.UserData.Items[GameConstants.SlotRightHand].Duration);
        Assert.Equal(99800, user.UserData.Gold);

        byte[] reply = Unframe(frames.Single(f => Unframe(f)[0] == 0x3B));
        Assert.Equal(0x01, reply[1]);
    }

    [Fact]
    public async Task NpcEvent_MerchantOpensTheTradeWindow()
    {
        var world = MakeWorld();
        world.PointCheckFlag = true;
        world.Npcs[10005] = new GameNpc { Nid = 10005, NpcType = 21, SellingGroup = 5000 };
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeInGameUser(world, db, "Hero");
        frames.Clear();

        var packet = new byte[3];
        packet[0] = 0x20; // WIZ_NPC_EVENT
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 10005);
        await user.ParsingAsync(packet);

        byte[] reply = Unframe(Assert.Single(frames));
        Assert.Equal(0x25, reply[0]); // WIZ_TRADE_NPC
        Assert.Equal(5000u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(1)));
    }
}
