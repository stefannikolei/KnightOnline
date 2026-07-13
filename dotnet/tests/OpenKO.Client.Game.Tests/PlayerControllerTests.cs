using System.Numerics;
using OpenKO.Client.Assets;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.9 pins: local player movement + the WIZ_MOVE request.</summary>
public class PlayerControllerTests
{
    private static N3Terrain FlatTerrain(int mapSize, float height)
    {
        var data = new N3MapData[mapSize * mapSize];
        for (int i = 0; i < data.Length; i++)
            data[i] = new N3MapData { Height = height };
        var terrain = new N3Terrain();
        terrain.Initialize(mapSize, data, new byte[mapSize * mapSize]);
        return terrain;
    }

    [Fact]
    public void MoveBy_AdvancesAlongDirectionAndFollowsTerrain()
    {
        N3Terrain terrain = FlatTerrain(16, 5f);
        var pc = new PlayerController { RunSpeed = 10f, Position = new Vector3(20, 0, 20) };

        Assert.True(pc.MoveBy(new Vector3(0, 0, 1), 1f, terrain));
        Assert.Equal(30f, pc.Position.Z, 3);   // moved +10 in Z
        Assert.Equal(5f, pc.Position.Y, 3);     // snapped to terrain height
        Assert.Equal(0f, pc.Facing, 3);         // atan2(0,1) = 0
        Assert.True(pc.IsMoving);
    }

    [Fact]
    public void MoveBy_FacingFromDirection()
    {
        var pc = new PlayerController { RunSpeed = 1f, Position = Vector3.Zero };
        pc.MoveBy(new Vector3(1, 0, 0), 1f, null);
        Assert.Equal(MathF.PI / 2f, pc.Facing, 3); // atan2(1,0)
    }

    [Fact]
    public void MoveBy_NoInput_IsNoMove()
    {
        var pc = new PlayerController { Position = new Vector3(1, 2, 3) };
        Assert.False(pc.MoveBy(Vector3.Zero, 1f, null));
        Assert.False(pc.IsMoving);
        Assert.Equal(new Vector3(1, 2, 3), pc.Position);
    }

    private sealed class CaptureClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }
    }

    [Fact]
    public void SendMove_UpdatesLocalAndBuildsRequest()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        byte flag = (byte)(WorldProtocol.MoveFlagMoving | WorldProtocol.MoveFlagContinuous);
        ctx.InGame.SendMove(650f, 12f, 530f, speed: 4f, flag);

        Assert.Equal(650f, ctx.InGame.World.Local.X, 3);
        var r = new PacketReader(client.Sent[^1]);
        Assert.Equal((byte)GameOpcode.WIZ_MOVE, r.GetByte());
        Assert.Equal(6500, (ushort)r.GetShort()); // x*10
        Assert.Equal(5300, (ushort)r.GetShort()); // z*10
        Assert.Equal(120, r.GetShort());          // y*10
        Assert.Equal(40, (ushort)r.GetShort());   // speed*10
        Assert.Equal(0x03, r.GetByte());          // moveFlag: moving | continuous
    }

    [Fact]
    public void SendRotation_BuildsRotateRequest()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        ctx.InGame.SendRotation(MathF.PI / 2f);

        var r = new PacketReader(client.Sent[^1]);
        Assert.Equal((byte)GameOpcode.WIZ_ROTATE, r.GetByte());
        Assert.Equal((short)(MathF.PI / 2f * 100f), r.GetShort()); // yaw*100
    }

    [Fact]
    public void BuildMove_RoundTripsThroughBroadcastParser()
    {
        // The request has no id; the broadcast parser reads id first, so we frame
        // a broadcast with a known id to confirm the coordinate scaling matches.
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MOVE);
        w.SetShort(7); // id
        w.SetShort(6500);
        w.SetShort(5300);
        w.SetShort(120);
        w.SetShort(40);
        w.SetByte(0);
        MoveUpdate move = WorldProtocol.ParseMove(w.Written);
        Assert.Equal(650f, move.X, 3);
        Assert.Equal(530f, move.Z, 3);
        Assert.Equal(12f, move.Y, 3);
    }
}
