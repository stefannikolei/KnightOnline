using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data.Models;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer world slice: region packet buffering, in/out
/// broadcasts, movement and the user/NPC list requests.
/// </summary>
public class GameUserWorldTests
{
    private static EbenezerWorld MakeWorld()
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: 21, mapSize: 480f)); // 11×11 regions
        return world;
    }

    private static (GameUser User, List<byte[]> Frames) MakeInGameUser(
        EbenezerWorld world, FakeDbAgent db, float x, float z, string charId)
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
        data.CurX = x;
        data.CurZ = z;
        data.Hp = 100;
        user.UserData = data;
        user.ZoneIndex = 0;
        user.RegionX = (short)(x / GameZone.ViewDistance);
        user.RegionZ = (short)(z / GameZone.ViewDistance);
        user.WillX = x;
        user.WillZ = z;
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
    public void RegionPacketBuffer_AccumulatesAndDrains()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeInGameUser(world, db, 100, 100, "Hero");

        user.RegionPacketAdd([0x06, 0x01]);
        user.RegionPacketAdd([0x09]);

        byte[]? packet = user.RegionPacketClear();
        Assert.NotNull(packet);
        Assert.Equal(0x44, packet![0]); // WIZ_CONTINOUS_PACKET
        Assert.Equal(7, BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(1)));
        Assert.Equal(new byte[] { 2, 0, 0x06, 0x01, 1, 0, 0x09 }, packet[3..]);

        Assert.Null(user.RegionPacketClear()); // drained
    }

    [Fact]
    public void UserInOut_In_BroadcastsInfoToNeighbours()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser mover, List<byte[]> moverFrames) = MakeInGameUser(world, db, 100, 100, "Mover");
        (GameUser observer, List<byte[]> observerFrames) = MakeInGameUser(world, db, 110, 110, "Watcher"); // same region
        moverFrames.Clear();
        observerFrames.Clear();

        // Re-announce the mover: the C++ sends USER_IN + info directly (bDirect default).
        mover.UserInOut(GameUser.UserIn);

        byte[] payload = Unframe(Assert.Single(observerFrames));
        Assert.Equal(0x07, payload[0]); // WIZ_USER_INOUT
        Assert.Equal(0x01, payload[1]); // USER_IN
        Assert.Equal(mover.SocketId, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2)));
        Assert.Equal(5, payload[4]); // name length "Mover"
        Assert.Null(observer.RegionPacketClear());

        // The mover itself is excluded.
        Assert.Empty(moverFrames);
    }

    [Fact]
    public void UserInOut_Out_NotifiesAiServer()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));
        (GameUser user, _) = MakeInGameUser(world, db, 100, 100, "Hero");

        user.UserInOut(GameUser.UserOut);

        Assert.Empty(world.Zones[0].Regions[2, 2].Users);
        (int zone, byte[] data) = Assert.Single(aiPackets);
        Assert.Equal(21, zone);
        Assert.Equal(OpenKO.Core.Protocol.AiOpcode.AG_USER_INOUT, data[0]);
        Assert.Equal(0x02, data[1]); // USER_OUT
    }

    [Fact]
    public async Task MoveProcess_UpdatesPositionAndBroadcasts()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        var aiPackets = new List<(int Zone, byte[] Data)>();
        world.SendToAiServer = (zone, data) => aiPackets.Add((zone, data));

        (GameUser mover, _) = MakeInGameUser(world, db, 100, 100, "Mover");
        (GameUser observer, _) = MakeInGameUser(world, db, 110, 110, "Watcher");

        // Move to (102.4, 100.8) with speed 45: cur ← old will, will ← new.
        byte[] packet = new byte[10];
        packet[0] = 0x06; // WIZ_MOVE
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3), 1008);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(5), 0);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(7), 45);
        packet[9] = 9; // echo

        await mover.ParsingAsync(packet);

        Assert.Equal(100f, mover.UserData!.CurX); // promoted old will
        Assert.Equal(102.4f, mover.WillX);
        Assert.Equal(100.8f, mover.WillZ);

        byte[]? observed = observer.RegionPacketClear();
        Assert.NotNull(observed);
        Assert.Equal(0x06, observed![5]); // WIZ_MOVE
        Assert.Equal(mover.SocketId, BinaryPrimitives.ReadInt16LittleEndian(observed.AsSpan(6)));
        Assert.Equal(1024, BinaryPrimitives.ReadUInt16LittleEndian(observed.AsSpan(8)));

        (int zone, byte[] ai) = Assert.Single(aiPackets);
        Assert.Equal(21, zone);
        Assert.Equal(OpenKO.Core.Protocol.AiOpcode.AG_USER_MOVE, ai[0]);
        Assert.Equal(102.4f, BitConverter.ToSingle(ai, 3));
    }

    [Fact]
    public async Task MoveProcess_RegionCrossing_SendsRegionChangeLists()
    {
        var world = MakeWorld();
        world.PointCheckFlag = true;
        var db = new FakeDbAgent();
        (GameUser mover, List<byte[]> frames) = MakeInGameUser(world, db, 95, 100, "Mover");
        frames.Clear();

        // Cross from region (1,2) to (2,2): x 95 → 97.
        byte[] packet = new byte[10];
        packet[0] = 0x06;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1), 970);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(7), 0); // speed 0 → position jump

        await mover.ParsingAsync(packet);

        Assert.Equal(2, mover.RegionX);
        Assert.Contains(mover.SocketId, world.Zones[0].Regions[2, 2].Users);
        Assert.DoesNotContain(mover.SocketId, world.Zones[0].Regions[1, 2].Users);

        // Border crossing pushes WIZ_NPC_REGION + WIZ_REGIONCHANGE to the mover.
        byte[][] payloads = [.. frames.Select(Unframe)];
        Assert.Contains(payloads, p => p[0] == 0x1C); // WIZ_NPC_REGION
        Assert.Contains(payloads, p => p[0] == 0x15); // WIZ_REGIONCHANGE
    }

    [Fact]
    public async Task RequestUserIn_ReturnsInfosForKnownUsers()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser asker, List<byte[]> frames) = MakeInGameUser(world, db, 100, 100, "Asker");
        (GameUser other, _) = MakeInGameUser(world, db, 200, 200, "Other");
        frames.Clear();

        byte[] packet = new byte[5];
        packet[0] = 0x16; // WIZ_REQ_USERIN
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3), other.SocketId);

        await asker.ParsingAsync(packet);

        byte[] reply = Unframe(Assert.Single(frames));
        Assert.Equal(0x16, reply[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(1)));
        Assert.Equal(other.SocketId, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(3)));
        Assert.Equal(5, reply[5]); // "Other".Length
    }

    [Fact]
    public async Task RequestNpcIn_ReturnsNpcInfo()
    {
        var world = MakeWorld();
        world.PointCheckFlag = true;
        world.Npcs[7] = new GameNpc
        {
            Nid = 7,
            Name = "Wolf",
            Pid = 42,
            CurX = 120,
            CurZ = 120,
            Level = 5,
        };

        var db = new FakeDbAgent();
        (GameUser asker, List<byte[]> frames) = MakeInGameUser(world, db, 100, 100, "Asker");
        frames.Clear();

        byte[] packet = new byte[5];
        packet[0] = 0x1D; // WIZ_REQ_NPCIN
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3), 7);

        await asker.ParsingAsync(packet);

        byte[] reply = Unframe(Assert.Single(frames));
        Assert.Equal(0x1D, reply[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(1)));
        Assert.Equal(7, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(3)));
        Assert.Equal(42, BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(5))); // Pid
    }

    [Fact]
    public async Task RequestNpcIn_GatedByPointCheckFlag()
    {
        var world = MakeWorld();
        world.PointCheckFlag = false;
        var db = new FakeDbAgent();
        (GameUser asker, List<byte[]> frames) = MakeInGameUser(world, db, 100, 100, "Asker");
        frames.Clear();

        byte[] packet = new byte[5];
        packet[0] = 0x1D;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), 1);

        await asker.ParsingAsync(packet);

        Assert.Empty(frames);
    }

    [Fact]
    public void UserInOutForMe_CompressesNeighbourhood()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser me, List<byte[]> frames) = MakeInGameUser(world, db, 100, 100, "Me");
        MakeInGameUser(world, db, 110, 110, "Near");     // same region
        MakeInGameUser(world, db, 130, 100, "NextDoor"); // east region (2→wait 130/48=2, same) — use 150
        MakeInGameUser(world, db, 400, 400, "Far");      // out of the 3×3
        frames.Clear();

        world.UserInOutForMe(me);

        // WIZ_COMPRESS_PACKET envelope around WIZ_REQ_USERIN with 3 users (me + 2 near).
        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x42, payload[0]); // WIZ_COMPRESS_PACKET
    }
}
