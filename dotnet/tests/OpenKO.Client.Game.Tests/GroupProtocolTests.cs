using System.Text;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.5 pins: party/exchange/warehouse/knights request layouts + routing.</summary>
public class GroupProtocolTests
{
    private sealed class CaptureClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }
    }

    [Fact]
    public void Party_CreateAndControlLayouts()
    {
        var r = new PacketReader(PartyProtocol.BuildCreate("Buddy"));
        Assert.Equal((byte)GameOpcode.WIZ_PARTY, r.GetByte());
        Assert.Equal(PartyProtocol.Create, r.GetByte());
        Assert.Equal("Buddy", Encoding.Latin1.GetString(r.GetVarString(2)));

        Assert.Equal([(byte)GameOpcode.WIZ_PARTY, PartyProtocol.Permit, 1], PartyProtocol.BuildPermit(true));
        Assert.Equal([(byte)GameOpcode.WIZ_PARTY, PartyProtocol.Delete], PartyProtocol.BuildLeave());

        var rm = new PacketReader(PartyProtocol.BuildRemove(99));
        rm.GetByte();
        Assert.Equal(PartyProtocol.Remove, rm.GetByte());
        Assert.Equal((short)99, rm.GetShort());
    }

    [Fact]
    public void Exchange_RequestAndAddLayouts()
    {
        var r = new PacketReader(ExchangeProtocol.BuildRequest(55));
        Assert.Equal((byte)GameOpcode.WIZ_EXCHANGE, r.GetByte());
        Assert.Equal(ExchangeProtocol.Request, r.GetByte());
        Assert.Equal((short)55, r.GetShort());

        var add = new PacketReader(ExchangeProtocol.BuildAdd(3, 379001000, 2));
        add.GetByte();
        Assert.Equal(ExchangeProtocol.Add, add.GetByte());
        Assert.Equal(3, add.GetByte());
        Assert.Equal(379001000u, add.GetDWord());
        Assert.Equal(2u, add.GetDWord());
    }

    [Fact]
    public void Warehouse_OpenAndInputLayouts()
    {
        Assert.Equal([(byte)GameOpcode.WIZ_WAREHOUSE, WarehouseProtocol.Open], WarehouseProtocol.BuildOpen());

        var r = new PacketReader(WarehouseProtocol.BuildInput(379001000, page: 1, srcPos: 20, destPos: 4, count: 7));
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, r.GetByte());
        Assert.Equal(WarehouseProtocol.Input, r.GetByte());
        Assert.Equal(379001000u, r.GetDWord());
        Assert.Equal(1, r.GetByte());
        Assert.Equal(20, r.GetByte());
        Assert.Equal(4, r.GetByte());
        Assert.Equal(7u, r.GetDWord());
    }

    [Fact]
    public void Knights_CreateJoinAndListLayouts()
    {
        var c = new PacketReader(KnightsProtocol.BuildCreate("Templars"));
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, c.GetByte());
        Assert.Equal(KnightsProtocol.Create, c.GetByte());
        Assert.Equal("Templars", Encoding.Latin1.GetString(c.GetVarString(2)));

        var j = new PacketReader(KnightsProtocol.BuildJoin(1234));
        j.GetByte();
        Assert.Equal(KnightsProtocol.Join, j.GetByte());
        Assert.Equal((short)1234, j.GetShort());

        var l = new PacketReader(KnightsProtocol.BuildAllListRequest(2));
        l.GetByte();
        Assert.Equal(KnightsProtocol.AllListReq, l.GetByte());
        Assert.Equal((short)2, l.GetShort());
    }

    [Fact]
    public void InGame_RoutesGroupPacketsToEvents()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive();

        (byte Sub, byte[] Payload)? party = null;
        (byte Sub, byte[] Payload)? knights = null;
        ctx.InGame.PartyReceived = (sub, p) => party = (sub, p);
        ctx.InGame.KnightsReceived = (sub, p) => knights = (sub, p);

        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_PARTY, PartyProtocol.Create, 1, 2]);
        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, KnightsProtocol.MemberReq, 0]);

        Assert.Equal(PartyProtocol.Create, party!.Value.Sub);
        Assert.Equal(KnightsProtocol.MemberReq, knights!.Value.Sub);

        // The send helper queues a pre-built group packet.
        ctx.InGame.SendRaw(WarehouseProtocol.BuildOpen());
        Assert.Equal((byte)GameOpcode.WIZ_WAREHOUSE, client.Sent[^1][0]);
    }
}
