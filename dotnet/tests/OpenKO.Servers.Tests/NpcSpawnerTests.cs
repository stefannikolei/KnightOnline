using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data.Models;
using OpenKO.GameData.Maps;
using OpenKO.Servers.AIServer.Ai;
using Xunit;
using NpcRow = OpenKO.Data.Models.Npc;

namespace OpenKO.Servers.Tests;

public class NpcSpawnerTests
{
    private static NpcRow MakeNpcRow(int id, string name) => new()
    {
        NpcId = (short)id,
        Name = name,
        PictureId = 1,
        Size = 100,
        Weapon1 = 0,
        Weapon2 = 0,
        Group = 0,
        ActType = 0,
        Type = 0,
        Family = 0,
        Rank = 0,
        Title = 0,
        SellingGroup = 0,
        Level = 10,
        Exp = 100,
        Loyalty = 5,
        HitPoints = 500,
        ManaPoints = 50,
        Attack = 10,
        Armor = 20,
        HitRate = 100,
        EvadeRate = 10,
        Damage = 30,
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
        Money = 100,
        Item = 0,
        DirectAttack = 0,
        MagicAttack = 0,
        MoneyType = 0,
    };

    private static AiWorld MakeWorld()
    {
        var world = new AiWorld { Rand = (min, _) => min }; // deterministic
        world.MonsterTable[7] = MakeNpcRow(7, "Wolf");
        world.NpcTable[9] = MakeNpcRow(9, "Merchant");

        // Minimal zone 21 (map content irrelevant for spawning).
        var map = (GameMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameMap));
        world.Zones.Add(new AiZone
        {
            ServerNo = 1,
            ZoneNumber = 21,
            Map = map,
            Regions = new Region[1, 1],
        });

        return world;
    }

    private static NpcPos MakePosRow(int npcId, byte actType, byte numNpc, byte pathPoints = 0, string path = "") => new()
    {
        ZoneId = 21,
        NpcId = npcId,
        ActType = actType,
        RegenType = 0,
        DungeonFamily = 0,
        SpecialType = 0,
        TrapNumber = 0,
        LeftX = 100,
        TopZ = 200,
        RightX = 110,
        BottomZ = 210,
        LimitMinZ = 0,
        LimitMinX = 0,
        LimitMaxX = 0,
        LimitMaxZ = 0,
        NumNpc = numNpc,
        RespawnTime = 30,
        Direction = 1,
        PathPointCount = pathPoints,
        Path = path,
    };

    [Fact]
    public void ExpandsNumNpcInstances_MonsterByActTypeBelow100()
    {
        var world = MakeWorld();
        var spawner = new NpcSpawner(world, NullLogger.Instance);

        bool ok = spawner.SpawnAll([MakePosRow(7, actType: 1, numNpc: 3)], serverZoneType: 0, _ => 1);

        Assert.True(ok);
        Assert.Equal(3, world.Npcs.Count);
        Assert.All(world.Npcs.Values, npc =>
        {
            Assert.Equal("Wolf", npc.Name);
            Assert.Equal(7, npc.Sid);
            Assert.Equal(1, npc.MoveType);
            Assert.Equal(21, npc.CurZone);
            Assert.Equal(0, npc.ZoneIndex);
            Assert.Equal(100f, npc.CurX); // deterministic Rand → LeftX
            Assert.Equal(30_000, npc.RegenTime);
            // Monster speeds scaled by 1500/1000.
            Assert.Equal(3f, npc.Speed1);
            Assert.Equal(6f, npc.Speed2);
        });
        Assert.Equal([0, 1, 2], world.Npcs.Keys.Order().ToArray());
    }

    [Fact]
    public void ActTypeAbove100ResolvesNpcTableAndShiftsMoveType()
    {
        var world = MakeWorld();
        var spawner = new NpcSpawner(world, NullLogger.Instance);

        bool ok = spawner.SpawnAll([MakePosRow(9, actType: 100, numNpc: 1)], serverZoneType: 0, _ => 1);

        Assert.True(ok);
        var npc = world.Npcs.Values.Single();
        Assert.Equal("Merchant", npc.Name);
        Assert.Equal(0, npc.MoveType);       // 100 - 100
        Assert.Equal(100, npc.InitMoveType); // untouched, like the C++
    }

    [Fact]
    public void ParsesPathBlob()
    {
        var world = MakeWorld();
        var spawner = new NpcSpawner(world, NullLogger.Instance);

        // Two points: (12, 34) and (567, 890) as %04d%04d pairs.
        bool ok = spawner.SpawnAll(
            [MakePosRow(7, actType: 2, numNpc: 1, pathPoints: 2, path: "0012003405670890")],
            serverZoneType: 0, _ => 1);

        Assert.True(ok);
        var npc = world.Npcs.Values.Single();
        Assert.Equal(12, npc.PathList[0].X);
        Assert.Equal(34, npc.PathList[0].Z);
        Assert.Equal(567, npc.PathList[1].X);
        Assert.Equal(890, npc.PathList[1].Z);
        Assert.Equal(2, npc.MaxPathCount);
    }

    [Fact]
    public void PathMovingNpcWithoutPathIsFatal()
    {
        var world = MakeWorld();
        var spawner = new NpcSpawner(world, NullLogger.Instance);

        Assert.False(spawner.SpawnAll([MakePosRow(7, actType: 2, numNpc: 1)], serverZoneType: 0, _ => 1));
    }

    [Fact]
    public void SkipsRowsForOtherServerZones()
    {
        var world = MakeWorld();
        var spawner = new NpcSpawner(world, NullLogger.Instance);

        bool ok = spawner.SpawnAll([MakePosRow(7, actType: 1, numNpc: 2)], serverZoneType: 2, _ => 1);

        Assert.True(ok);
        Assert.Empty(world.Npcs);
    }
}
