using OpenKO.Core.Protocol;
using OpenKO.GameData.Maps;
using OpenKO.Servers.AIServer.Ai;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the CRoomEvent / MAP room-event port: .evt parsing, room activation,
/// the reset cycle and the (faithfully kept) CheckMonsterCount no-op quirk.
/// </summary>
public class RoomEventTests
{
    private static AiZone MakeZone()
    {
        var map = (GameMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameMap));
        return new AiZone
        {
            ServerNo = 1,
            ZoneNumber = 101,
            Map = map,
            Regions = new Region[1, 1] { { new Region() } },
        };
    }

    private static readonly string[] SampleEvt =
    [
        "; battle event rooms",
        "ROOM 1",
        "TYPE 1",
        "NATION 1",
        "POS 100 200 110 210",
        "POSEND 300 400 310 410",
        "A 3 2 0",
        "E 100 3 1",
        "END",
        "",
        "ROOM 2",
        "NATION 2",
        "A 1 705 0",
        "E 1 706 0",
        "END",
    ];

    [Fact]
    public void LoadRoomEventLines_ParsesRoomsTypeNationAndTables()
    {
        AiZone zone = MakeZone();

        Assert.True(zone.LoadRoomEventLines(SampleEvt));

        Assert.Equal(2, zone.Rooms.Count);
        Assert.Equal(1, zone.RoomType);
        Assert.Equal(1, zone.KarusRooms);
        Assert.Equal(1, zone.ElmoradRooms);

        RoomEvent room1 = zone.Rooms[1];
        Assert.Equal(101, room1.ZoneNumber);
        Assert.Equal(1, room1.RoomNumber);
        Assert.Equal(1, room1.Status);
        Assert.Equal(1, room1.Check);
        Assert.Equal(3, room1.Logic[0].Number);
        Assert.Equal(2, room1.Logic[0].Option1);
        Assert.Equal(100, room1.Exec[0].Number);
        Assert.Equal(3, room1.Exec[0].Option1);
        Assert.Equal(1, room1.Exec[0].Option2);
        Assert.Equal(100, room1.InitMinX);
        Assert.Equal(210, room1.InitMaxZ);
        Assert.Equal(300, room1.EndMinX);
        Assert.Equal(410, room1.EndMaxZ);

        Assert.Equal(1, zone.Rooms[2].Logic[0].Number);
        Assert.Equal(705, zone.Rooms[2].Logic[0].Option1);
    }

    [Fact]
    public void LoadRoomEventLines_DuplicateRoomFails()
    {
        AiZone zone = MakeZone();

        Assert.False(zone.LoadRoomEventLines(["ROOM 1", "END", "ROOM 1"]));
    }

    [Fact]
    public void LoadRoomEventLines_DirectiveBeforeRoomFails()
    {
        AiZone zone = MakeZone();

        Assert.False(zone.LoadRoomEventLines(["A 1 2 3"]));
    }

    [Fact]
    public void IsRoomCheck_ActivatesInitRoomInsideRect()
    {
        AiZone zone = MakeZone();
        Assert.True(zone.LoadRoomEventLines(SampleEvt));

        // Outside every rect: nothing happens.
        Assert.Equal(0, zone.IsRoomCheck(50f, 50f, () => 7.0));
        Assert.Equal(1, zone.Rooms[1].Status);

        // Inside room 1's init rect: status 1 → 2, DelayTime stamped.
        Assert.Equal(1, zone.IsRoomCheck(105f, 205f, () => 7.0));
        Assert.Equal(2, zone.Rooms[1].Status);
        Assert.Equal(7.0, zone.Rooms[1].DelayTime);
    }

    [Fact]
    public void IsRoomCheck_GoalRoomClearsOnEndRect()
    {
        AiZone zone = MakeZone();
        RoomEvent room = zone.SetRoomEvent(1)!;
        room.Status = 2;
        room.Logic[0].Number = 4; // goal-movement room
        room.EndMinX = 10;
        room.EndMinZ = 10;
        room.EndMaxX = 20;
        room.EndMaxZ = 20;

        Assert.Equal(0, zone.IsRoomCheck(15f, 15f)); // returns 0 but clears
        Assert.Equal(3, room.Status);
    }

    [Fact]
    public void MainRoom_SurvivalRoomClearsAndAnnouncesVictory()
    {
        var world = new AiWorld();
        AiZone zone = MakeZone();
        zone.RoomEventFlag = 1;
        Assert.True(zone.LoadRoomEventLines(SampleEvt));
        world.Zones.Add(zone);

        var outbox = new List<byte[]>();
        RoomEvent room = zone.Rooms[1];
        room.World = world;
        room.SendToZone = outbox.Add;
        room.Status = 2;      // activated
        room.DelayTime = 0.0; // started at t=0

        // Survive 2 minutes (A 3 2 0): not yet at t=60.
        world.TickRoomEvents(60.0);
        Assert.Equal(2, room.Status);
        Assert.Empty(outbox);

        // At t=120 the condition is met → E 100 3 1 → battle-event result for Karus.
        world.TickRoomEvents(120.0);
        Assert.Equal(3, room.Status);

        byte[] sent = Assert.Single(outbox);
        Assert.Equal([AiOpcode.AG_BATTLE_EVENT, 3, 1], sent); // BATTLE_EVENT_RESULT, KARUS_ZONE
    }

    [Fact]
    public void RunEvent_SpawnCaseRevivesRoomNpc()
    {
        var world = new AiWorld();
        AiZone zone = MakeZone();

        // NPC 706 sits in the room, "dead" until the event revives it.
        var npc = new Npc
        {
            Nid = 9,
            Sid = 706,
            World = world,
            MaxHP = 300,
            HP = 0,
            ZoneIndex = 5, // out of range → SetLive exits after the stat reset
        };
        world.Npcs[9] = npc;

        RoomEvent room = zone.SetRoomEvent(1)!;
        room.World = world;
        room.Check = 1;
        room.Exec[0].Number = 1;
        room.Exec[0].Option1 = 706;
        room.RoomNpcs.Add(9);

        Assert.True(room.RunEvent(1)); // Check == logicNumber → room clears
        Assert.Equal(3, npc.ChangeType);
        Assert.Equal(300, npc.HP); // SetLive restored the HP
    }

    [Fact]
    public void CheckMonsterCount_IsDeadCodeLikeTheCpp()
    {
        var world = new AiWorld();
        AiZone zone = MakeZone();

        var npc = new Npc { Nid = 4, Sid = 705, World = world, DeadType = 100, ChangeType = 100 };
        world.Npcs[4] = npc;

        RoomEvent room = zone.SetRoomEvent(1)!;
        room.World = world;
        room.RoomNpcs.Add(4);

        // Quirk: the shadowed nMonster keeps the scan loop from ever running.
        Assert.False(room.CheckMonsterCount(0, 0, 3));   // "all dead" never confirms
        Assert.False(room.CheckMonsterCount(705, 1, 1)); // kill-count never confirms
        room.CheckMonsterCount(0, 0, 4);                 // reset touches nothing
        Assert.Equal(100, npc.ChangeType);
    }

    [Fact]
    public void IsRoomStatusCheck_RunsTheResetCycle()
    {
        AiZone zone = MakeZone();
        RoomEvent room = zone.SetRoomEvent(1)!;
        room.Status = 3; // cleared

        // All rooms cleared → RoomStatus 1 → 2.
        Assert.True(zone.IsRoomStatusCheck());
        Assert.Equal(2, zone.RoomStatus);

        // Nine quiet ticks while InitRoomCount < 10.
        for (int i = 0; i < 9; i++)
            Assert.False(zone.IsRoomStatusCheck());

        // Tenth tick: room reinitialized, RoomStatus 2 → 3.
        Assert.True(zone.IsRoomStatusCheck());
        Assert.Equal(3, zone.RoomStatus);
        Assert.Equal(1, room.Status);

        // Next tick restarts the cycle: RoomStatus 3 → 1.
        Assert.True(zone.IsRoomStatusCheck());
        Assert.Equal(1, zone.RoomStatus);
        Assert.Equal(0, zone.InitRoomCount);
    }

    [Fact]
    public void DurationMagic4_KillsDungeonNpcWhenRoomCleared()
    {
        var world = new AiWorld { Clock = () => 10.0 };
        AiZone zone = MakeZone();
        world.Zones.Add(zone);

        RoomEvent room = zone.SetRoomEvent(1)!;
        room.Status = 3; // cleared

        var npc = new Npc
        {
            Nid = 2,
            World = world,
            ZoneIndex = 0,
            State = NpcState.Standing,
            MaxHP = 100,
            HP = 100,
            DungeonFamily = 1,
            RegenType = 0,
        };

        npc.DurationMagic_4(10.0);

        Assert.Equal(2, npc.RegenType); // never respawns again
        Assert.Equal(NpcState.Dead, npc.State);
        Assert.Equal(0, npc.HP);
    }
}
