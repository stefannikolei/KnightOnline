using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>Tests for the party/market BBS slice (stage 4.16).</summary>
public class GameUserBbsTests
{
    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f) { Type = 1 });
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

    private static (GameUser User, List<byte[]> Frames) MakeUser(EbenezerWorld world, FakeDbAgent db, string charId)
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
        data.Gold = 100_000;
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

    [Fact]
    public async Task PartyBbs_RegisterListsSeeker()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser seeker, List<byte[]> frames) = MakeUser(world, db, "seeker");

        frames.Clear();
        await seeker.ParsingAsync([(byte)GameOpcode.WIZ_PARTY_BBS, GameUser.PartyBbsRegister]);

        Assert.Equal(2, seeker.NeedParty);

        byte[] reply = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_PARTY_BBS);
        Assert.Equal(GameUser.PartyBbsRegister, reply[1]);
        Assert.Equal(1, reply[2]);

        // First row is the seeker itself; the tail holds the counter.
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(3));
        Assert.Equal("seeker", System.Text.Encoding.Latin1.GetString(reply, 5, nameLen));
        short totalCount = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(reply.Length - 2));
        Assert.Equal(1, totalCount);

        // A duplicate registration fails.
        frames.Clear();
        await seeker.ParsingAsync([(byte)GameOpcode.WIZ_PARTY_BBS, GameUser.PartyBbsRegister]);
        byte[] fail = Unframe(frames.Single());
        Assert.Equal(0, fail[2]);

        // Delete restores the state.
        frames.Clear();
        await seeker.ParsingAsync([(byte)GameOpcode.WIZ_PARTY_BBS, GameUser.PartyBbsDelete]);
        Assert.Equal(1, seeker.NeedParty);
    }

    [Fact]
    public async Task MarketBbs_RegisterChargesGoldAndReports()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser trader, List<byte[]> frames) = MakeUser(world, db, "trader");

        byte[] title = System.Text.Encoding.Latin1.GetBytes("Selling sword");
        byte[] message = System.Text.Encoding.Latin1.GetBytes("cheap +8");
        var packet = new byte[16 + title.Length + message.Length];
        var writer = new OpenKO.Network.PacketWriter(packet);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(GameUser.MarketBbsRegister);
        writer.SetByte(GameUser.MarketBbsSell);
        writer.SetString2(title);
        writer.SetString2(message);
        writer.SetDWord(75000); // asking price

        frames.Clear();
        await trader.ParsingAsync(packet[..writer.Index]);

        Assert.Equal(trader.SocketId, world.MarketSell.PosterId[0]);
        Assert.Equal("Selling sword", world.MarketSell.Title[0]);
        Assert.Equal(75000, world.MarketSell.Price[0]);
        Assert.Equal(99_000, trader.UserData!.Gold); // SELL_POST_PRICE

        byte[] gold = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_GOLD_CHANGE);
        Assert.Equal(2, gold[1]); // lose
        Assert.Equal(1000u, BinaryPrimitives.ReadUInt32LittleEndian(gold.AsSpan(2)));

        byte[] report = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_MARKET_BBS);
        Assert.Equal(GameUser.MarketBbsRegister, report[1]);
        Assert.Equal(GameUser.MarketBbsSell, report[2]);
        Assert.Equal(1, report[3]);
        short posterId = BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(4));
        Assert.Equal(trader.SocketId, posterId);
    }

    [Fact]
    public async Task MarketBbs_DeleteByOtherUserRejected()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser owner, _) = MakeUser(world, db, "owner");
        (GameUser thief, List<byte[]> thiefFrames) = MakeUser(world, db, "thief");

        world.MarketBuy.PosterId[0] = owner.SocketId;
        world.MarketBuy.Title[0] = "buying";

        var packet = new byte[5];
        packet[0] = (byte)GameOpcode.WIZ_MARKET_BBS;
        packet[1] = GameUser.MarketBbsDelete;
        packet[2] = GameUser.MarketBbsBuy;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3), 0);

        thiefFrames.Clear();
        await thief.ParsingAsync(packet);

        Assert.Equal(owner.SocketId, world.MarketBuy.PosterId[0]); // untouched
        byte[] fail = Unframe(thiefFrames.Single());
        Assert.Equal(GameUser.MarketBbsDelete, fail[1]);
        Assert.Equal(0, fail[3]); // result
    }

    [Fact]
    public void MarketBbs_CompactShiftsPostsLeft()
    {
        var board = new MarketBbsBoard();
        board.PosterId[0] = -1;
        board.PosterId[1] = 7;
        board.Title[1] = "second";
        board.PosterId[3] = 9;
        board.Title[3] = "fourth";

        board.Compact();

        Assert.Equal(7, board.PosterId[0]);
        Assert.Equal("second", board.Title[0]);
        Assert.Equal(9, board.PosterId[1]);
        Assert.Equal("fourth", board.Title[1]);
        Assert.Equal(-1, board.PosterId[3]);
    }

    [Fact]
    public void MarketBbs_UserDeleteDropsOwnPosts()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser trader, _) = MakeUser(world, db, "trader");

        world.MarketBuy.PosterId[2] = trader.SocketId;
        world.MarketSell.PosterId[5] = trader.SocketId;
        world.MarketSell.PosterId[6] = 999;

        trader.MarketBbsUserDelete();

        Assert.Equal(-1, world.MarketBuy.PosterId[2]);
        Assert.Equal(-1, world.MarketSell.PosterId[5]);
        Assert.Equal(999, world.MarketSell.PosterId[6]);
    }
}
