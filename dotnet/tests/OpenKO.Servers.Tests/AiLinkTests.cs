using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;
using OpenKO.Network.Framing;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the Ebenezer AISocket slice (stage 4.5): the AI_SERVER_CONNECT /
/// SERVER_INFO handshake, the NPC mirror sync and the world broadcasts it
/// triggers, plus the round-robin Send_AIServer.
/// </summary>
public class AiLinkTests
{
    private static EbenezerWorld MakeWorld(params short[] zoneNumbers)
    {
        var world = new EbenezerWorld { ServerNo = 1 };
        if (zoneNumbers.Length == 0)
            zoneNumbers = [21];

        foreach (short zone in zoneNumbers)
            world.Zones.Add(new GameZone(serverNo: 1, zoneNumber: zone, mapSize: 480f)); // 11×11 regions

        return world;
    }

    private static (AiLink Link, List<byte[]> Sent) MakeLink(EbenezerWorld world, int index = 0)
    {
        var sent = new List<byte[]>();
        var link = new AiLink(index, world, NullLogger.Instance)
        {
            Transmit = p =>
            {
                sent.Add(p);
                return true;
            },
        };
        return (link, sent);
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
        user.State = ConnectionState.GameStart;

        world.Zones[0].RegionUserAdd(user.RegionX, user.RegionZ, user.SocketId);
        return (user, frames);
    }

    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    /// <summary>The RecvNpcInfoAll entry layout (AISocket.cpp).</summary>
    private static void WriteNpcInfoAllEntry(ref PacketWriter writer, byte spawnType, short nid,
        float x, float z, string name = "Wolf", short zoneIndex = 0, byte npcType = 0, int hp = 500)
    {
        writer.SetByte(spawnType);
        writer.SetShort(nid);
        writer.SetShort(101);   // npcId (sid)
        writer.SetShort(42);    // pictureId
        writer.SetShort(100);   // size
        writer.SetInt(0);       // weapon 1
        writer.SetInt(0);       // weapon 2
        writer.SetShort(21);    // zone
        writer.SetShort(zoneIndex);
        writer.SetString1(System.Text.Encoding.Latin1.GetBytes(name));
        writer.SetByte(1);      // group
        writer.SetByte(5);      // level
        writer.SetFloat(x);
        writer.SetFloat(z);
        writer.SetFloat(0f);    // y
        writer.SetByte(0);      // direction
        writer.SetByte(npcType);
        writer.SetInt(0);       // selling group
        writer.SetInt(hp);      // max hp
        writer.SetInt(hp);      // hp
        writer.SetByte(1);      // gate open
        writer.SetShort(90);    // hit rate
        writer.SetByte(0);      // object type
        writer.SetByte(0);      // trap number
    }

