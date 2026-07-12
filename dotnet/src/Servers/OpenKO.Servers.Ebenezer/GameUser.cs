using Microsoft.Extensions.Logging;
using OpenKO.Core.Compression;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Servers.Aujard;
using OpenKO.Servers.Ebenezer.Net;

namespace OpenKO.Servers.Ebenezer;

/// <summary>CONNECTION_STATE_* (shared-server/TcpSocket.h).</summary>
public enum ConnectionState : byte
{
    Connected = 1,
    Disconnected = 2,
    GameStart = 3,
}

/// <summary>
/// Port of <c>CUser</c> (Server/Ebenezer/User.cpp) — stage 4.1 covers the socket
/// layer (framing/cryption via <see cref="GameSocketCore"/>), the Parsing
/// dispatch, WIZ_VERSION_CHECK (which enables the cryption) and the login /
/// pre-game account flow routed directly through the Aujard library instead of
/// the KNIGHT_SEND/RECV shared-memory queues.
/// </summary>
public sealed partial class GameUser(short socketId, EbenezerWorld world, IDbAgent dbAgent, ILogger logger)
{
    private const int MaxIdSize = 20;  // MAX_ID_SIZE
    private const int MaxPwSize = 12;  // MAX_PW_SIZE
    private const short Version = 1298; // __VERSION (shared/version.h)

    private readonly HashSet<byte> _unhandledReported = [];

    public GameSocketCore Core { get; } = new();

    public short SocketId { get; } = socketId;

    /// <summary>m_strAccountID — only meaningful between login and character select.</summary>
    public string AccountId = string.Empty;

    /// <summary>Client IP for the CURRENTUSER login record.</summary>
    public string RemoteIp = "127.0.0.1";

    public ConnectionState State = ConnectionState.Connected;

    /// <summary>Sends one framed packet to the socket; wired by the host session.</summary>
    public Func<byte[], bool>? Transmit;

    /// <summary>Requests the socket be closed (CUser::Close), wired by the host session.</summary>
    public Action? Close;

    /// <summary>CUser::Send — frame (+ encrypt) and queue. Returns false like the C++ -1.</summary>
    public bool Send(ReadOnlySpan<byte> payload)
    {
        byte[]? frame = Core.BuildFrame(payload);
        if (frame is null)
            return false;

        return Transmit?.Invoke(frame) ?? false;
    }

    /// <summary>
    /// CUser::SendCompressingPacket — WIZ_COMPRESS_PACKET envelope
    /// [opcode][compLen i16][origLen i16][checksum=0 u32][lzf data];
    /// falls back to a plain send when the compression fails.
    /// </summary>
    public void SendCompressingPacket(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 0 || payload.Length >= 49152)
        {
            logger.LogError("SendCompressingPacket: message length out of bounds [len={Len}]", payload.Length);
            return;
        }

        var compressed = new byte[32000];
        int compLen = Lzf.Compress(payload, compressed);
        if (compLen == 0)
        {
            logger.LogError("SendCompressingPacket: compression failed");
            Send(payload);
            return;
        }

