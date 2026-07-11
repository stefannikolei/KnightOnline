using System.Net;
using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.TestClient;

/// <summary>
/// Scripted login-protocol client for manual verification and the parity harness:
///   OpenKO.TestClient <host:port> version
///   OpenKO.TestClient <host:port> downloadinfo <clientVersion>
///   OpenKO.TestClient <host:port> serverlist
///   OpenKO.TestClient <host:port> news
///   OpenKO.TestClient <host:port> login <accountId> <password>
/// Prints the raw response payload as hex (for byte-diffing between servers).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: OpenKO.TestClient <host:port> <version|downloadinfo|serverlist|news|login> [args]");
            return 1;
        }

        string[] hostPort = args[0].Split(':');
        var endPoint = new IPEndPoint(IPAddress.Parse(hostPort[0]), int.Parse(hostPort[1]));

        byte[] request = BuildRequest(args);

        using var client = new KoTestClient();
        await client.ConnectAsync(endPoint);
        byte[] response = await client.RequestAsync(request);

        Console.WriteLine(Convert.ToHexStringLower(response));
        return 0;
    }

    private static byte[] BuildRequest(string[] args)
    {
        switch (args[1])
        {
            case "version":
                return [(byte)LoginOpcode.LS_VERSION_REQ];

            case "downloadinfo":
            {
                var buffer = new byte[3];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)LoginOpcode.LS_DOWNLOADINFO_REQ);
                writer.SetShort(short.Parse(args[2]));
                return writer.Written.ToArray();
            }

            case "serverlist":
                return [(byte)LoginOpcode.LS_SERVERLIST];

            case "news":
                return [(byte)LoginOpcode.LS_NEWS];

            case "login":
            {
                byte[] id = Encoding.ASCII.GetBytes(args[2]);
                byte[] pw = Encoding.ASCII.GetBytes(args.Length > 3 ? args[3] : "");
                var buffer = new byte[1 + 2 + id.Length + 2 + pw.Length];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)LoginOpcode.LS_LOGIN_REQ);
                writer.SetString2(id);
                writer.SetString2(pw);
                return writer.Written.ToArray();
            }

            default:
                throw new ArgumentException($"unknown command: {args[1]}");
        }
    }
}
