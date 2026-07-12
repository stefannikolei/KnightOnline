using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the party slice (stage 4.10): the WIZ_PARTY invite/accept flow,
/// broadcasts, loot routing and the gold split.
/// </summary>
public class GameUserPartyTests
{
    private const int SwordId = 810210000;

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
        world.LevelUpTable[12] = 3000;
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
        world.ItemTable[900000000] = world.ItemTable[SwordId] with { ID = 900000000, Weight = 0, Countable = 1 };
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeMember(
        EbenezerWorld world, FakeDbAgent db, string charId, byte level = 10, float x = 100, float z = 100)
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
        data.Level = level;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.Gold = 0;
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

    private static async Task FormParty(GameUser leader, GameUser member)
    {
        byte[] name = System.Text.Encoding.Latin1.GetBytes(member.UserData!.CharId);
        var invite = new byte[4 + name.Length];
        invite[0] = 0x2F; // WIZ_PARTY
        invite[1] = GameUser.PartyCreate;
        BinaryPrimitives.WriteInt16LittleEndian(invite.AsSpan(2), (short)name.Length);
        name.CopyTo(invite.AsSpan(4));
        await leader.ParsingAsync(invite);

        await member.ParsingAsync([0x2F, GameUser.PartyPermit, 0x01]); // accept
    }

