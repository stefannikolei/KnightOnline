using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data.Models;
using OpenKO.GameData.Maps;
using OpenKO.Servers.AIServer;
using OpenKO.Servers.AIServer.Ai;
using Xunit;
using AiNpc = OpenKO.Servers.AIServer.Ai.Npc;

namespace OpenKO.Servers.Tests;

/// <summary>Tests for the AIServerApp port (object-event NPCs, region flags, user wipe).</summary>
public class AiServerAppTests
{
    private static readonly NpcRowBuilder Rows = new();

    private sealed class NpcRowBuilder
    {
        public OpenKO.Data.Models.Npc Make(short id, string name, byte type = 50) => new()
        {
            NpcId = id,
            Name = name,
            PictureId = 1,
            Size = 100,
            Weapon1 = 0,
            Weapon2 = 0,
            Group = 0,
            ActType = 0,
            Type = type,
            Family = 0,
            Rank = 0,
            Title = 0,
            SellingGroup = 0,
            Level = 10,
            Exp = 0,
            Loyalty = 0,
            HitPoints = 1000,
            ManaPoints = 0,
            Attack = 10,
            Armor = 10,
            HitRate = 100,
            EvadeRate = 10,
            Damage = 10,
            AttackDelay = 1000,
            WalkSpeed = 2,
            RunSpeed = 4,
            StandTime = 5,
            Magic1 = 0,
            Magic2 = 0,
            Magic3 = 0,
            FireResist = 0,
            ColdResist = 0,
            LightningResist = 0,
            MagicResist = 0,
            DiseaseResist = 0,
            PoisonResist = 0,
            LightResist = 0,
            Bulk = 100,
            AttackRange = 10,
            SearchRange = 20,
            TracingRange = 30,
            Money = 0,
            Item = 0,
            DirectAttack = 0,
            MagicAttack = 0,
            MoneyType = 0,
        };
    }

    private static AiWorld MakeWorld()
    {
        var world = new AiWorld { Clock = () => 5.0 };
        world.ZoneInfoTable[21] = new ZoneInfo
        {
            ServerId = 1,
            ZoneId = 21,
            Name = "21.smd",
            InitX = 0,
            InitZ = 0,
            InitY = 0,
            Type = 0,
            RoomEvent = 0,
        };
        world.NpcTable[900] = Rows.Make(900, "Castle Gate");

        var map = (GameMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameMap));
        world.Zones.Add(new AiZone
        {
            ServerNo = 1,
            ZoneNumber = 21,
            Map = map,
            Regions = new Region[1, 1] { { new Region() } },
        });

        return world;
    }

    private static AiServerApp MakeApp(AiWorld world, int serverZoneType = 1)
        => new(world, new GameSocketHandlers(world, NullLogger.Instance), serverZoneType, NullLogger.Instance);

    [Fact]
    public void AddObjectEventNpc_CreatesGateNpcWithSpecialObjectDefaults()
    {
        AiWorld world = MakeWorld();
        AiServerApp app = MakeApp(world);

        var ev = new ObjectEvent(0, 900, 1, 0, 1, 512.5f, 10f, 768.5f); // OBJECT_TYPE_GATE

        Assert.True(app.AddObjectEventNpc(ev, 21));

        AiNpc npc = Assert.Single(world.Npcs.Values);
        Assert.Equal(0, npc.Nid);
        Assert.Equal(900, npc.Sid);
        Assert.Equal("Castle Gate", npc.Name);
        Assert.Equal(21, npc.CurZone);
        Assert.Equal(1, npc.GateOpen);
        Assert.Equal(512.5f, npc.CurX);
        Assert.Equal(768.5f, npc.CurZ);
        Assert.Equal(511, npc.InitMinX);
        Assert.Equal(767, npc.InitMinY);
        Assert.Equal(10_000_000, npc.RegenTime);
        Assert.Equal(-1, npc.ZoneIndex);          // never resolved, like the C++
        Assert.Equal(1, npc.ObjectType);          // SPECIAL_OBJECT
        Assert.Equal(4.0f, npc.SecForMeter);
        Assert.True(npc.FirstLive);
        Assert.Equal(1, app.MapEventNpcCount);
        Assert.Equal(1, app.TotalNpcCount);
    }

    [Fact]
    public void AddObjectEventNpc_SkipsZonesOfOtherServers()
    {
        AiWorld world = MakeWorld();
        AiServerApp app = MakeApp(world, serverZoneType: 2); // Elmorad server, zone 21 belongs to server 1

        Assert.False(app.AddObjectEventNpc(new ObjectEvent(0, 900, 1, 0, 0, 1f, 0f, 1f), 21));
        Assert.Empty(world.Npcs);
    }

    [Fact]
    public void GetServerNumber_UnknownZoneIsMinusOne()
    {
        AiServerApp app = MakeApp(MakeWorld());

        Assert.Equal(1, app.GetServerNumber(21));
        Assert.Equal(-1, app.GetServerNumber(99));
    }

    [Fact]
    public void RegionCheck_FlagsRegionsWithUsers()
    {
        AiWorld world = MakeWorld();
        AiServerApp app = MakeApp(world);
        Region region = world.Zones[0].Regions[0, 0];

        region.Users.Add(3);
        app.RegionCheck();
        Assert.Equal(1, region.Moving);

        region.Users.Clear();
        app.RegionCheck();
        Assert.Equal(0, region.Moving);
    }

    [Fact]
    public void DeleteAllUserList_WipesUsersOnceAllSocketsDie()
    {
        AiWorld world = MakeWorld();
        AiServerApp app = MakeApp(world);
        world.Users[5] = new AiUser { Uid = 5 };
        world.Parties[1] = new PartyGroup();
        world.Zones[0].Regions[0, 0].Users.Add(5);

        // Without the first-server flag nothing happens (like the C++).
        app.DeleteAllUserList(9999);
        Assert.NotNull(world.Users[5]);

        app.FirstServerFlag = true;
        app.DeleteAllUserList(9999);

        Assert.Null(world.Users[5]);
        Assert.Empty(world.Parties);
        Assert.Empty(world.Zones[0].Regions[0, 0].Users);
        Assert.False(app.FirstServerFlag);
    }
}