        var buffer = new byte[9 + compLen];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_COMPRESS_PACKET);
        writer.SetShort(compLen);
        writer.SetShort(payload.Length);
        writer.SetDWord(0); // checksum — always 0 in the C++
        writer.SetString(compressed.AsSpan(0, compLen));
        Send(writer.Written);
    }

    /// <summary>CUser::Parsing — the WIZ_* dispatch (stage-4.1 subset).</summary>
    public async ValueTask ParsingAsync(byte[] packet)
    {
        var opcode = (GameOpcode)packet[0];

        switch (opcode)
        {
            case GameOpcode.WIZ_LOGIN:
                await LoginProcessAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_SEL_NATION:
                await SelNationToAgentAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_NEW_CHAR:
                await NewCharToAgentAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_DEL_CHAR:
                await DelCharToAgentAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_SEL_CHAR:
                await SelCharToAgentAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_ALLCHAR_INFO_REQ:
                await AllCharInfoToAgentAsync();
                break;

            case GameOpcode.WIZ_GAMESTART:
                if (State != ConnectionState.GameStart)
                    GameStart(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_MOVE:
                MoveProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ATTACK:
                Attack(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_MAGIC_PROCESS:
                Magic.MagicPacket(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_STATE_CHANGE:
                StateChange(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_REGENE:
                Regene(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_WARP:
                WarpProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_CHAT:
                Chat(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_CHAT_TARGET:
                ChatTargetSelect(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ITEM_MOVE:
                ItemMove(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ITEM_TRADE:
                ItemTrade(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ITEM_GET:
                ItemGet(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_BUNDLE_OPEN_REQ:
                BundleOpenReq(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ITEM_REMOVE:
                ItemRemove(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ITEM_REPAIR:
                ItemRepair(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_NPC_EVENT:
                NpcEvent(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_PARTY:
                PartyProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_WAREHOUSE:
                WarehouseProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ROTATE:
                Rotate(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_REQ_USERIN:
                RequestUserIn(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_REQ_NPCIN:
                RequestNpcIn(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_TARGET_HP:
            {
                var reader = new PacketReader(packet.AsSpan(1));
                short uid = reader.GetShort();
                byte echo = reader.GetByte();
                SendTargetHP(echo, uid, 0);
                break;
            }

            case GameOpcode.WIZ_ZONE_CHANGE:
                RecvZoneChange(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_POINT_CHANGE:
                PointChange(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_EXCHANGE:
                ExchangeProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_SKILLPT_CHANGE:
                SkillPointChange(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_OBJECT_EVENT:
                ObjectEventProcess(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_WEATHER:
            case GameOpcode.WIZ_TIME:
                UpdateGameWeather(packet.AsSpan(1), opcode);
                break;

            case GameOpcode.WIZ_CLASS_CHANGE:
                ClassChange(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_CONCURRENTUSER:
                CountConcurrentUser();
                break;

            case GameOpcode.WIZ_DATASAVE:
                UserDataSaveToAgent();
                break;

            case GameOpcode.WIZ_SPEEDHACK_CHECK:
                SpeedHackTime(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_SERVER_CHECK:
                ServerStatusCheck();
                break;

            case GameOpcode.WIZ_REPORT_BUG:
                ReportBug(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_HOME:
                Home();
                break;

            case GameOpcode.WIZ_WARP_LIST:
                SelectWarpList(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_ZONE_CONCURRENT:
                ZoneConCurrentUsers(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_VIRTUAL_SERVER:
                ServerChangeOk(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_KICKOUT:
                KickOut(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_KNIGHTS_PROCESS:
                await KnightsProcessAsync(packet.AsMemory(1));
                break;

            case GameOpcode.WIZ_CLIENT_EVENT:
                ClientEvent(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_SELECT_MSG:
                RecvSelectMsg(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_EDIT_BOX:
                RecvEditBox(packet.AsSpan(1));
                break;

            case GameOpcode.WIZ_TEST_PACKET:
                TestPacket();
                break;

            case GameOpcode.WIZ_VERSION_CHECK:
                VersionCheck();
                break;

            default:
                // Remaining opcodes are ported in later stage-4 slices.
                if (_unhandledReported.Add((byte)opcode))
                    logger.LogDebug("user {Id}: opcode {Opcode:X2} not yet ported", SocketId, (byte)opcode);
                break;
        }

        // Like the C++, the HP-regeneration and type-3/type-4 duration timers
        // run at the end of every packet dispatch.
        PacketTimerTail((byte)opcode);
    }

    /// <summary>
    /// CUser::VersionCheck — replies with the version and the public cryption
    /// key, then enables the cryption for everything that follows.
    /// </summary>
    public void VersionCheck()
    {
        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_VERSION_CHECK);
        writer.SetShort(Version);
        writer.SetInt64((long)Core.Cryption.PublicKey);
        Send(writer.Written);

        Core.CryptionEnabled = true;
    }

    /// <summary>
    /// CUser::LoginProcess + AujardApp::AccountLogIn + the read-queue reply:
    /// validates, kicks a duplicate account session, asks the DB agent and
    /// replies [WIZ_LOGIN][nation | 0xFF].
    /// </summary>
    public async ValueTask LoginProcessAsync(ReadOnlyMemory<byte> body)
    {
        string accountId;
        string password;
        {
            var reader = new PacketReader(body.Span);

            int idLen = reader.GetShort();
            if (idLen > MaxIdSize || idLen <= 0)
            {
                SendLoginResult(0xFF);
                return;
            }

            accountId = System.Text.Encoding.Latin1.GetString(reader.GetString(idLen));

            int pwdLen = reader.GetShort();
            if (pwdLen > MaxPwSize || pwdLen <= 0)
            {
                SendLoginResult(0xFF);
                return;
            }

            password = System.Text.Encoding.Latin1.GetString(reader.GetString(pwdLen));
        }

        GameUser? existing = world.GetUserByAccount(accountId);
        if (existing is not null && existing.SocketId != SocketId)
        {
            existing.UserDataSaveToAgent();
            existing.Close?.Invoke();
            SendLoginResult(0xFF);
            return;
        }

        AccountId = accountId;

        int nation = await dbAgent.AccountLoginAsync(accountId, password);
        byte result = (byte)nation; // -1 → 0xFF, like the C++ byte write

        if (result == 0xFF)
            AccountId = string.Empty; // the read-queue thread clears it on failure

        SendLoginResult(result);
    }

    private void SendLoginResult(byte result)
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_LOGIN);
        writer.SetByte(result); // nation on success, 0xFF on failure
        Send(writer.Written);
    }

    /// <summary>
    /// CUser::UserDataSaveToAgent — the WIZ_DATASAVE pair: the Aujard save
    /// trigger (via the world's SaveUserData hook, replacing the Logger queue)
    /// and the character snapshot line for the ItemManager.
    /// </summary>
    public void UserDataSaveToAgent()
    {
        if (UserData is not { } user || user.CharId.Length == 0 || user.AccountId.Length == 0)
            return;

        world.SaveUserData?.Invoke(this);

        // The second C++ queue message ([0x02] flag) goes to the item logger.
        SendItemLogDataSave(0x02);
    }
}
