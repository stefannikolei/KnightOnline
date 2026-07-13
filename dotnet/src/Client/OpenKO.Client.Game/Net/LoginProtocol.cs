using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>One entry of the LS_SERVERLIST reply.</summary>
public readonly record struct ServerListEntry(string Ip, string Name, int ConcurrentUsers);

/// <summary>LS_LOGIN_REQ result (AUTH_* codes from the login protocol).</summary>
public readonly record struct AccountLoginResult(byte Result, string? GameServerIp, int Port)
{
    public const byte Ok = 1;
    public const byte NotFound = 2;
    public const byte BadPassword = 3;
    public const byte Maintenance = 4;
    public const byte AlreadyConnected = 5;

    public bool Success => Result == Ok;
}

/// <summary>LS_NEWS reply (label + body; the C++ expects label "Login Notice").</summary>
public readonly record struct NewsResult(string Label, string Content);

/// <summary>
/// The client login-server (VersionManager, port 15100, unencrypted) request
/// builders and reply parsers — the LS_* half of CGameProcLogIn. Payloads are
/// opcode + body; the socket core adds the framing. Field order is pinned
/// against the C# VersionManager/CGameProcLogIn.
/// </summary>
public static class LoginProtocol
{
    // Login IDs are ASCII; the server uses Latin1 for these strings.
    private static readonly Encoding Ascii = Encoding.Latin1;

    public static byte[] BuildServerListRequest() => [(byte)LoginOpcode.LS_SERVERLIST];

    public static byte[] BuildNewsRequest() => [(byte)LoginOpcode.LS_NEWS];

    public static byte[] BuildAccountLogin(string account, string password)
    {
        var buffer = new byte[5 + account.Length + password.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)LoginOpcode.LS_LOGIN_REQ);
        w.SetString2(Ascii.GetBytes(account));
        w.SetString2(Ascii.GetBytes(password));
        return w.Written.ToArray();
    }

    public static IReadOnlyList<ServerListEntry> ParseServerList(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        int count = r.GetByte();
        var list = new List<ServerListEntry>(count);
        for (int i = 0; i < count; i++)
        {
            string ip = Ascii.GetString(r.GetVarString(2));
            string name = Ascii.GetString(r.GetVarString(2));
            int users = r.GetShort();
            list.Add(new ServerListEntry(ip, name, users));
        }

        return list;
    }

    public static AccountLoginResult ParseAccountLogin(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();
        if (result == AccountLoginResult.AlreadyConnected && r.Remaining > 0)
        {
            string ip = Ascii.GetString(r.GetVarString(2));
            int port = r.GetShort();
            return new AccountLoginResult(result, ip, port);
        }

        return new AccountLoginResult(result, null, 0);
    }

    public static NewsResult ParseNews(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        string label = Ascii.GetString(r.GetVarString(2));
        string content = Ascii.GetString(r.GetVarString(2));
        return new NewsResult(label, content);
    }
}
