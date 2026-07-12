using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.GameData.Maps;
using OpenKO.Network;
using OpenKO.Servers.AIServer;
using OpenKO.Servers.AIServer.Ai;
using Xunit;

namespace OpenKO.Servers.Tests;

public class GameSocketHandlerTests
{
    private static AiWorld MakeWorld()
    {
        // Uninitialized GameMap with a plausible tile count (bounds checks use MapSize).
        var map = (GameMap)RuntimeHelpers.GetUninitializedObject(typeof(GameMap));
        typeof(GameMap).GetProperty(nameof(GameMap.MapSize))!.SetMethod!.Invoke(map, [128]);

        var regions = new Region[12, 12];
        for (int x = 0; x < 12; x++)
        {
            for (int z = 0; z < 12; z++)
                regions[x, z] = new Region();
        }

        var world = new AiWorld();
        world.Zones.Add(new AiZone
        {
            ServerNo = 1,
            ZoneNumber = 21,
            Map = map,
            Regions = regions,
        });

        return world;
    }

    private static GameSocketHandlers MakeHandlers(AiWorld world)
        => new(world, NullLogger.Instance);

    private static byte[] UserInfoBody(short uid, string name)
    {
        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetShort(uid);
        w.SetString2(Encoding.Latin1.GetBytes(name));
        w.SetByte(21);        // bZone
        w.SetShort(0);        // sZoneIndex
        w.SetByte(1);         // bNation
        w.SetByte(60);        // bLevel
        w.SetShort(1200);     // sHp
        w.SetShort(300);      // sMp
        w.SetShort(35);       // sDamage
        w.SetShort(40);       // sAC
        w.SetFloat(1.5f);     // fHitAgi
        w.SetFloat(0.8f);     // fAvoidAgi
        w.SetShort(5);        // sItemAC
        w.SetByte(0);         // bTypeLeft
        w.SetByte(0);         // bTypeRight
        w.SetShort(0);        // sAmountLeft
        w.SetShort(0);        // sAmountRight
        w.SetByte(1);         // bAuthority
        return w.Written.ToArray();
    }

    private static byte[] UserInOutBody(byte type, short uid, string name, float x, float z)
    {
        var buf = new byte[64];
        var w = new PacketWriter(buf);
        w.SetByte(type);
        w.SetShort(uid);
        w.SetString2(Encoding.Latin1.GetBytes(name));
        w.SetFloat(x);
        w.SetFloat(z);
        return w.Written.ToArray();
    }

    private static byte[] UserMoveBody(short uid, float x, float z, short speed)
    {
        var buf = new byte[16];
        var w = new PacketWriter(buf);
        w.SetShort(uid);
        w.SetFloat(x);
        w.SetFloat(z);
        w.SetFloat(0f); // fY (discarded)
        w.SetShort(speed);
        return w.Written.ToArray();
    }

    private static byte[] UserLogOutBody(short uid, string name)
    {
        var buf = new byte[32];
        var w = new PacketWriter(buf);
        w.SetShort(uid);
        w.SetString2(Encoding.Latin1.GetBytes(name));
        return w.Written.ToArray();
    }

    private static byte[] UserInfoAllBody(params (short Uid, string Name, short PartyIndex)[] users)
    {
        var buf = new byte[1024];
        var w = new PacketWriter(buf);
        w.SetByte((byte)users.Length);
        foreach ((short uid, string name, short partyIndex) in users)
        {
            w.SetShort(uid);
            w.SetString2(Encoding.Latin1.GetBytes(name));
            w.SetByte(21);      // bZone
            w.SetShort(0);      // sZoneIndex
            w.SetByte(2);       // bNation
            w.SetByte(45);      // bLevel
            w.SetShort(800);    // sHp
            w.SetShort(400);    // sMp
            w.SetShort(20);     // sDamage
            w.SetShort(30);     // sAC
            w.SetFloat(1.1f);   // fHitAgi
            w.SetFloat(0.9f);   // fAvoidAgi
            w.SetShort(partyIndex);
            w.SetByte(1);       // bAuthority
        }

        return w.Written.ToArray();
    }

    private static byte[] PartyBody(byte subcommand, params short[] shorts)
    {
        var buf = new byte[16];
        var w = new PacketWriter(buf);
        w.SetByte(subcommand);
        foreach (short s in shorts)
            w.SetShort(s);
        return w.Written.ToArray();
    }

    private static byte[] PartyInsertBody(short partyIndex, byte memberIndex, short uid)
    {
        var buf = new byte[16];
        var w = new PacketWriter(buf);
        w.SetByte(0x03); // PARTY_INSERT
        w.SetShort(partyIndex);
        w.SetByte(memberIndex);
        w.SetShort(uid);
        return w.Written.ToArray();
    }