    [Fact]
    public async Task PartyCreate_InviteAcceptFlow()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));
        (GameUser leader, List<byte[]> leaderFrames) = MakeMember(world, db, "Leader");
        (GameUser member, List<byte[]> memberFrames) = MakeMember(world, db, "Member", x: 110, z: 110);
        leaderFrames.Clear();
        memberFrames.Clear();

        await FormParty(leader, member);

        Assert.Equal(leader.PartyIndex, member.PartyIndex);
        Assert.NotEqual(-1, leader.PartyIndex);

        PartyGroup party = world.Parties[leader.PartyIndex];
        Assert.Equal(leader.SocketId, party.Uid[0]);
        Assert.Equal(member.SocketId, party.Uid[1]);
        Assert.Equal(10, party.Level[0]);

        // The member got the PARTY_PERMIT invite with the leader's name.
        byte[] permit = memberFrames.Select(Unframe).First(p => p[0] == 0x2F && p[1] == GameUser.PartyPermit);
        Assert.Equal(leader.SocketId, BinaryPrimitives.ReadInt16LittleEndian(permit.AsSpan(2)));

        // Both got the PARTY_INSERT broadcast for the member.
        Assert.Contains(leaderFrames.Select(Unframe), p => p[0] == 0x2F && p[1] == GameUser.PartyInsert);

        // AI server heard the create and the insert.
        Assert.Contains(aiPackets, p => p.Data[0] == AiOpcode.AG_USER_PARTY && p.Data[1] == GameUser.PartyCreate);
        Assert.Contains(aiPackets, p => p.Data[0] == AiOpcode.AG_USER_PARTY && p.Data[1] == GameUser.PartyInsert);
    }

    [Fact]
    public async Task PartyDecline_DisbandsTheFreshParty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, List<byte[]> leaderFrames) = MakeMember(world, db, "Leader");
        (GameUser member, _) = MakeMember(world, db, "Member", x: 110, z: 110);
        leaderFrames.Clear();

        byte[] name = "Member"u8.ToArray();
        var invite = new byte[4 + name.Length];
        invite[0] = 0x2F;
        invite[1] = GameUser.PartyCreate;
        BinaryPrimitives.WriteInt16LittleEndian(invite.AsSpan(2), (short)name.Length);
        name.CopyTo(invite.AsSpan(4));
        await leader.ParsingAsync(invite);

        await member.ParsingAsync([0x2F, GameUser.PartyPermit, 0x00]); // decline

        Assert.Equal(-1, leader.PartyIndex);
        Assert.Equal(-1, member.PartyIndex);
        Assert.Empty(world.Parties);

        // The leader got the -1 rejection notice.
        byte[] reject = leaderFrames.Select(Unframe).Last(p => p[0] == 0x2F && p[1] == GameUser.PartyInsert);
        Assert.Equal(-1, BinaryPrimitives.ReadInt16LittleEndian(reject.AsSpan(2)));
    }

    [Fact]
    public async Task PartyRequest_RejectsLevelGap()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, List<byte[]> leaderFrames) = MakeMember(world, db, "Leader", level: 10);
        MakeMember(world, db, "Lowbie", level: 1, x: 110, z: 110); // outside the ±8 band
        leaderFrames.Clear();

        byte[] name = "Lowbie"u8.ToArray();
        var invite = new byte[4 + name.Length];
        invite[0] = 0x2F;
        invite[1] = GameUser.PartyCreate;
        BinaryPrimitives.WriteInt16LittleEndian(invite.AsSpan(2), (short)name.Length);
        name.CopyTo(invite.AsSpan(4));
        await leader.ParsingAsync(invite);

        Assert.Equal(-1, leader.PartyIndex);
        byte[] fail = Unframe(Assert.Single(leaderFrames));
        Assert.Equal(-2, BinaryPrimitives.ReadInt16LittleEndian(fail.AsSpan(2)));
    }

    [Fact]
    public async Task PartyLeaderLeaving_DisbandsTheParty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, _) = MakeMember(world, db, "Leader");
        (GameUser member, List<byte[]> memberFrames) = MakeMember(world, db, "Member", x: 110, z: 110);
        await FormParty(leader, member);
        memberFrames.Clear();

        var packet = new byte[4];
        packet[0] = 0x2F;
        packet[1] = GameUser.PartyRemove;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2), leader.SocketId);
        await leader.ParsingAsync(packet);

        Assert.Equal(-1, leader.PartyIndex);
        Assert.Equal(-1, member.PartyIndex);
        Assert.Empty(world.Parties);
        Assert.Contains(memberFrames.Select(Unframe), p => p[0] == 0x2F && p[1] == GameUser.PartyDelete);
    }

    [Fact]
    public async Task HpChange_BroadcastsPartyHpChange()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, _) = MakeMember(world, db, "Leader");
        (GameUser member, List<byte[]> memberFrames) = MakeMember(world, db, "Member", x: 110, z: 110);
        await FormParty(leader, member);
        memberFrames.Clear();

        leader.HpChange(-30);

        byte[] update = memberFrames.Select(Unframe).Single(p => p[0] == 0x2F && p[1] == GameUser.PartyHpChange);
        Assert.Equal(leader.SocketId, BinaryPrimitives.ReadInt16LittleEndian(update.AsSpan(2)));
        Assert.Equal(leader.UserData!.Hp, BinaryPrimitives.ReadInt16LittleEndian(update.AsSpan(6)));
    }

    [Fact]
    public async Task PartyChat_ReachesAllMembers()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, _) = MakeMember(world, db, "Leader");
        (GameUser member, List<byte[]> memberFrames) = MakeMember(world, db, "Member", x: 400, z: 400);
        await FormParty(leader, member);
        memberFrames.Clear();

        byte[] text = "inc left"u8.ToArray();
        var packet = new byte[4 + text.Length];
        packet[0] = 0x10; // WIZ_CHAT
        packet[1] = GameUser.PartyChat;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2), (short)text.Length);
        text.CopyTo(packet.AsSpan(4));
        await leader.ParsingAsync(packet);

        byte[] chat = memberFrames.Select(Unframe).Single(p => p[0] == 0x10);
        Assert.Equal(GameUser.PartyChat, chat[1]);
    }

    [Fact]
    public async Task ItemGet_RoutesLootThroughTheParty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, List<byte[]> leaderFrames) = MakeMember(world, db, "Leader");
        (GameUser member, List<byte[]> memberFrames) = MakeMember(world, db, "Member", x: 110, z: 110);
        await FormParty(leader, member);
        leaderFrames.Clear();
        memberFrames.Clear();

        var bundle = new ZoneItem { BundleIndex = 1 };
        bundle.ItemId[0] = SwordId;
        bundle.Count[0] = 1;
        world.Zones[0].RegionItemAdd(member.RegionX, member.RegionZ, bundle);

        // The MEMBER picks up, but the router starts at slot 0 → the leader receives.
        var packet = new byte[9];
        packet[0] = 0x26; // WIZ_ITEM_GET
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), SwordId);
        await member.ParsingAsync(packet);

        Assert.Equal(SwordId, leader.UserData!.Items[GameConstants.SlotMax].Num);
        Assert.Equal(0, member.UserData!.Items[GameConstants.SlotMax].Num);

        // The receiver sees 0x05 (routed), the picker the 0x04 notice, the party the 0x03 note.
        Assert.Contains(leaderFrames.Select(Unframe), p => p[0] == 0x26 && p[1] == 0x05);
        Assert.Contains(memberFrames.Select(Unframe), p => p[0] == 0x26 && p[1] == 0x04);
        Assert.Contains(memberFrames.Select(Unframe), p => p[0] == 0x26 && p[1] == 0x03);
        Assert.Equal(1, world.Parties[leader.PartyIndex].ItemRouting);
    }

    [Fact]
    public async Task ItemGet_SplitsGoldByLevel()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, _) = MakeMember(world, db, "Leader", level: 10);
        (GameUser member, _) = MakeMember(world, db, "Member", level: 12, x: 110, z: 110);
        await FormParty(leader, member);

        var bundle = new ZoneItem { BundleIndex = 1 };
        bundle.ItemId[0] = 900000000; // gold
        bundle.Count[0] = 1100;
        world.Zones[0].RegionItemAdd(member.RegionX, member.RegionZ, bundle);

        var packet = new byte[9];
        packet[0] = 0x26;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), 900000000);
        await member.ParsingAsync(packet);

        // level sum 22: leader 10/22, member 12/22 of 1100.
        Assert.Equal(500, leader.UserData!.Gold);
        Assert.Equal(600, member.UserData!.Gold);
    }

    [Fact]
    public async Task Disconnect_LeavesTheParty()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser leader, _) = MakeMember(world, db, "Leader");
        (GameUser member, _) = MakeMember(world, db, "Member", x: 110, z: 110);
        (GameUser third, _) = MakeMember(world, db, "Third", x: 120, z: 120);
        await FormParty(leader, member);

        // Third joins via a direct insert invite.
        byte[] name = "Third"u8.ToArray();
        var invite = new byte[4 + name.Length];
        invite[0] = 0x2F;
        invite[1] = GameUser.PartyInsert;
        BinaryPrimitives.WriteInt16LittleEndian(invite.AsSpan(2), (short)name.Length);
        name.CopyTo(invite.AsSpan(4));
        await leader.ParsingAsync(invite);
        await third.ParsingAsync([0x2F, GameUser.PartyPermit, 0x01]);

        Assert.Equal(leader.PartyIndex, third.PartyIndex);

        // The member "disconnects" (the close path removes it from the party).
        member.PartyRemoveMember(member.SocketId);

        Assert.Equal(-1, member.PartyIndex);
        Assert.NotEqual(-1, leader.PartyIndex);
        Assert.DoesNotContain(member.SocketId, world.Parties[leader.PartyIndex].Uid);
    }
}
