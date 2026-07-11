using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// Port of <c>CUser::Parsing / LogInReq / SendDownloadInfo / NewsReq</c>
/// (Server/VersionManager/User.cpp). Produces the exact response payloads;
/// framing/crypto is not involved (the login protocol is plaintext).
/// Session-independent and stateless apart from the shared server state.
/// </summary>
public sealed class LoginPacketHandler(VersionManagerState state, IVersionManagerDb db)
{
    private static readonly byte[] LoginNoticeHeader = "Login Notice"u8.ToArray();
    private static readonly byte[] EmptyNews = "<empty>"u8.ToArray();

    // LS_SERVERLIST refreshes shared user counts before responding; the C++
    // serialized all parsing behind a recursive mutex, so serialize here too.
    private readonly SemaphoreSlim _serverListLock = new(1, 1);

    /// <summary>Handles one payload; returns the response payload or null (unknown opcode).</summary>
    public async ValueTask<byte[]?> HandleAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        try
        {
            return (LoginOpcode)payload[0] switch
            {
                LoginOpcode.LS_VERSION_REQ => HandleVersionReq(),
                LoginOpcode.LS_SERVERLIST => await HandleServerListAsync(cancellationToken),
                LoginOpcode.LS_DOWNLOADINFO_REQ => HandleDownloadInfo(payload),
                LoginOpcode.LS_LOGIN_REQ => await HandleLoginAsync(payload, cancellationToken),
                LoginOpcode.LS_NEWS => HandleNews(),
                _ => null,
            };
        }
        catch (IndexOutOfRangeException)
        {
            // Truncated packet: the C++ would read out of bounds; we drop it.
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private byte[] HandleVersionReq()
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)LoginOpcode.LS_VERSION_REQ);
        writer.SetShort(state.LastVersion);
        return writer.Written.ToArray();
    }

    private async ValueTask<byte[]> HandleServerListAsync(CancellationToken cancellationToken)
    {
        await _serverListLock.WaitAsync(cancellationToken);
        try
        {
            // "기범이가 ^^;" — refresh the counts from CONCURRENT first, like the C++.
            List<ConcurrentRow>? counts = await db.LoadUserCountsAsync(cancellationToken);
            if (counts is not null)
            {
                foreach (ConcurrentRow row in counts)
                {
                    int serverIndex = row.ServerId - 1;
                    if (serverIndex < 0 || serverIndex >= state.Servers.Count)
                        continue;

                    state.Servers[serverIndex].UserCount =
                        (short)(row.Zone1Count + row.Zone2Count + row.Zone3Count);
                }
            }

            var buffer = new byte[2048];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)LoginOpcode.LS_SERVERLIST);
            writer.SetByte((byte)state.Servers.Count);

            foreach (ServerInfo server in state.Servers)
            {
                writer.SetString2(server.ServerIP);
                writer.SetString2(server.ServerName);

                if (server.UserCount <= server.UserLimit)
                    writer.SetShort(server.UserCount);
                else
                    writer.SetShort(-1);
            }

            return writer.Written.ToArray();
        }
        finally
        {
            _serverListLock.Release();
        }
    }

    private byte[] HandleDownloadInfo(byte[] payload)
    {
        var reader = new PacketReader(payload) { Index = 1 };
        int clientVersion = reader.GetShort();

        // std::set<std::string>: deduplicated, byte-wise (ordinal) sorted.
        var downloadSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (VersionRow row in state.VersionList)
        {
            if (row.Number > clientVersion)
                downloadSet.Add(row.CompressName);
        }

        var buffer = new byte[2048];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)LoginOpcode.LS_DOWNLOADINFO_REQ);
        writer.SetString2(state.FtpUrl);
        writer.SetString2(state.FtpPath);
        writer.SetShort(downloadSet.Count);

        foreach (string fileName in downloadSet)
            writer.SetString2(Encoding.Latin1.GetBytes(fileName));

        return writer.Written.ToArray();
    }

    private async ValueTask<byte[]> HandleLoginAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var reader = new PacketReader(payload) { Index = 1 };

        // idlen: (0, 20]; pwdlen: [0, 12] — anything else is AUTH_NOT_FOUND (fail_return).
        int idLen;
        int pwdLen;
        byte[] accountId;
        byte[] password;
        try
        {
            idLen = reader.GetShort();
            if (idLen > ProtocolConstants.MaxIdSize || idLen <= 0)
                return LoginFail();

            accountId = reader.GetString(idLen).ToArray();

            pwdLen = reader.GetShort();
            if (pwdLen > ProtocolConstants.MaxPwSize || pwdLen < 0)
                return LoginFail();

            password = reader.GetString(pwdLen).ToArray();
        }
        catch (ArgumentOutOfRangeException)
        {
            return LoginFail();
        }

        string accountIdStr = Encoding.Latin1.GetString(accountId);
        string passwordStr = Encoding.Latin1.GetString(password);

        AuthResult result = await db.AccountLoginAsync(accountIdStr, passwordStr, cancellationToken);

        if (result != AuthResult.AUTH_OK)
            return BuildLoginResponse(result, currentUser: null, premiumDays: 0);

        CurrentUser? currentUser = await db.GetCurrentUserAsync(accountIdStr, cancellationToken);
        if (currentUser is not null)
            return BuildLoginResponse(AuthResult.AUTH_IN_GAME, currentUser, premiumDays: 0);

        short premiumDays = await db.LoadPremiumServiceUserAsync(accountIdStr, cancellationToken) ?? -1;
        return BuildLoginResponse(AuthResult.AUTH_OK, currentUser: null, premiumDays);
    }

    private static byte[] BuildLoginResponse(AuthResult result, CurrentUser? currentUser, short premiumDays)
    {
        var buffer = new byte[256];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)LoginOpcode.LS_LOGIN_REQ);
        writer.SetByte((byte)result);

        if (result == AuthResult.AUTH_IN_GAME && currentUser is not null)
        {
            // Already in game: point the client at the server to kick from.
            writer.SetString2(Encoding.Latin1.GetBytes(currentUser.ServerIP));
            writer.SetShort(currentUser.ServerId);
        }
        else if (result == AuthResult.AUTH_OK)
        {
            writer.SetShort(premiumDays);
        }

        return writer.Written.ToArray();
    }

    private static byte[] LoginFail()
    {
        return [(byte)LoginOpcode.LS_LOGIN_REQ, (byte)AuthResult.AUTH_NOT_FOUND];
    }

    private byte[] HandleNews()
    {
        var buffer = new byte[8192];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)LoginOpcode.LS_NEWS);
        writer.SetString2(LoginNoticeHeader);

        if (state.News.Length > 0)
            writer.SetString2(state.News);
        else
            writer.SetString2(EmptyNews);

        return writer.Written.ToArray();
    }
}