    [Fact]
    public async Task UserInfo_AddsUserToWorld_AndInOutRegistersRegion()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "TestChar"));

        AiUser? user = world.Users[100];
        Assert.NotNull(user);
        Assert.Equal(100, user.Uid);
        Assert.Equal("TestChar", user.UserId);
        Assert.Equal(21, user.CurZone);
        Assert.Equal(60, user.Level);
        Assert.Equal(1200, user.HP);
        Assert.Equal(35, user.HitDamage);
        Assert.Equal(AiUser.UserLive, user.Live);

        // region in at (100, 100) -> region [2, 2] (VIEW_DIST 48)
        await handlers.HandleAsync(null, AiOpcode.AG_USER_INOUT, UserInOutBody(1, 100, "TestChar", 100f, 100f));

        Assert.Equal(100f, user.CurX);
        Assert.Equal(100f, user.WillZ);
        Assert.Equal(2, user.RegionX);
        Assert.Equal(2, user.RegionZ);
        Assert.Contains(100, world.Zones[0].Regions[2, 2].Users);
    }

    [Fact]
    public async Task UserMove_UpdatesPositionAndRegion()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "TestChar"));
        await handlers.HandleAsync(null, AiOpcode.AG_USER_INOUT, UserInOutBody(1, 100, "TestChar", 100f, 100f));

        // (250, 250) -> region [5, 5]
        await handlers.HandleAsync(null, AiOpcode.AG_USER_MOVE, UserMoveBody(100, 250f, 250f, speed: 0));

        AiUser user = world.Users[100]!;
        Assert.Equal(250f, user.CurX);
        Assert.Equal(250f, user.WillX);
        Assert.Equal(250f, user.CurZ);
        Assert.Equal(5, user.RegionX);
        Assert.Equal(5, user.RegionZ);
        Assert.DoesNotContain(100, world.Zones[0].Regions[2, 2].Users);
        Assert.Contains(100, world.Zones[0].Regions[5, 5].Users);
    }

    [Fact]
    public async Task UserMove_WithSpeed_KeepsWillPositionSeparate()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "TestChar"));
        await handlers.HandleAsync(null, AiOpcode.AG_USER_INOUT, UserInOutBody(1, 100, "TestChar", 100f, 100f));
        await handlers.HandleAsync(null, AiOpcode.AG_USER_MOVE, UserMoveBody(100, 150f, 150f, speed: 45));

        AiUser user = world.Users[100]!;
        Assert.Equal(100f, user.CurX);   // cur <- previous will
        Assert.Equal(150f, user.WillX);  // will <- new target
    }

    [Fact]
    public async Task UserLogOut_ClearsSlot()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "TestChar"));
        Assert.NotNull(world.Users[100]);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_LOG_OUT, UserLogOutBody(100, "TestChar"));
        Assert.Null(world.Users[100]);
    }

    [Fact]
    public async Task UserInfoAll_ParsesMultipleUsers()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO_ALL, UserInfoAllBody(
            (200, "Alpha", -1),
            (201, "Bravo", 7)));

        AiUser? alpha = world.Users[200];
        AiUser? bravo = world.Users[201];
        Assert.NotNull(alpha);
        Assert.NotNull(bravo);
        Assert.Equal("Alpha", alpha.UserId);
        Assert.Equal("Bravo", bravo.UserId);
        Assert.Equal(45, alpha.Level);
        Assert.Equal(AiUser.UserLive, alpha.Live);

        Assert.Equal(0, alpha.NowParty);
        Assert.Equal(-1, alpha.PartyNumber);
        Assert.Equal(1, bravo.NowParty);
        Assert.Equal(7, bravo.PartyNumber);
    }

    [Fact]
    public async Task Party_CreateInsertRemoveDelete()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "Leader"));
        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(101, "Member"));

        // PARTY_CREATE(0x01): partyIndex 3, leader 100
        await handlers.HandleAsync(null, AiOpcode.AG_USER_PARTY, PartyBody(0x01, 3, 100));

        PartyGroup party = Assert.Contains((short)3, (IDictionary<short, PartyGroup>)world.Parties);
        Assert.Equal(100, party.Users[0]);
        Assert.Equal(1, world.Users[100]!.NowParty);
        Assert.Equal(3, world.Users[100]!.PartyNumber);

        // PARTY_INSERT(0x03): slot 1 <- 101
        await handlers.HandleAsync(null, AiOpcode.AG_USER_PARTY, PartyInsertBody(3, 1, 101));
        Assert.Equal(101, party.Users[1]);
        Assert.Equal(1, world.Users[101]!.NowParty);
        Assert.Equal(3, world.Users[101]!.PartyNumber);

        // PARTY_REMOVE(0x04): drop 100
        await handlers.HandleAsync(null, AiOpcode.AG_USER_PARTY, PartyBody(0x04, 3, 100));
        Assert.Equal(-1, party.Users[0]);
        Assert.Equal(101, party.Users[1]);
        Assert.Equal(0, world.Users[100]!.NowParty);
        Assert.Equal(-1, world.Users[100]!.PartyNumber);

        // PARTY_DELETE(0x05)
        await handlers.HandleAsync(null, AiOpcode.AG_USER_PARTY, PartyBody(0x05, 3));
        Assert.Empty(world.Parties);
        Assert.Equal(0, world.Users[101]!.NowParty);
        Assert.Equal(-1, world.Users[101]!.PartyNumber);
    }

    [Fact]
    public async Task UserSetHp_ZeroHp_KillsUserAndClearsRegion()
    {
        var world = MakeWorld();
        var handlers = MakeHandlers(world);

        await handlers.HandleAsync(null, AiOpcode.AG_USER_INFO, UserInfoBody(100, "TestChar"));
        await handlers.HandleAsync(null, AiOpcode.AG_USER_INOUT, UserInOutBody(1, 100, "TestChar", 100f, 100f));

        var buf = new byte[8];
        var w = new PacketWriter(buf);
        w.SetShort(100);
        w.SetDWord(0);
        byte[] body = w.Written.ToArray();

        await handlers.HandleAsync(null, AiOpcode.AG_USER_SET_HP, body);

        AiUser user = world.Users[100]!;
        Assert.Equal(AiUser.UserDead, user.Live);
        Assert.Equal(0, user.HP);
        Assert.Equal(-1, user.RegionX);
        Assert.DoesNotContain(100, world.Zones[0].Regions[2, 2].Users);
    }
}
