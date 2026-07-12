using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the battle-event slice (stage 4.14): the war state machine, the
/// AG_BATTLE_EVENT handler and the GM commands.
/// </summary>
public class EbenezerBattleTests
{
    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 1, mapSize: 480f) { Type = 1 });
        world.Rand = Math.Min;
        world.ServerResources[105] = "Lunar Gate is now open. The War has begun.";
        world.ServerResources[126] = "#### NOTICE : %s ####";
        world.ServerResources[133] = "Lunar Gate is closing.";
        world.ServerResources[135] = "The user that killed the enemy captain is [%s][%s].";
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
        EbenezerWorld world, FakeDbAgent db, string charId, byte authority = 1)
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
        data.Level = 30;
        data.Str = 70;
        data.Sta = 60;
        data.Dex = 50;
        data.Intel = 50;
        data.Cha = 50;
        data.Hp = 100;
        data.Mp = 100;
        data.Authority = authority;
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
    public void BattleZoneOpen_SetsStateAndAnnounces()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db, "watcher");

        var aiSent = new List<byte[]>();
        world.SendToAiServer = (_, buf) => aiSent.Add(buf);

        frames.Clear();
        world.BattleZoneOpen(EbenezerWorld.BattlezoneOpen);

        Assert.Equal(1, world.BattleOpen);    // NATION_BATTLE
        Assert.Equal(1, world.OldBattleOpen);

        byte[] chat = frames.Select(Unframe).First(p => p[0] == (byte)GameOpcode.WIZ_CHAT);
        Assert.Equal(8, chat[1]); // chat_type default (WAR_SYSTEM_CHAT)

        byte[] ai = Assert.Single(aiSent);
        Assert.Equal(65, ai[0]); // AG_BATTLE_EVENT
        Assert.Equal(1, ai[1]);  // BATTLE_EVENT_OPEN
        Assert.Equal(0, ai[2]);  // BATTLEZONE_OPEN
    }

    [Fact]
    public void BattleZoneOpenTimer_CountdownRunsAllStages()
    {
        EbenezerWorld world = MakeWorld();
        world.SendToAiServer = (_, _) => { };
        world.BattleOpen = 1;
        world.OldBattleOpen = 1;
        world.Victory = 1; // Karus already declared by AG_BATTLE_EVENT
        world.KarusDead = 5;
        world.ElmoradDead = 9;
        world.BanishFlag = 1;

        // Stage 0: close + flag reset.
        world.BattleZoneOpenTimer();
        Assert.Equal(0, world.BattleOpen);
        Assert.Equal(1, world.BanishDelay);

        // Run the countdown to the reset stage.
        for (int i = 0; i < 19; i++)
            world.BattleZoneOpenTimer();

        Assert.Equal(0, world.BanishFlag);
        Assert.Equal(0, world.BanishDelay);
        Assert.Equal(0, world.Victory);
        Assert.Equal(0, world.KarusDead);
        Assert.Equal(0, world.ElmoradDead);
        Assert.Equal(0, world.OldBattleOpen);
    }

    [Fact]
    public void RecvBattleEvent_MapResult_OpensInvasionFlag()
    {
        EbenezerWorld world = MakeWorld();
        world.BattleOpen = 1;

        var link = new AiLink(0, world, NullLogger.Instance);
        link.Parsing([65, 2, 1]); // AG_BATTLE_EVENT, BATTLE_MAP_EVENT_RESULT, KARUS

        Assert.Equal(1, world.KarusOpenFlag);
        Assert.Equal(0, world.ElmoradOpenFlag);
    }

    [Fact]
    public void RecvBattleEvent_Result_SavesVictoryAndStartsBanish()
    {
        EbenezerWorld world = MakeWorld();
        world.BattleOpen = 1;

        (string CharId, byte Nation)? saved = null;
        world.SaveBattleResult = (charId, nation) => saved = (charId, nation);

        var link = new AiLink(0, world, NullLogger.Instance);
        byte[] name = System.Text.Encoding.Latin1.GetBytes("warhero");
        var packet = new byte[4 + name.Length];
        packet[0] = 65; // AG_BATTLE_EVENT
        packet[1] = 3;  // BATTLE_EVENT_RESULT
        packet[2] = 2;  // ELMORAD wins
        packet[3] = (byte)name.Length;
        name.CopyTo(packet.AsSpan(4));
        link.Parsing(packet);

        Assert.Equal(("warhero", (byte)2), saved);
        Assert.Equal(2, world.Victory);
        Assert.Equal(2, world.OldVictory);
        Assert.Equal(1, world.BanishFlag);
        Assert.Equal(1, world.BattleSave);
    }

    [Fact]
    public void RecvBattleEvent_MaxUser_AnnouncesKill()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser hero, List<byte[]> frames) = MakeUser(world, db, "hero");
        world.Knights[7] = new KnightsClan { Index = 7, Name = "WarClan", Nation = 1 };
        hero.UserData!.Knights = 7;

        var link = new AiLink(0, world, NullLogger.Instance);
        byte[] name = System.Text.Encoding.Latin1.GetBytes("hero");
        var packet = new byte[4 + name.Length];
        packet[0] = 65;
        packet[1] = 4; // BATTLE_EVENT_MAX_USER
        packet[2] = 1; // captain kill
        packet[3] = (byte)name.Length;
        name.CopyTo(packet.AsSpan(4));

        frames.Clear();
        link.Parsing(packet);

        // Both the war chat and the public chat carry the formatted line.
        List<byte[]> chats = frames.Select(Unframe).Where(p => p[0] == (byte)GameOpcode.WIZ_CHAT).ToList();
        Assert.Equal(2, chats.Count);
        Assert.Equal(8, chats[0][1]); // WAR_SYSTEM_CHAT
        Assert.Equal(1, chats[1][1]); // PUBLIC_CHAT

        string text = System.Text.Encoding.Latin1.GetString(chats[0].AsSpan(7 + 0));
        Assert.Contains("WarClan", text);
        Assert.Contains("hero", text);
    }

    [Fact]
    public async Task OperationMessage_GmCommands()
    {
        EbenezerWorld world = MakeWorld();
        world.SendToAiServer = (_, _) => { };
        var db = new FakeDbAgent();
        (GameUser gm, _) = MakeUser(world, db, "gm", authority: 0); // AUTHORITY_MANAGER

        // '+discount' via the chat pipeline.
        byte[] command = System.Text.Encoding.Latin1.GetBytes("+discount");
        var chat = new byte[4 + command.Length];
        chat[0] = (byte)GameOpcode.WIZ_CHAT;
        chat[1] = 1; // GENERAL_CHAT
        BinaryPrimitives.WriteInt16LittleEndian(chat.AsSpan(2), (short)command.Length);
        command.CopyTo(chat.AsSpan(4));
        await gm.ParsingAsync(chat);
        Assert.Equal(1, world.Discount);

        Assert.True(gm.OperationMessage("+alldiscount"));
        Assert.Equal(2, world.Discount);

        Assert.True(gm.OperationMessage("+santa"));
        Assert.Equal(1, world.Santa);

        Assert.True(gm.OperationMessage("+close"));
        Assert.Equal(1, world.BanishFlag);

        Assert.False(gm.OperationMessage("+unknowncommand"));
    }

    [Fact]
    public async Task OperatorCommand_ChatForbidAndPermit()
    {
        EbenezerWorld world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser gm, _) = MakeUser(world, db, "gm", authority: 0);
        (GameUser target, _) = MakeUser(world, db, "victim");

        byte[] name = System.Text.Encoding.Latin1.GetBytes("victim");
        var packet = new byte[5 + name.Length];
        packet[0] = (byte)GameOpcode.WIZ_OPERATOR;
        packet[1] = 3; // OPERATOR_CHAT_FORBID
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2), (short)name.Length);
        name.CopyTo(packet.AsSpan(4));
        await gm.ParsingAsync(packet[..(4 + name.Length)]);

        Assert.Equal(11, target.UserData!.Authority); // AUTHORITY_NOCHAT

        packet[1] = 4; // OPERATOR_CHAT_PERMIT
        await gm.ParsingAsync(packet[..(4 + name.Length)]);
        Assert.Equal(1, target.UserData.Authority); // AUTHORITY_USER
    }

    [Fact]
    public void UpdateGameTime_RollsMinutesAndSendsAiTimeWeather()
    {
        EbenezerWorld world = MakeWorld();
        var aiSent = new List<byte[]>();
        world.SendToAiServer = (_, buf) => aiSent.Add(buf);
        world.Minute = 58;

        world.UpdateGameTime();
        Assert.Equal(59, world.Minute);

        world.UpdateGameTime(); // 59 → 60 → hour roll
        Assert.Equal(0, world.Minute);
        Assert.Equal(2, world.Hour);

        byte[] timeWeather = aiSent.Last(b => b[0] == 64); // AG_TIME_WEATHER
        Assert.Equal(world.Year, BinaryPrimitives.ReadInt16LittleEndian(timeWeather.AsSpan(1)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(timeWeather.AsSpan(7))); // hour
    }
}
