using OpenKO.Common;
using OpenKO.Net;
using Xunit;

namespace OpenKO.Tests;

public class LoginProtocolTests
{
    [Fact]
    public void ServerListRequestIsJustTheOpcode()
    {
        Packet pkt = LoginProtocol.BuildServerListRequest();
        Assert.Equal((byte)LoginOpcode.ServerList, pkt.Opcode);
        Assert.Equal(1, pkt.Size);
    }

    [Fact]
    public void AccountLoginPacketHasOpcodeAndLengthPrefixedCredentials()
    {
        Packet pkt = LoginProtocol.BuildAccountLogin("hero", "secret");

        // opcode(1) + [len(2) + "hero"(4)] + [len(2) + "secret"(6)] = 15 bytes
        Assert.Equal((byte)LoginOpcode.LoginReq, pkt.Opcode);
        Assert.Equal(1 + 2 + 4 + 2 + 6, pkt.Size);

        // Re-read the payload to confirm the int16 length-prefixed layout.
        pkt.SyncForRead();
        Assert.Equal((byte)LoginOpcode.LoginReq, pkt.Read<byte>());
        pkt.DByte();
        Assert.True(pkt.ReadString(out string account));
        Assert.True(pkt.ReadString(out string password));
        Assert.Equal("hero", account);
        Assert.Equal("secret", password);
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("acc", "")]
    [InlineData("this_account_name_is_too_long", "pw")]   // >= 20
    [InlineData("acc", "password_too_long")]               // >= 12
    public void AccountLoginRejectsInvalidCredentials(string account, string password)
    {
        Assert.Throws<ArgumentException>(() => LoginProtocol.BuildAccountLogin(account, password));
    }

    [Fact]
    public void ServerListRoundTrips()
    {
        // Build a server-list packet the way the login server would.
        var pkt = new Packet(LoginOpcode.ServerList);
        pkt.DByte();
        pkt.Append((byte)2);          // server count
        pkt.AppendString("127.0.0.1");
        pkt.AppendString("Ronark Land");
        pkt.Append((short)123);
        pkt.AppendString("10.0.0.5");
        pkt.AppendString("Moradon");
        pkt.Append((short)45);

        IReadOnlyList<GameServerInfo> servers = LoginProtocol.ParseServerList(pkt);

        Assert.Equal(2, servers.Count);
        Assert.Equal(new GameServerInfo("127.0.0.1", "Ronark Land", 123), servers[0]);
        Assert.Equal(new GameServerInfo("10.0.0.5", "Moradon", 45), servers[1]);
    }

    [Fact]
    public void ParseLoginResultReadsAuthByte()
    {
        var pkt = new Packet(LoginOpcode.LoginReq);
        pkt.Append((byte)AuthResult.Ok);

        Assert.Equal(AuthResult.Ok, LoginProtocol.ParseLoginResult(pkt));
    }

    [Fact]
    public void ParseNewsReturnsNoticeBody()
    {
        var pkt = new Packet(LoginOpcode.News);
        pkt.DByte();
        pkt.AppendString("Login Notice");
        pkt.Append((ushort)5);
        pkt.Append(System.Text.Encoding.Latin1.GetBytes("hello"));

        Assert.Equal("hello", LoginProtocol.ParseNews(pkt));
    }
}
