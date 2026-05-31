using OpenKO.Common;

namespace OpenKO.Net;

/// <summary>One game server advertised in the login server's server list (port of <c>__GameServerInfo</c>).</summary>
public readonly record struct GameServerInfo(string Ip, string Name, short ConcurrentUsers);

/// <summary>
/// Builders and parsers for the login-server handshake packets, ported from the C++ login procedure
/// (Client/WarFare/GameProcLogIn_1298.cpp). These mirror the original byte layout exactly:
/// a 1-byte command, then int16-length-prefixed strings (the original's
/// <c>MP_AddShort(len) + MP_AddString(bytes)</c> pattern), all little-endian.
/// </summary>
public static class LoginProtocol
{
    /// <summary>Default login-server TCP port (port of <c>SOCKET_PORT_LOGIN</c>).</summary>
    public const int LoginServerPort = 15100;

    /// <summary>Build the "request game-server list" packet (<c>LS_SERVERLIST</c> = 0xF5).</summary>
    public static Packet BuildServerListRequest() => new(LoginOpcode.ServerList);

    /// <summary>Build the "request login notice/news" packet (<c>LS_NEWS</c> = 0xF6).</summary>
    public static Packet BuildNewsRequest() => new(LoginOpcode.News);

    /// <summary>
    /// Build the account-login packet (<c>LS_LOGIN_REQ</c> = 0xF3): opcode, then the account id and
    /// password each as an int16 length followed by the raw bytes. Mirrors
    /// <c>MsgSend_AccountLogIn</c> including the original length limits (id &lt; 20, pw &lt; 12).
    /// </summary>
    public static Packet BuildAccountLogin(string account, string password)
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password)
            || account.Length >= 20 || password.Length >= 12)
            throw new ArgumentException("Account/password missing or exceeds the login length limits.");

        var pkt = new Packet(LoginOpcode.LoginReq);
        pkt.DByte();                 // int16 length prefix (MP_AddShort), like the original
        pkt.AppendString(account);
        pkt.AppendString(password);
        return pkt;
    }

    /// <summary>
    /// Parse a game-server-group list packet (port of <c>MsgRecv_GameServerGroupList</c>): a 1-byte
    /// count, then for each server an int16-prefixed IP, an int16-prefixed name and an int16 user count.
    /// The opcode byte must already have been consumed.
    /// </summary>
    public static IReadOnlyList<GameServerInfo> ParseServerList(Packet packet)
    {
        packet.SyncForRead();
        packet.Read<byte>(); // skip the opcode byte
        packet.DByte();      // int16 length-prefixed strings
        int count = packet.Read<byte>();
        var servers = new List<GameServerInfo>(count);

        for (int i = 0; i < count; i++)
        {
            packet.ReadString(out string ip);
            packet.ReadString(out string name);
            short users = packet.Read<short>();
            servers.Add(new GameServerInfo(ip, name, users));
        }

        return servers;
    }
}
