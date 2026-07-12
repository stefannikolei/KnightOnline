using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the knights/clan slice (stage 4.13): creation, invite/join,
/// withdraw/destroy, member listing and the fame changes.
/// </summary>
public class GameUserKnightsTests
{
    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 1, mapSize: 480f) { Type = 1 });
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
        world.LevelUpTable[30] = 1000;
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeUser(
        EbenezerWorld world, FakeDbAgent db, string charId, byte level = 30)
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
        data.Zone = 1;
        data.Nation = 1;
        data.Race = 1;
        data.Class = 105;
        data.Level = level;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.Gold = 1_000_000;
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

    private static byte[] CreatePacket(string name)
    {
        byte[] nameBytes = System.Text.Encoding.Latin1.GetBytes(name);
        var packet = new byte[4 + nameBytes.Length];
        packet[0] = (byte)GameOpcode.WIZ_KNIGHTS_PROCESS;
        packet[1] = GameUser.KnightsCreate;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2), (short)nameBytes.Length);
        nameBytes.CopyTo(packet.AsSpan(4));
        return packet;
    }

    [Fact]
    public async Task Create_Succeeds_ChargesGoldAndRegistersClan()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, List<byte[]> frames) = MakeUser(world, db, "chief");

        frames.Clear();
        await chief.ParsingAsync(CreatePacket("TestClan"));

        (int knightsId, int nation, string name, string chiefName, int flag) = Assert.Single(db.CreateKnightsCalls);
        Assert.Equal(1, knightsId);      // first Karus clan index
        Assert.Equal(1, nation);
        Assert.Equal("TestClan", name);
        Assert.Equal("chief", chiefName);
        Assert.Equal(1, flag);           // CLAN_TYPE

        Assert.Equal(1, chief.UserData!.Knights);
        Assert.Equal(1, chief.UserData.Fame); // CHIEF
        Assert.Equal(500_000, chief.UserData.Gold);

        KnightsClan clan = world.Knights[1];
        Assert.Equal("TestClan", clan.Name);
        Assert.Equal("chief", clan.Chief);
        Assert.Equal(1, clan.Members);
        Assert.Equal(1, Assert.Single(clan.Users, u => u.Used == 1 && u.UserName == "chief").Used);

        // The success announcement goes to the region buffered (direct: false),
        // so it sits in the region packet buffer until the 200ms flush.
        byte[]? buffered = chief.RegionPacketClear();
        Assert.NotNull(buffered);
        // [WIZ_CONTINOUS_PACKET][len i16][[len i16][payload]] — first entry.
        byte[] reply = buffered[5..];
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, reply[0]);
        Assert.Equal(GameUser.KnightsCreate, reply[1]);
        Assert.Equal(1, reply[2]);
    }

    [Fact]
    public async Task Create_UnderLevel20_FailsWithCode2()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        world.LevelUpTable[10] = 100;
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "lowbie", level: 10);

        frames.Clear();
        await user.ParsingAsync(CreatePacket("NoClan"));

        Assert.Empty(db.CreateKnightsCalls);
        byte[] reply = Unframe(frames.Single());
        Assert.Equal(GameUser.KnightsCreate, reply[1]);
        Assert.Equal(2, reply[2]);
    }

    [Fact]
    public async Task Create_DuplicateName_FailsWithCode3()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        world.Knights[5] = new KnightsClan { Index = 5, Name = "TAKEN", Nation = 1 };
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "chief");

        frames.Clear();
        await user.ParsingAsync(CreatePacket("taken")); // case-insensitive

        byte[] reply = Unframe(frames.Single());
        Assert.Equal(3, reply[2]);
    }

    [Fact]
    public async Task JoinFlow_InviteAcceptRegistersMember()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, _) = MakeUser(world, db, "chief");
        (GameUser member, List<byte[]> memberFrames) = MakeUser(world, db, "member");

        await chief.ParsingAsync(CreatePacket("TestClan"));
        memberFrames.Clear();

        // Chief invites the member.
        var invite = new byte[4];
        invite[0] = (byte)GameOpcode.WIZ_KNIGHTS_PROCESS;
        invite[1] = GameUser.KnightsJoin;
        BinaryPrimitives.WriteInt16LittleEndian(invite.AsSpan(2), member.SocketId);
        await chief.ParsingAsync(invite);

        byte[] req = Unframe(memberFrames.Single());
        Assert.Equal(GameUser.KnightsJoinReq, req[1]);
        Assert.Equal(1, req[2]);
        Assert.Equal(chief.SocketId, BinaryPrimitives.ReadInt16LittleEndian(req.AsSpan(3)));
        short knightsIndex = BinaryPrimitives.ReadInt16LittleEndian(req.AsSpan(5));
        Assert.Equal(1, knightsIndex);

        // Member accepts.
        var accept = new byte[7];
        accept[0] = (byte)GameOpcode.WIZ_KNIGHTS_PROCESS;
        accept[1] = GameUser.KnightsJoinReq;
        accept[2] = 1; // flag
        BinaryPrimitives.WriteInt16LittleEndian(accept.AsSpan(3), chief.SocketId);
        BinaryPrimitives.WriteInt16LittleEndian(accept.AsSpan(5), knightsIndex);
        await member.ParsingAsync(accept);

        (int type, string charId, int updatedIndex) = Assert.Single(db.UpdateKnightsCalls);
        Assert.Equal(0x12, type); // Aujard KNIGHTS_JOIN
        Assert.Equal("member", charId);
        Assert.Equal(1, updatedIndex);

        Assert.Equal(1, member.UserData!.Knights);
        Assert.Equal(5, member.UserData.Fame); // TRAINEE
        Assert.Contains(world.Knights[1].Users, u => u.Used == 1 && u.UserName == "member");
    }

    [Fact]
    public async Task Withdraw_Member_LeavesClan()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser member, List<byte[]> frames) = MakeUser(world, db, "member");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1, Members = 2 };
        world.AddKnightsUser(7, "member");
        member.UserData!.Knights = 7;
        member.UserData.Fame = 5; // TRAINEE

        frames.Clear();
        await member.ParsingAsync([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, GameUser.KnightsWithdraw]);

        (int type, string charId, _) = Assert.Single(db.UpdateKnightsCalls);
        Assert.Equal(0x13, type); // Aujard KNIGHTS_WITHDRAW
        Assert.Equal("member", charId);
        Assert.Equal(0, member.UserData.Knights);
        Assert.Equal(0, member.UserData.Fame);
        Assert.DoesNotContain(world.Knights[7].Users, u => u.Used == 1 && u.UserName == "member");
    }

    [Fact]
    public async Task Withdraw_Chief_DestroysClan()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, List<byte[]> frames) = MakeUser(world, db, "chief");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1, Members = 1 };
        world.AddKnightsUser(7, "chief");
        chief.UserData!.Knights = 7;
        chief.UserData.Fame = 1; // CHIEF

        frames.Clear();
        await chief.ParsingAsync([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, GameUser.KnightsWithdraw]);

        Assert.Equal(7, Assert.Single(db.DeleteKnightsCalls));
        Assert.False(world.Knights.ContainsKey(7));
        Assert.Equal(0, chief.UserData.Knights);
        Assert.Equal(0, chief.UserData.Fame);

        // The destroy confirmation reaches the chief.
        Assert.Contains(frames.Select(Unframe),
            p => p[0] == (byte)GameOpcode.WIZ_KNIGHTS_PROCESS && p[1] == GameUser.KnightsDestroy && p[2] == 1);
    }

    [Fact]
    public async Task Destroy_AlsoSendsFailTailQuirk()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, List<byte[]> frames) = MakeUser(world, db, "chief");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1 };
        world.AddKnightsUser(7, "chief");
        chief.UserData!.Knights = 7;
        chief.UserData.Fame = 1;

        frames.Clear();
        await chief.ParsingAsync([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, GameUser.KnightsDestroy]);

        // C++ falls through into fail_return: a [DESTROY][0] tail precedes the
        // [DESTROY][1] success reply.
        List<byte[]> destroys = frames.Select(Unframe)
            .Where(p => p[0] == (byte)GameOpcode.WIZ_KNIGHTS_PROCESS && p[1] == GameUser.KnightsDestroy)
            .ToList();
        Assert.Equal(2, destroys.Count);
        Assert.Equal(0, destroys[0][2]);
        Assert.Equal(1, destroys[1][2]);
    }

    [Fact]
    public async Task ModifyMember_ChiefCannotAdmit_RenumberedFameQuirk()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, List<byte[]> frames) = MakeUser(world, db, "chief");
        (GameUser member, _) = MakeUser(world, db, "member");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1 };
        chief.UserData!.Knights = 7;
        chief.UserData.Fame = 1;  // CHIEF (renumbered to 1)
        member.UserData!.Knights = 7;
        member.UserData.Fame = 5; // TRAINEE

        byte[] name = System.Text.Encoding.Latin1.GetBytes("member");
        var admit = new byte[4 + name.Length];
        admit[0] = (byte)GameOpcode.WIZ_KNIGHTS_PROCESS;
        admit[1] = GameUser.KnightsAdmit;
        BinaryPrimitives.WriteInt16LittleEndian(admit.AsSpan(2), (short)name.Length);
        name.CopyTo(admit.AsSpan(4));

        frames.Clear();
        await chief.ParsingAsync(admit);

        // Upstream renumbered CHIEF to 1, so "fame >= OFFICER(4)" rejects the chief.
        Assert.Empty(db.UpdateKnightsCalls);
        byte[] reply = Unframe(frames.Single());
        Assert.Equal(GameUser.KnightsAdmit, reply[1]);
        Assert.Equal(0, reply[2]);
    }

    [Fact]
    public async Task ModifyMember_ChiefPromotesViceChief()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, _) = MakeUser(world, db, "chief");
        (GameUser member, List<byte[]> memberFrames) = MakeUser(world, db, "member");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1 };
        world.AddKnightsUser(7, "chief");
        world.AddKnightsUser(7, "member");
        chief.UserData!.Knights = 7;
        chief.UserData.Fame = 1;
        member.UserData!.Knights = 7;
        member.UserData.Fame = 3; // KNIGHT

        byte[] name = System.Text.Encoding.Latin1.GetBytes("member");
        var promote = new byte[4 + name.Length];
        promote[0] = (byte)GameOpcode.WIZ_KNIGHTS_PROCESS;
        promote[1] = GameUser.KnightsViceChief;
        BinaryPrimitives.WriteInt16LittleEndian(promote.AsSpan(2), (short)name.Length);
        name.CopyTo(promote.AsSpan(4));

        memberFrames.Clear();
        await chief.ParsingAsync(promote);

        (int type, string charId, _) = Assert.Single(db.UpdateKnightsCalls);
        Assert.Equal(0x1A, type); // Aujard KNIGHTS_VICECHIEF
        Assert.Equal("member", charId);
        Assert.Equal(2, member.UserData.Fame); // VICECHIEF

        // The target gets the MODIFY_FAME packet directly.
        byte[] fame = memberFrames.Select(Unframe)
            .First(p => p[0] == (byte)GameOpcode.WIZ_KNIGHTS_PROCESS && p[1] == GameUser.KnightsModifyFame);
        Assert.Equal(1, fame[2]);
        Assert.Equal(member.SocketId, BinaryPrimitives.ReadInt16LittleEndian(fame.AsSpan(3)));
        Assert.Equal(2, fame[7]); // new fame
    }

    [Fact]
    public async Task AllList_PagesNationKnights()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "viewer");

        world.Knights[1] = new KnightsClan
        {
            Index = 1, Name = "Alpha", Nation = 1, Flag = KnightsClan.KnightsType,
            Members = 5, Chief = "boss", Points = 240,
        };
        world.Knights[2] = new KnightsClan
        {
            Index = 2, Name = "ClanOnly", Nation = 1, Flag = KnightsClan.ClanType, // filtered out
        };
        world.Knights[15001] = new KnightsClan
        {
            Index = 15001, Name = "Enemy", Nation = 2, Flag = KnightsClan.KnightsType, // wrong nation
        };

        frames.Clear();
        await user.ParsingAsync([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, GameUser.KnightsAllListReq, 0, 0]);

        byte[] reply = Unframe(frames.Single());
        Assert.Equal(GameUser.KnightsAllListReq, reply[1]);
        Assert.Equal(1, reply[2]);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(3))); // page
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(5))); // one entry

        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(7))); // clan index
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(9));
        Assert.Equal("Alpha", System.Text.Encoding.Latin1.GetString(reply, 11, nameLen));
    }

    [Fact]
    public async Task MemberList_ChiefGetsDuplicatedSweepQuirk()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser chief, List<byte[]> frames) = MakeUser(world, db, "chief");

        world.Knights[7] = new KnightsClan { Index = 7, Name = "Clan", Nation = 1, Members = 1 };
        world.AddKnightsUser(7, "chief");
        chief.UserData!.Knights = 7;
        chief.UserData.Fame = 1; // CHIEF

        frames.Clear();
        await chief.ParsingAsync([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, GameUser.KnightsMemberReq]);

        byte[] reply = Unframe(frames.Single());
        Assert.Equal(GameUser.KnightsMemberReq, reply[1]);
        Assert.Equal(1, reply[2]);
        short onlineCount = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(5));
        short count = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(9));

        // C++ quirk: the chief's reply contains the online sweep AND the full
        // member sweep appended — the same single member counted both times.
        Assert.Equal(1, onlineCount);
        Assert.Equal(1, count);

        // Entry blob holds two copies of "chief" ([str2][fame][level][class][online]).
        int blobStart = 11;
        short len1 = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(blobStart));
        Assert.Equal("chief", System.Text.Encoding.Latin1.GetString(reply, blobStart + 2, len1));
        int second = blobStart + 2 + len1 + 5;
        short len2 = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(second));
        Assert.Equal("chief", System.Text.Encoding.Latin1.GetString(reply, second + 2, len2));
    }

    [Fact]
    public void GetKnightsGrade_Brackets()
    {
        Assert.Equal(5, EbenezerWorld.GetKnightsGrade(0));
        Assert.Equal(4, EbenezerWorld.GetKnightsGrade(2000 * 24));
        Assert.Equal(3, EbenezerWorld.GetKnightsGrade(5000 * 24));
        Assert.Equal(2, EbenezerWorld.GetKnightsGrade(10000 * 24));
        Assert.Equal(1, EbenezerWorld.GetKnightsGrade(20000 * 24));
    }

    [Fact]
    public async Task UserInfo_CarriesClanBlock()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser member, _) = MakeUser(world, db, "member");
        (GameUser observer, List<byte[]> observerFrames) = MakeUser(world, db, "observer");

        world.Knights[7] = new KnightsClan
        {
            Index = 7, Name = "Clan", Nation = 1, Grade = 3, Ranking = 2,
            MarkVersion = 9, Cape = 4, AllianceKnights = 0,
        };
        member.UserData!.Knights = 7;
        member.UserData.Fame = 3;

        // Observer requests the member's info.
        var packet = new byte[5];
        packet[0] = (byte)GameOpcode.WIZ_REQ_USERIN;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1); // one uid
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3), member.SocketId);
        observerFrames.Clear();
        await observer.ParsingAsync(packet);

        byte[] reply = Unframe(observerFrames.Single());
        // [WIZ_REQ_USERIN][count u16][uid][charId str1][nation][knights i16][fame]...
        int offset = 3 + 2;
        byte idLen = reply[offset];
        offset += 1 + idLen + 1; // charId + nation
        Assert.Equal(7, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset)));
        offset += 2;
        Assert.Equal(3, reply[offset]); // fame
        offset += 1;
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset))); // alliance
        offset += 2;
        byte clanNameLen = reply[offset];
        Assert.Equal("Clan", System.Text.Encoding.Latin1.GetString(reply, offset + 1, clanNameLen));
        offset += 1 + clanNameLen;
        Assert.Equal(3, reply[offset]);     // grade
        Assert.Equal(2, reply[offset + 1]); // ranking
        Assert.Equal(9, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset + 2))); // mark version
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(offset + 4))); // cape
    }
}
