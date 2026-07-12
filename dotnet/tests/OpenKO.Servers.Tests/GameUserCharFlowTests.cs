using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer pre-game character flow (WIZ_SEL_NATION, WIZ_NEW_CHAR,
/// WIZ_DEL_CHAR, WIZ_ALLCHAR_INFO_REQ, WIZ_SEL_CHAR).
/// </summary>
public class GameUserCharFlowTests
{
    private static Coefficient MakeCoefficient(short classId) => new()
    {
        ClassId = classId,
        ShortSword = 1,
        Sword = 1,
        Axe = 1,
        Club = 1,
        Spear = 1,
        Pole = 1,
        Staff = 1,
        Bow = 1,
        HitPoint = 1,
        ManaPoint = 1,
        Sp = 1,
        Armor = 1,
        HitRate = 1,
        EvasionRate = 1,
    };

    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new ZoneMeta(ServerNo: 1, ZoneNumber: 21));
        world.Zones.Add(new ZoneMeta(ServerNo: 2, ZoneNumber: 22));
        world.ServerInfos[1] = new ZoneServerInfo(1, "10.0.0.1", 15001);
        world.ServerInfos[2] = new ZoneServerInfo(2, "10.0.0.2", 15002);
        world.CoefficientTable[105] = MakeCoefficient(105);
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeUser(
        EbenezerWorld world, FakeDbAgent db, string account = "acct")
    {
        var frames = new List<byte[]>();
        short id = world.Register(i => new GameUser(i, world, db, NullLogger.Instance));
        GameUser user = world.Users[id]!;
        user.AccountId = account;
        user.Transmit = frame =>
        {
            frames.Add(frame);
            return true;
        };
        return (user, frames);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    private static byte[] NewCharPacket(
        string charId, byte index = 0, byte race = 1, short cls = 105,
        byte str = 60, byte sta = 60, byte dex = 60, byte intel = 60, byte cha = 60)
    {
        byte[] id = Encoding.Latin1.GetBytes(charId);
        var packet = new byte[15 + id.Length];
        var i = 0;
        packet[i++] = 0x02; // WIZ_NEW_CHAR
        packet[i++] = index;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(i), (short)id.Length);
        i += 2;
        id.CopyTo(packet, i);
        i += id.Length;
        packet[i++] = race;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(i), cls);
        i += 2;
        packet[i++] = 5;  // face
        packet[i++] = 3;  // hair
        packet[i++] = str;
        packet[i++] = sta;
        packet[i++] = dex;
        packet[i++] = intel;
        packet[i++] = cha;
        return packet;
    }

    private static byte[] SelCharPacket(string account, string charId, byte init = 1, byte zone = 21)
    {
        byte[] acc = Encoding.Latin1.GetBytes(account);
        byte[] chr = Encoding.Latin1.GetBytes(charId);
        var packet = new byte[7 + acc.Length + chr.Length];
        var i = 0;
        packet[i++] = 0x04; // WIZ_SEL_CHAR
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(i), (short)acc.Length);
        i += 2;
        acc.CopyTo(packet, i);
        i += acc.Length;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(i), (short)chr.Length);
        i += 2;
        chr.CopyTo(packet, i);
        i += chr.Length;
        packet[i++] = init;
        packet[i] = zone;
        return packet;
    }

    [Fact]
    public async Task SelNation_ValidNation_RunsNationSelect()
    {
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), new FakeDbAgent());
        var db = new FakeDbAgent();
        (user, frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync([0x05, 0x02]); // WIZ_SEL_NATION, elmorad

        Assert.Equal(("acct", 2), Assert.Single(db.NationSelectCalls));
        Assert.Equal(new byte[] { 0x05, 0x02 }, Unframe(Assert.Single(frames)));
    }

    [Fact]
    public async Task SelNation_InvalidNation_FailsWithoutDbCall()
    {
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync([0x05, 0x03]);

        Assert.Empty(db.NationSelectCalls);
        Assert.Equal(new byte[] { 0x05, 0x00 }, Unframe(Assert.Single(frames)));
    }

    [Fact]
    public async Task NewChar_HappyPath_UsesDbResult()
    {
        var db = new FakeDbAgent { CreateNewCharResult = NewCharResult.Success };
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync(NewCharPacket("Hero"));

        (string account, int index, string charId, int race, int cls) = Assert.Single(db.CreateNewCharCalls);
        Assert.Equal(("acct", 0, "Hero", 1, 105), (account, index, charId, race, cls));
        Assert.Equal(new byte[] { 0x02, 0x00 }, Unframe(Assert.Single(frames))); // NEW_CHAR_SUCCESS = 0
    }

    [Theory]
    [InlineData("Bad Name", 0x05)] // blocked substring (space)
    [InlineData("xKnightx", 0x05)] // blocked substring (case-sensitive "Knight")
    public async Task NewChar_InvalidName_Rejected(string name, byte expected)
    {
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync(NewCharPacket(name));

        Assert.Empty(db.CreateNewCharCalls);
        Assert.Equal(new byte[] { 0x02, expected }, Unframe(Assert.Single(frames)));
    }

    [Fact]
    public async Task NewChar_StatValidation()
    {
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        // Unknown class → 0x02.
        await user.ParsingAsync(NewCharPacket("Hero", cls: 999));
        Assert.Equal(new byte[] { 0x02, 0x02 }, Unframe(frames[^1]));

        // Sum over 300 → 0x02.
        await user.ParsingAsync(NewCharPacket("Hero", str: 100, sta: 100, dex: 100, intel: 51, cha: 50));
        Assert.Equal(new byte[] { 0x02, 0x02 }, Unframe(frames[^1]));

        // A stat below 50 → 0x11.
        await user.ParsingAsync(NewCharPacket("Hero", str: 49, sta: 60, dex: 60, intel: 60, cha: 60));
        Assert.Equal(new byte[] { 0x02, 0x11 }, Unframe(frames[^1]));

        Assert.Empty(db.CreateNewCharCalls);
    }

    [Fact]
    public async Task DelChar_RepliesUnimplementedResult()
    {
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        byte[] packet = [0x03, 0, 2, 0, (byte)'h', (byte)'i', 4, 0, (byte)'1', (byte)'2', (byte)'3', (byte)'4'];
        await user.ParsingAsync(packet);

        Assert.Equal(new byte[] { 0x03, 0x00, 0xFF }, Unframe(Assert.Single(frames)));
    }

    [Fact]
    public async Task AllCharInfo_SerializesSlotsByteExactly()
    {
        var db = new FakeDbAgent
        {
            AllCharIds = new AllCharIds("Hero", "", ""),
        };
        db.CharInfos["Hero"] = new CharInfo(
            "Hero", Race: 1, Class: 105, Level: 42, Face: 5, HairColor: 3, Zone: 21,
            [(101, 5000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (202, 1234)]);

        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync([0x0C]); // WIZ_ALLCHAR_INFO_REQ

        byte[] p = Unframe(Assert.Single(frames));
        Assert.Equal(0x0C, p[0]);
        Assert.Equal(0x01, p[1]);

        // Slot 1: [len=4]["Hero"][race][class i16][level][face][hair][zone][8×(u32,i16)].
        int i = 2;
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(i)));
        i += 2;
        Assert.Equal("Hero", Encoding.Latin1.GetString(p, i, 4));
        i += 4;
        Assert.Equal(1, p[i++]);
        Assert.Equal(105, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(i)));
        i += 2;
        Assert.Equal(42, p[i++]);
        Assert.Equal(5, p[i++]);
        Assert.Equal(3, p[i++]);
        Assert.Equal(21, p[i++]);
        Assert.Equal(101u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i)));
        Assert.Equal(5000, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(i + 4)));
        i += 7 * 6;
        Assert.Equal(202u, BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i)));
        i += 6;

        // Two empty slots: [len=0] + 7 zero field bytes + 8 empty items each.
        int emptySlotSize = 2 + 0 + 7 + 8 * 6;
        Assert.Equal(i + 2 * emptySlotSize, p.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(i)));
    }

    [Fact]
    public async Task SelChar_HappyPath_LoadsUserAndRepliesPosition()
    {
        var db = new FakeDbAgent
        {
            PopulateUserData = u =>
            {
                u.Zone = 21;
                u.Nation = 1;
                u.CurX = 512.3f;
                u.CurZ = 768.7f;
                u.CurY = 30.5f;
                u.Level = 10;
            },
        };
        EbenezerWorld world = MakeWorld();
        world.OldVictory = 2;
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        // The user store slot must exist for LoadUserData to fill.
        Assert.NotNull(db.Users.Get(user.SocketId));

        await user.ParsingAsync(SelCharPacket("acct", "Hero"));

        byte[] p = Unframe(Assert.Single(frames));
        Assert.Equal(0x04, p[0]); // WIZ_SEL_CHAR
        Assert.Equal(0x01, p[1]); // success
        Assert.Equal(21, p[2]);   // zone
        Assert.Equal((ushort)(512.3f * 10), BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(3)));
        Assert.Equal((ushort)(768.7f * 10), BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(5)));
        Assert.Equal((short)(30.5f * 10), BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(7)));
        Assert.Equal(2, p[9]);    // old victory

        Assert.NotNull(user.UserData);
        Assert.Equal("Hero", user.UserData!.CharId);
        Assert.Equal("acct", user.UserData.AccountId);
        Assert.Equal(("acct", "Hero", (byte)1), Assert.Single(db.SetLoginInfoCalls));
    }

    [Fact]
    public async Task SelChar_OtherServerZone_SendsServerChange()
    {
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync(SelCharPacket("acct", "Hero", zone: 22)); // zone 22 → server 2

        byte[] p = Unframe(Assert.Single(frames));
        Assert.Equal(0x46, p[0]); // WIZ_SERVER_CHANGE
        short ipLen = BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(1));
        Assert.Equal("10.0.0.2", Encoding.Latin1.GetString(p, 3, ipLen));
        Assert.Equal(15002, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(3 + ipLen)));
        Assert.Equal(1, p[5 + ipLen]);  // init
        Assert.Equal(22, p[6 + ipLen]); // zone
    }

    [Fact]
    public async Task SelChar_LoadFailure_RepliesZero()
    {
        var db = new FakeDbAgent { PopulateUserData = null }; // LoadUserData fails
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);

        await user.ParsingAsync(SelCharPacket("acct", "Hero"));

        Assert.Equal(new byte[] { 0x04, 0x00 }, Unframe(Assert.Single(frames)));
        Assert.Null(user.UserData);
    }

    [Fact]
    public async Task SelChar_BlockedAuthority_ClosesWithoutReply()
    {
        var db = new FakeDbAgent
        {
            PopulateUserData = u =>
            {
                u.Zone = 21;
                u.Nation = 1;
                u.Authority = 255; // AUTHORITY_BLOCK_USER
            },
        };
        (GameUser user, List<byte[]> frames) = MakeUser(MakeWorld(), db);
        bool closed = false;
        user.Close = () => closed = true;

        await user.ParsingAsync(SelCharPacket("acct", "Hero"));

        Assert.True(closed);
        Assert.Empty(frames);
    }

    [Fact]
    public async Task SelChar_CharLoadedInOtherSlot_LogsThatSlotOut()
    {
        var db = new FakeDbAgent();
        EbenezerWorld world = MakeWorld();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        // Simulate the character being resident in store slot 7.
        UserData stale = db.Users.Get(7)!;
        stale.AccountId = "other";
        stale.CharId = "Hero";

        await user.ParsingAsync(SelCharPacket("acct", "Hero"));

        // The C++ saves + resets that slot and never answers the requester.
        Assert.Empty(frames);
        Assert.Equal("other", Assert.Single(db.AccountLogoutCalls));
        Assert.Contains(7, db.UpdateUserCalls);
        Assert.Equal(string.Empty, db.Users.Get(7)!.CharId);
    }
}
