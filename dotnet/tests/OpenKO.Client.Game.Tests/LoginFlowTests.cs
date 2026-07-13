using System.Text;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-7.2 pins: the login → nation → char-select → in-game state flow, driven
/// by synthetic server replies through a fake client (fully headless).
/// </summary>
public class LoginFlowTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public List<(string Host, int Port)> Connects { get; } = [];

        public ulong? EnabledKey { get; private set; }

        public bool CryptionEnabled => EnabledKey is not null and not 0;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) => Connects.Add((host, port));

        public void EnableCryption(ulong publicKey) => EnabledKey = publicKey;

        public byte LastOpcode => Sent.Count > 0 ? Sent[^1][0] : (byte)0;
    }

    private static byte[] ServerListReply(params (string Ip, string Name, int Users)[] servers)
    {
        var buffer = new byte[512];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)LoginOpcode.LS_SERVERLIST);
        w.SetByte((byte)servers.Length);
        foreach ((string ip, string name, int users) in servers)
        {
            w.SetString2(Encoding.Latin1.GetBytes(ip));
            w.SetString2(Encoding.Latin1.GetBytes(name));
            w.SetShort(users);
        }

        return w.Written.ToArray();
    }

    private static byte[] Reply(GameOpcode opcode, params byte[] body)
        => [(byte)opcode, .. body];

    private static byte[] VersionCheckReply(ulong key)
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_VERSION_CHECK);
        w.SetShort(1298);
        w.SetInt64(unchecked((long)key));
        return w.Written.ToArray();
    }

    private static byte[] AllCharReply(params bool[] occupied)
    {
        var buffer = new byte[1024];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_ALLCHAR_INFO_REQ);
        w.SetByte(1);
        foreach (bool occ in occupied)
        {
            w.SetString2(occ ? Encoding.Latin1.GetBytes("Hero") : []);
            w.SetByte(1);
            w.SetShort(101);
            w.SetByte(60);
            w.SetByte(0);
            w.SetByte(1);
            w.SetByte(21);
            for (int i = 0; i < GameProtocol.VisibleEquipment; i++)
            {
                w.SetDWord(0);
                w.SetShort(0);
            }
        }

        return w.Written.ToArray();
    }

    private static byte[] SelectCharReply()
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_SEL_CHAR);
        w.SetByte(1);
        w.SetByte(21);
        w.SetShort(6500);
        w.SetShort(5300);
        w.SetShort(120);
        w.SetByte(1);
        return w.Written.ToArray();
    }

    [Fact]
    public void FullFlow_NotSelectedNation_GoesThroughNationSelect()
    {
        var client = new FakeGameClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.Login);
        ctx.Machine.TickActive(); // Login.Init → LS_SERVERLIST

        Assert.Equal((byte)LoginOpcode.LS_SERVERLIST, client.Sent[0][0]);

        // Server list arrives.
        ctx.Machine.DispatchPacket(ServerListReply(("127.0.0.1", "Ronark", 5)));
        Assert.Single(ctx.Servers);

        // Account login → success → news request.
        ctx.Login.SubmitAccountLogin("acct", "pw");
        Assert.Equal((byte)LoginOpcode.LS_LOGIN_REQ, client.LastOpcode);
        ctx.Machine.DispatchPacket(Reply((GameOpcode)LoginOpcode.LS_LOGIN_REQ, AccountLoginResult.Ok));
        Assert.Equal((byte)LoginOpcode.LS_NEWS, client.LastOpcode);

        // Connect to the game server → version check.
        ctx.Login.ConnectToGameServer(ctx.Servers[0]);
        Assert.Contains(("127.0.0.1", GameContext.GameServerPort), client.Connects);
        Assert.Equal((byte)GameOpcode.WIZ_VERSION_CHECK, client.LastOpcode);

        // Version check reply keys the cipher and sends WIZ_LOGIN.
        ctx.Machine.DispatchPacket(VersionCheckReply(0x1234567890ABCDEF));
        Assert.True(client.CryptionEnabled);
        Assert.Equal((byte)GameOpcode.WIZ_LOGIN, client.LastOpcode);

        // WIZ_LOGIN nation 0 → nation select.
        ctx.Machine.DispatchPacket(Reply(GameOpcode.WIZ_LOGIN, 0));
        ctx.Machine.TickActive();
        Assert.Equal("NationSelect", ctx.Machine.Active!.Name);

        // Pick a nation → char select.
        ctx.NationSelect.SelectNation(NationSelectState.Karus);
        Assert.Equal((byte)GameOpcode.WIZ_SEL_NATION, client.LastOpcode);
        ctx.Machine.DispatchPacket(Reply(GameOpcode.WIZ_SEL_NATION, NationSelectState.Karus));
        ctx.Machine.TickActive(); // CharSelect.Init → WIZ_ALLCHAR_INFO_REQ
        Assert.Equal("CharSelect", ctx.Machine.Active!.Name);
        Assert.Equal((byte)GameOpcode.WIZ_ALLCHAR_INFO_REQ, client.LastOpcode);

        // Characters arrive, pick slot 0 → WIZ_SEL_CHAR → in game.
        ctx.Machine.DispatchPacket(AllCharReply(true, false, false));
        Assert.Equal(3, ctx.Characters.Count);
        ctx.CharSelect.SelectCharacter(0);
        Assert.Equal((byte)GameOpcode.WIZ_SEL_CHAR, client.LastOpcode);

        SelectCharResult? entered = null;
        ctx.EnteredGame = r => entered = r;
        ctx.Machine.DispatchPacket(SelectCharReply());
        ctx.Machine.TickActive();
        Assert.Equal("InGame", ctx.Machine.Active!.Name);
        Assert.True(ctx.InGame.Entered);
        Assert.Equal((byte)21, ctx.Spawn.Zone);
        Assert.NotNull(entered);
    }

    [Fact]
    public void Login_WithNationAlreadySet_SkipsNationSelect()
    {
        var client = new FakeGameClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.Login);
        ctx.Machine.TickActive();

        // WIZ_LOGIN nation 2 (El Morad) → straight to char select.
        ctx.Machine.DispatchPacket(Reply(GameOpcode.WIZ_LOGIN, 2));
        ctx.Machine.TickActive();
        Assert.Equal("CharSelect", ctx.Machine.Active!.Name);
        Assert.Equal((byte)2, ctx.Nation);
    }

    [Fact]
    public void CharSelect_EmptySlot_GoesToCharCreate()
    {
        var client = new FakeGameClient();
        var ctx = new GameContext(client);
        ctx.Machine.SetActive(ctx.CharSelect);
        ctx.Machine.TickActive();

        ctx.Machine.DispatchPacket(AllCharReply(false, false, false));
        ctx.CharSelect.SelectCharacter(1); // empty
        ctx.Machine.TickActive();
        Assert.Equal("CharCreate", ctx.Machine.Active!.Name);
        Assert.Equal(1, ctx.CharCreate.SlotIndex);

        // A successful create returns to the select screen.
        byte? createResult = null;
        ctx.CharCreate.CreateResult = r => createResult = r;
        ctx.Machine.DispatchPacket(Reply(GameOpcode.WIZ_NEW_CHAR, 0));
        ctx.Machine.TickActive();
        Assert.Equal((byte)0, createResult);
        Assert.Equal("CharSelect", ctx.Machine.Active!.Name);
    }
}