    /// <summary>The RecvNpcInfo layout (Mode + tState variant).</summary>
    private static byte[] BuildNpcInfoPacket(byte mode, short nid, float x, float z, byte state)
    {
        var buffer = new byte[256];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_INFO);
        writer.SetByte(mode);
        writer.SetShort(nid);
        writer.SetShort(101);
        writer.SetShort(42);
        writer.SetShort(100);
        writer.SetInt(0);
        writer.SetInt(0);
        writer.SetShort(21);
        writer.SetShort(0);
        writer.SetString1("Wolf"u8);
        writer.SetByte(1);
        writer.SetByte(5);
        writer.SetFloat(x);
        writer.SetFloat(z);
        writer.SetFloat(0f);
        writer.SetByte(0);      // direction
        writer.SetByte(state);  // tState
        writer.SetByte(0);      // npc kind
        writer.SetInt(0);
        writer.SetInt(500);
        writer.SetInt(500);
        writer.SetByte(1);
        writer.SetShort(90);
        writer.SetByte(0);
        writer.SetByte(0);
        return buffer[..writer.Index];
    }

    [Fact]
    public void CheckAlive_ResetsErrorCountAndEchoes()
    {
        var world = MakeWorld();
        world.ErrorSocketCount = 5;
        (AiLink link, List<byte[]> sent) = MakeLink(world);

        link.Parsing([AiOpcode.AG_CHECK_ALIVE_REQ]);

        Assert.Equal(0, world.ErrorSocketCount);
        Assert.Equal(new byte[] { AiOpcode.AG_CHECK_ALIVE_REQ }, Assert.Single(sent));
    }

    [Fact]
    public void LoginProcess_TenFirstConnects_TriggersUserInfoDownload()
    {
        var world = MakeWorld();
        var allSent = new List<byte[]>();

        var links = new AiLink[EbenezerWorld.MaxAiSocket];
        for (int i = 0; i < links.Length; i++)
        {
            links[i] = new AiLink(i, world, NullLogger.Instance)
            {
                Transmit = p =>
                {
                    allSent.Add(p);
                    return true;
                },
            };
            world.AiSockets[i] = links[i];
        }

        for (int i = 0; i < 9; i++)
            links[i].Parsing([AiOpcode.AI_SERVER_CONNECT, (byte)i, 0]);

        Assert.False(world.ServerCheckFlag);
        Assert.Equal(9, world.SocketCount);
        Assert.Empty(allSent);

        links[9].Parsing([AiOpcode.AI_SERVER_CONNECT, 9, 0]);

        Assert.True(world.ServerCheckFlag);
        Assert.Equal(0, world.SocketCount);

        // SendAllUserInfo without users: SERVER_INFO START + END.
        byte[][] serverInfo = [.. allSent.Where(p => p[0] == AiOpcode.AG_SERVER_INFO)];
        Assert.Equal(2, serverInfo.Length);
        Assert.Equal(EbenezerWorld.ServerInfoStart, serverInfo[0][1]);
        Assert.Equal(EbenezerWorld.ServerInfoEnd, serverInfo[1][1]);
    }

    [Fact]
    public void ServerInfoEnd_ForAllZones_OpensTheServer()
    {
        var world = MakeWorld(21, 22);
        int accepts = 0;
        world.UserAccept = () => accepts++;
        (AiLink link, _) = MakeLink(world);

        link.Parsing([AiOpcode.AG_SERVER_INFO, EbenezerWorld.ServerInfoStart, 21]);
        Assert.False(world.PointCheckFlag);

        link.Parsing([AiOpcode.AG_SERVER_INFO, EbenezerWorld.ServerInfoEnd, 21, 10, 0]);
        Assert.False(world.PointCheckFlag);
        Assert.Equal(1, world.ZoneCount);

        link.Parsing([AiOpcode.AG_SERVER_INFO, EbenezerWorld.ServerInfoEnd, 22, 10, 0]);
        Assert.True(world.PointCheckFlag);
        Assert.True(world.FirstServerFlag);
        Assert.Equal(0, world.ZoneCount);
        Assert.Equal(1, accepts);

        // A later full re-download does not re-open the accept loop.
        link.Parsing([AiOpcode.AG_SERVER_INFO, EbenezerWorld.ServerInfoEnd, 21, 10, 0]);
        link.Parsing([AiOpcode.AG_SERVER_INFO, EbenezerWorld.ServerInfoEnd, 22, 10, 0]);
        Assert.Equal(1, accepts);
    }

    [Fact]
    public void NpcInfoAll_FillsMirrorAndRegions()
    {
        var world = MakeWorld();
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[512];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.NPC_INFO_ALL);
        writer.SetByte(2); // count
        WriteNpcInfoAllEntry(ref writer, spawnType: 1, nid: 10005, x: 120f, z: 120f);
        WriteNpcInfoAllEntry(ref writer, spawnType: 0, nid: 10006, x: 130f, z: 130f, name: "Bandit");
        link.Parsing(buffer.AsSpan(0, writer.Index));

        GameNpc wolf = world.Npcs[10005];
        Assert.Equal("Wolf", wolf.Name);
        Assert.Equal(42, wolf.Pid);
        Assert.Equal(500, wolf.HP);
        Assert.Equal(21, wolf.CurZone);
        Assert.Equal(2, wolf.RegionX); // 120 / 48
        Assert.Equal(GameNpc.StateLive, wolf.NpcState);
        Assert.Contains(10005, world.Zones[0].Regions[2, 2].Npcs);

        // byType == 0 quirk: mirrored but never added to a region.
        Assert.Equal("Bandit", world.Npcs[10006].Name);
        Assert.DoesNotContain(10006, world.Zones[0].Regions[2, 2].Npcs);
    }

    [Fact]
    public void NpcInfo_Respawn_BroadcastsNpcInDirectly()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (_, List<byte[]> frames) = MakeInGameUser(world, db, 100, 100, "Watcher");
        world.Npcs[10005] = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21 };
        frames.Clear();

        ParseWithFreshLink(world, BuildNpcInfoPacket(mode: 1, nid: 10005, x: 120f, z: 120f, state: 1));

        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(0x0A, payload[0]); // WIZ_NPC_INOUT
        Assert.Equal(GameNpc.NpcIn, payload[1]);
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2)));
        Assert.Contains(10005, world.Zones[0].Regions[2, 2].Npcs);
        Assert.Equal(GameNpc.StateLive, world.Npcs[10005].NpcState);

        // Mode == 0 refreshes the mirror but never broadcasts.
        frames.Clear();
        world.Zones[0].Regions[2, 2].Npcs.Clear();
        ParseWithFreshLink(world, BuildNpcInfoPacket(mode: 0, nid: 10005, x: 120f, z: 120f, state: 1));
        Assert.Empty(frames);
        Assert.Empty(world.Zones[0].Regions[2, 2].Npcs);
    }

    private static void ParseWithFreshLink(EbenezerWorld world, byte[] packet)
    {
        var link = new AiLink(0, world, NullLogger.Instance);
        link.Parsing(packet);
    }

    [Fact]
    public void MoveResult_MovesTheMirrorAndBuffersNpcMove()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser observer, _) = MakeInGameUser(world, db, 100, 100, "Watcher");
        var npc = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2, HP = 500 };
        world.Npcs[10005] = npc;
        world.Zones[0].RegionNpcAdd(2, 2, 10005);
        (AiLink link, List<byte[]> sent) = MakeLink(world);

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.MOVE_RESULT);
        writer.SetByte(1); // INFO_MODIFY
        writer.SetShort(10005);
        writer.SetFloat(150f);
        writer.SetFloat(120f);
        writer.SetFloat(0f);
        writer.SetFloat(4.5f);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(150f, npc.CurX);
        Assert.Equal(3, npc.RegionX); // crossed 2 → 3
        Assert.DoesNotContain(10005, world.Zones[0].Regions[2, 2].Npcs);
        Assert.Contains(10005, world.Zones[0].Regions[3, 2].Npcs);
        Assert.Empty(sent); // alive NPC: no HP re-request

        // WIZ_NPC_MOVE is buffered; the C++ truncates the floats before scaling.
        byte[]? buffered = observer.RegionPacketClear();
        Assert.NotNull(buffered);
        Assert.Equal(0x0B, buffered![5]); // WIZ_NPC_MOVE
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(buffered.AsSpan(6)));
        Assert.Equal(1500, BinaryPrimitives.ReadUInt16LittleEndian(buffered.AsSpan(8))); // (ushort)150 * 10
    }

    [Fact]
    public void MoveResult_DeadMirrorNpc_RequestsHpResync()
    {
        var world = MakeWorld();
        var npc = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2, HP = 0 };
        world.Npcs[10005] = npc;
        (AiLink link, List<byte[]> sent) = MakeLink(world);

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.MOVE_RESULT);
        writer.SetByte(1);
        writer.SetShort(10005);
        writer.SetFloat(120f);
        writer.SetFloat(120f);
        writer.SetFloat(0f);
        writer.SetFloat(4.5f);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        byte[] request = Assert.Single(sent);
        Assert.Equal(AiOpcode.AG_NPC_HP_REQ, request[0]);
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(request.AsSpan(1)));
    }

    [Fact]
    public void NpcDead_RemovesFromRegionAndBuffersWizDead()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser observer, _) = MakeInGameUser(world, db, 100, 100, "Watcher");
        var npc = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2 };
        world.Npcs[10005] = npc;
        world.Zones[0].RegionNpcAdd(2, 2, 10005);
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_DEAD);
        writer.SetShort(10005);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Empty(world.Zones[0].Regions[2, 2].Npcs);
        Assert.Equal(0, npc.RegionX);
        Assert.Equal(0, npc.RegionZ);

        byte[]? buffered = observer.RegionPacketClear();
        Assert.NotNull(buffered);
        Assert.Equal(0x11, buffered![5]); // WIZ_DEAD
        Assert.Equal(10005, BinaryPrimitives.ReadInt16LittleEndian(buffered.AsSpan(6)));
    }

    [Fact]
    public void UserSetHp_UpdatesUsersAndNpcsByBand()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        (GameUser user, _) = MakeInGameUser(world, db, 100, 100, "Hero");
        world.Npcs[10005] = new GameNpc { Nid = 10005, HP = 500, MaxHP = 500 };
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_SET_HP);
        writer.SetShort(user.SocketId);
        writer.SetDWord(55);
        writer.SetDWord(200);
        link.Parsing(buffer.AsSpan(0, writer.Index));
        Assert.Equal(55, user.UserData!.Hp);

        writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_SET_HP);
        writer.SetShort(10005);
        writer.SetDWord(123);
        writer.SetDWord(600);
        link.Parsing(buffer.AsSpan(0, writer.Index));
        Assert.Equal(123, world.Npcs[10005].HP);
        Assert.Equal(600, world.Npcs[10005].MaxHP);
    }

    [Fact]
    public void CompressedData_UnwrapsAndDispatches()
    {
        var world = MakeWorld();
        world.Npcs[10005] = new GameNpc { Nid = 10005, HP = 500 };
        (AiLink link, _) = MakeLink(world);

        var inner = new byte[16];
        var writer = new PacketWriter(inner);
        writer.SetByte(AiOpcode.AG_USER_SET_HP);
        writer.SetShort(10005);
        writer.SetDWord(42);
        writer.SetDWord(500);

        byte[]? body = AgCompressedCodec.Encode(inner.AsSpan(0, writer.Index));
        Assert.NotNull(body);

        byte[] packet = [AiOpcode.AG_COMPRESSED_DATA, .. body!];
        link.Parsing(packet);

        Assert.Equal(42, world.Npcs[10005].HP);
    }

    [Fact]
    public void GateDestroy_OnlyMirrorsTheStatus()
    {
        var world = MakeWorld();
        world.Npcs[10005] = new GameNpc { Nid = 10005, GateOpen = 1 };
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_GATE_DESTORY);
        writer.SetShort(10005);
        writer.SetByte(0);   // destroyed
        writer.SetShort(21);
        writer.SetShort(2);
        writer.SetShort(2);
        link.Parsing(buffer.AsSpan(0, writer.Index));

        Assert.Equal(0, world.Npcs[10005].GateOpen);
    }

    [Fact]
    public void NpcInOut_OutRemovesFromRegion_InReAddsWithPosition()
    {
        var world = MakeWorld();
        var npc = new GameNpc { Nid = 10005, ZoneIndex = 0, CurZone = 21, RegionX = 2, RegionZ = 2 };
        world.Npcs[10005] = npc;
        world.Zones[0].RegionNpcAdd(2, 2, 10005);
        (AiLink link, _) = MakeLink(world);

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_INOUT);
        writer.SetByte(GameNpc.NpcOut);
        writer.SetShort(10005);
        writer.SetFloat(0f);
        writer.SetFloat(0f);
        writer.SetFloat(0f);
        link.Parsing(buffer.AsSpan(0, writer.Index));
        Assert.Empty(world.Zones[0].Regions[2, 2].Npcs);

        writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_INOUT);
        writer.SetByte(GameNpc.NpcIn);
        writer.SetShort(10005);
        writer.SetFloat(130f);
        writer.SetFloat(140f);
        writer.SetFloat(1f);
        link.Parsing(buffer.AsSpan(0, writer.Index));
        Assert.Contains(10005, world.Zones[0].Regions[2, 2].Npcs);
        Assert.Equal(130f, npc.CurX);
        Assert.Equal(140f, npc.CurZ);
    }

    [Fact]
    public void SendAiServer_RoundRobinsAcrossTheLinks()
    {
        var world = MakeWorld();
        (AiLink link0, List<byte[]> sent0) = MakeLink(world, 0);
        (AiLink link1, List<byte[]> sent1) = MakeLink(world, 1);
        world.AiSockets[0] = link0;
        world.AiSockets[1] = link1;

        world.SendAiServer(1000, [0x01]);
        world.SendAiServer(1000, [0x02]);
        // With only two links the third send falls through (the C++ cursor
        // walks past both live sockets) and the cursor wraps back to 0.
        world.SendAiServer(1000, [0x03]);
        world.SendAiServer(1000, [0x04]);

        Assert.Equal(2, sent0.Count);
        Assert.Single(sent1);
        Assert.Equal(new byte[] { 0x01 }, sent0[0]);
        Assert.Equal(new byte[] { 0x02 }, sent1[0]);
        Assert.Equal(new byte[] { 0x04 }, sent0[1]);
    }

    [Fact]
    public void SendAllUserInfo_SendsUserBatchBetweenServerInfoBrackets()
    {
        var world = MakeWorld();
        var db = new FakeDbAgent();
        MakeInGameUser(world, db, 100, 100, "Hero");

        var allSent = new List<byte[]>();
        for (int i = 0; i < EbenezerWorld.MaxAiSocket; i++)
        {
            world.AiSockets[i] = new AiLink(i, world, NullLogger.Instance)
            {
                Transmit = p =>
                {
                    allSent.Add(p);
                    return true;
                },
            };
        }

        world.SendAllUserInfo();

        Assert.Equal(3, allSent.Count);
        Assert.Equal(AiOpcode.AG_SERVER_INFO, allSent[0][0]);
        Assert.Equal(EbenezerWorld.ServerInfoStart, allSent[0][1]);

        // One user < 19: the remainder goes out uncompressed.
        byte[] batch = allSent[1];
        Assert.Equal(AiOpcode.AG_USER_INFO_ALL, batch[0]);
        Assert.Equal(1, batch[1]);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(batch.AsSpan(2)));  // socket id
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(batch.AsSpan(4)));  // "Hero".Length
        Assert.Equal((byte)'H', batch[6]);

        Assert.Equal(AiOpcode.AG_SERVER_INFO, allSent[2][0]);
        Assert.Equal(EbenezerWorld.ServerInfoEnd, allSent[2][1]);
    }
}
