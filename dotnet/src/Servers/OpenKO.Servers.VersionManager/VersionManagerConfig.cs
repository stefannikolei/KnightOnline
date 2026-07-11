using System.Text;
using OpenKO.Core.Config;
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
/// Port of <c>VersionManagerApp::LoadConfig</c> — reads the same Version.ini
/// (sections [DOWNLOAD], [ODBC], [SERVER_LIST], [NEWS]) with the same defaults,
/// so a C++ deployment config works unchanged.
/// </summary>
public sealed class VersionManagerConfig
{
    public const int MaxUser = 3000;
    public const int ListenPort = 15100;

    public required byte[] FtpUrl { get; init; }

    public required byte[] FtpPath { get; init; }

    public required string DataSourceName { get; init; }

    public required string DataSourceUser { get; init; }

    public required string DataSourcePassword { get; init; }

    public string? DataSourceServer { get; init; }

    public required List<ServerInfo> Servers { get; init; }

    /// <summary>Pre-assembled news blob (contains embedded NULs; raw bytes only).</summary>
    public required byte[] News { get; init; }

    /// <summary>
    /// Loads and validates the configuration. Returns null when validation fails
    /// (the C++ logs and refuses to start).
    /// </summary>
    public static VersionManagerConfig? Load(IniFile ini, Action<string> logError)
    {
        // C++ buffers: _ftpUrl[256], _ftpPath[256], char[20] for IP/name (snprintf-truncated).
        byte[] ftpUrl = IniBytes(ini.GetString("DOWNLOAD", "URL", "127.0.0.1"), 255);
        byte[] ftpPath = IniBytes(ini.GetString("DOWNLOAD", "PATH", "/"), 255);

        string dsn = ini.GetString("ODBC", "DSN", "KN_online");
        string uid = ini.GetString("ODBC", "UID", "knight");
        string pwd = ini.GetString("ODBC", "PWD", "knight");
        // .NET port extension: SqlClient needs a host, ODBC DSNs carried that
        // out-of-band. Optional key, default localhost / OPENKO_DB_SERVER.
        string server = ini.GetString("ODBC", "SERVER", "");

        int serverCount = ini.GetInt("SERVER_LIST", "COUNT", 1);

        if (ftpUrl.Length == 0)
        {
            logError("VersionManagerConfig: The FTP URL must be set.");
            return null;
        }

        if (ftpPath.Length == 0)
        {
            logError("VersionManagerConfig: The FTP path must be set.");
            return null;
        }

        if (dsn.Length == 0 || uid.Length == 0 || pwd.Length == 0)
        {
            logError("VersionManagerConfig: Datasource config must be set.");
            return null;
        }

        if (serverCount <= 0)
        {
            logError("VersionManagerConfig: At least 1 server must exist in the server list.");
            return null;
        }

        var servers = new List<ServerInfo>(serverCount);
        for (int i = 0; i < serverCount; i++)
        {
            servers.Add(new ServerInfo
            {
                ServerIP = IniBytes(ini.GetString("SERVER_LIST", $"SERVER_{i:D2}", "127.0.0.1"), 19),
                ServerName = IniBytes(ini.GetString("SERVER_LIST", $"NAME_{i:D2}", "TEST|Server 1"), 19),
                ServerId = (short)ini.GetInt("SERVER_LIST", $"ID_{i:D2}", 1),
                UserLimit = (short)ini.GetInt("SERVER_LIST", $"USER_LIMIT_{i:D2}", MaxUser),
            });
        }

        // News blob: title + NEWS_MESSAGE_START + message + NEWS_MESSAGE_END per entry.
        var news = new MemoryStream();
        for (int i = 0; i < ProtocolConstants.MaxNewsCount; i++)
        {
            string title = ini.GetString("NEWS", $"TITLE_{i:D2}", "");
            if (title.Length == 0)
                continue;

            string message = ini.GetString("NEWS", $"MESSAGE_{i:D2}", "");
            if (message.Length == 0)
                continue;

            news.Write(IniBytes(title, int.MaxValue));
            news.Write(ProtocolConstants.NewsMessageStart);
            news.Write(IniBytes(message, int.MaxValue));
            news.Write(ProtocolConstants.NewsMessageEnd);
        }

        if (news.Length > 4096) // sizeof(_NEWS::Content)
        {
            logError("VersionManagerConfig: News too long");
            return null;
        }

        return new VersionManagerConfig
        {
            FtpUrl = ftpUrl,
            FtpPath = ftpPath,
            DataSourceName = dsn,
            DataSourceUser = uid,
            DataSourcePassword = pwd,
            DataSourceServer = server.Length > 0 ? server : null,
            Servers = servers,
            News = news.ToArray(),
        };
    }

    /// <summary>
    /// INI strings are loaded byte-preserving (Latin1); convert back to the raw
    /// bytes that go on the wire, truncated like the C++ snprintf into char[N].
    /// </summary>
    private static byte[] IniBytes(string value, int maxLength)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(value);
        return bytes.Length <= maxLength ? bytes : bytes[..maxLength];
    }
}
