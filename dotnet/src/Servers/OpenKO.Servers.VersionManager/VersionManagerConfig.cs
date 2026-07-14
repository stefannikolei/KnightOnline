using System.Text;
using OpenKO.Core.Protocol;

namespace OpenKO.Servers.VersionManager;

public sealed class ServerInfo
{
    /// <summary>Raw bytes as sent on the wire (C++ char[20] → max 19 bytes + NUL).</summary>
    public required byte[] ServerIP { get; init; }

    public required byte[] ServerName { get; init; }

    public short ServerId { get; init; } = 1;

    public short UserLimit { get; init; }

    /// <summary>Refreshed from the CONCURRENT table on every LS_SERVERLIST request.</summary>
    public short UserCount { get; set; }
}

/// <summary>
/// The compiled, wire-ready version-server config (the C++
/// <c>VersionManagerApp::LoadConfig</c> result): FTP info, the server list and
/// the pre-assembled news blob. Built from the bound <see cref="VersionManagerOptions"/>
/// with the same byte truncation and news layout as the original.
/// </summary>
public sealed class VersionManagerConfig
{
    public const int MaxUser = 3000;
    public const int ListenPort = 15100;

    public required byte[] FtpUrl { get; init; }

    public required byte[] FtpPath { get; init; }

    public required List<ServerInfo> Servers { get; init; }

    /// <summary>Pre-assembled news blob (contains embedded NULs; raw bytes only).</summary>
    public required byte[] News { get; init; }

    /// <summary>
    /// Compiles the bound options into the wire-ready config. Throws
    /// <see cref="InvalidOperationException"/> when the news blob overflows the
    /// C++ <c>_NEWS::Content</c> buffer (the other invariants — non-empty FTP
    /// fields, at least one server — are enforced by DataAnnotations on the
    /// options with <c>ValidateOnStart</c>).
    /// </summary>
    public static VersionManagerConfig FromOptions(VersionManagerOptions options)
    {
        // C++ buffers: _ftpUrl[256], _ftpPath[256], char[20] for IP/name (snprintf-truncated).
        byte[] ftpUrl = WireBytes(options.Download.Url, 255);
        byte[] ftpPath = WireBytes(options.Download.Path, 255);

        var servers = new List<ServerInfo>(options.ServerList.Count);
        foreach (ServerListEntry entry in options.ServerList)
        {
            servers.Add(new ServerInfo
            {
                ServerIP = WireBytes(entry.Ip, 19),
                ServerName = WireBytes(entry.Name, 19),
                ServerId = entry.Id,
                UserLimit = entry.UserLimit,
            });
        }

        // News blob: title + NEWS_MESSAGE_START + message + NEWS_MESSAGE_END per entry.
        var news = new MemoryStream();
        int count = 0;
        foreach (NewsEntry entry in options.News)
        {
            if (count >= ProtocolConstants.MaxNewsCount)
                break;
            if (entry.Title.Length == 0 || entry.Message.Length == 0)
                continue;

            news.Write(WireBytes(entry.Title, int.MaxValue));
            news.Write(ProtocolConstants.NewsMessageStart);
            news.Write(WireBytes(entry.Message, int.MaxValue));
            news.Write(ProtocolConstants.NewsMessageEnd);
            count++;
        }

        if (news.Length > 4096) // sizeof(_NEWS::Content)
            throw new InvalidOperationException("VersionManagerConfig: News too long");

        return new VersionManagerConfig
        {
            FtpUrl = ftpUrl,
            FtpPath = ftpPath,
            Servers = servers,
            News = news.ToArray(),
        };
    }

    /// <summary>
    /// Convert a config string to the raw bytes that go on the wire (Latin1),
    /// truncated like the C++ snprintf into char[N].
    /// </summary>
    private static byte[] WireBytes(string value, int maxLength)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(value);
        return bytes.Length <= maxLength ? bytes : bytes[..maxLength];
    }
}
