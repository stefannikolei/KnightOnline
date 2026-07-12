using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Config;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Hosting;
using OpenKO.Network.Tcp;
using OpenKO.Servers.Aujard;
using OpenKO.Servers.Ebenezer.Net;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Ebenezer host (stage-4.1 slice): reads the same server.ini as the C++
/// ([ZONE_INFO] MY_INFO → listen port 15000+n, [ODBC] GAME_DSN/UID/PWD) and
/// accepts game clients with the CUser socket layer (framing + WIZ_CRYPTION).
/// The Aujard DB agent runs embedded as a library — the KNIGHT_SEND/RECV
/// shared-memory queues of the C++ topology are replaced by direct calls.
/// </summary>
public static class Program
{
    public const int ListenPortBase = 15000; // _LISTEN_PORT

    public static async Task<int> Main(string[] args)
    {
        var builder = KoHost.CreateBuilder(args);

        var ini = new IniFile();
        ini.Load(KoHost.ResolveConfigPath("server.ini"));

        int serverNo = ini.GetInt("ZONE_INFO", "MY_INFO", 1);
        string dsn = ini.GetString("ODBC", "GAME_DSN", "KN_online");
        string uid = ini.GetString("ODBC", "GAME_UID", "knight");
        string pwd = ini.GetString("ODBC", "GAME_PWD", "knight");
        string server = ini.GetString("ODBC", "SERVER", "");
        string aiServerIp = ini.GetString("AI_SERVER", "IP", "127.0.0.1");

        int listenPort = ListenPortBase + serverNo;

        // [ZONE_INFO] SERVER_XX / SERVER_IP_XX entries (port = 15000 + server no).
        var serverInfos = new List<ZoneServerInfo>();
        int serverCount = ini.GetInt("ZONE_INFO", "SERVER_COUNT", 1);
        for (int i = 0; i < serverCount; i++)
        {
            short no = (short)ini.GetInt("ZONE_INFO", $"SERVER_{i:00}", 1);
            string ip = ini.GetString("ZONE_INFO", $"SERVER_IP_{i:00}", "127.0.0.1");
            serverInfos.Add(new ZoneServerInfo(no, ip, (short)(ListenPortBase + no)));
        }

        builder.Services.AddSingleton(SqlConnectionFactory.FromOdbcConfig(
            dsn, uid, pwd, server.Length > 0 ? server : null));
        builder.Services.AddSingleton<IDbAgent, DbAgent>(sp => new DbAgent(
            sp.GetRequiredService<SqlConnectionFactory>(),
            sp.GetRequiredService<ILogger<DbAgent>>()));
        builder.Services.AddSingleton(sp => new EbenezerService(
            listenPort,
            (short)serverNo,
            serverInfos,
            aiServerIp,
            sp.GetRequiredService<SqlConnectionFactory>(),
            sp.GetRequiredService<IDbAgent>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<EbenezerService>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EbenezerService>());

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}

/// <summary>
/// Accepts game-client connections and runs the single-writer game loop: every
/// received chunk (game clients and AI links alike) is queued and dispatched on
/// one loop, preserving the serialization the C++ enforced with its recursive
/// mutexes. Like the C++, game clients are only accepted after the AIServer has
/// delivered the NPC data for every zone (UserAcceptThread).
/// </summary>
public sealed class EbenezerService(
    int listenPort,
    short serverNo,
    IReadOnlyList<ZoneServerInfo> serverInfos,
    string aiServerIp,
    SqlConnectionFactory connectionFactory,
    IDbAgent dbAgent,
    IHostApplicationLifetime lifetime,
    ILogger<EbenezerService> logger) : BackgroundService
{
    // AI_KARUS/ELMO/BATTLE_SOCKET_PORT by server number (KARUS=1, ELMORAD=2, BATTLE=3).
    public const int AiKarusPort = 10020;
    public const int AiElmoPort = 10030;
    public const int AiBattlePort = 10040;

    private TcpListener? _listener;

    private readonly TaskCompletionSource _userAccept =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EbenezerWorld World { get; } = new();

    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.Server.LocalEndPoint;

    private int GetAiServerPort() => serverNo switch
    {
        1 => AiKarusPort,
        2 => AiElmoPort,
        3 => AiBattlePort,
        _ => -1,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await dbAgent.InitAsync(stoppingToken))
        {
            logger.LogError("Database Connection Fail!!");
            lifetime.StopApplication();
            return;
        }

        World.ServerNo = serverNo;
        foreach (ZoneServerInfo info in serverInfos)
            World.ServerInfos[info.ServerNo] = info;

        // Startup tables (EbenezerApp::OnStart slice for the pre-game flow).
        var db = new Db.EbenezerDb(connectionFactory, logger);

        List<OpenKO.Data.Models.Coefficient>? coefficients = await db.LoadCoefficientTableAsync(stoppingToken);
        List<OpenKO.Data.Models.ZoneInfo>? zoneInfos = await db.LoadZoneInfoTableAsync(stoppingToken);
        List<OpenKO.Data.Models.Item>? items = await db.LoadItemTableAsync(stoppingToken);
        List<OpenKO.Data.Models.LevelUp>? levels = await db.LoadLevelUpTableAsync(stoppingToken);
        List<OpenKO.Data.Models.Home>? homes = await db.LoadHomeTableAsync(stoppingToken);
        if (coefficients is null || zoneInfos is null || items is null || levels is null || homes is null)
        {
            logger.LogError("Ebenezer startup table load failed, closing server");
            lifetime.StopApplication();
            return;
        }

        World.CoefficientTable = coefficients.ToDictionary(c => c.ClassId);
        World.ItemTable = items.ToDictionary(i => i.ID);
        World.LevelUpTable = levels.ToDictionary(l => (int)l.Level, l => l.RequiredExp);
        World.HomeTable = homes.ToDictionary(h => h.Nation);
        foreach (OpenKO.Data.Models.ZoneInfo zone in zoneInfos)
            World.Zones.Add(new GameZone(zone.ServerId, zone.ZoneId));

        Channel<Func<ValueTask>> queue = Channel.CreateUnbounded<Func<ValueTask>>(
            new UnboundedChannelOptions { SingleReader = true });

        World.SendToAiServer = World.SendAiServer;
        World.UserAccept = () => _userAccept.TrySetResult();

        // EbenezerApp::AIServerConnect — one link per socket index; a failure
        // aborts startup like the C++ OnStart.
        for (int i = 0; i < EbenezerWorld.MaxAiSocket; i++)
        {
            if (!await AiSocketConnectAsync(i, reconnect: false, queue.Writer, stoppingToken))
            {
                logger.LogError("AI Server connection failed (zone {Zone}, {Ip}:{Port}), closing server",
                    i, aiServerIp, GetAiServerPort());
                lifetime.StopApplication();
                return;
            }
        }

        _listener = new TcpListener(IPAddress.Any, listenPort);

        Task acceptLoop = AcceptLoopAsync(queue.Writer, stoppingToken);
        Task gameLoop = GameLoopAsync(queue, stoppingToken);

        await Task.WhenAll(acceptLoop, gameLoop);
    }

    /// <summary>
    /// EbenezerApp::AISocketConnect — connect one AI link and send the
    /// AI_SERVER_CONNECT handshake. Registration into World.AiSockets happens
    /// inline (startup runs before the game loop, reconnects enqueue).
    /// </summary>
    private async Task<bool> AiSocketConnectAsync(
        int index, bool reconnect, ChannelWriter<Func<ValueTask>> queue, CancellationToken ct)
    {
        int port = GetAiServerPort();
        if (port < 0)
        {
            logger.LogError("AiSocketConnect: unsupported server number {ServerNo} (zone {Zone})", serverNo, index);
            return false;
        }

        var client = new KoTcpClient(logger);
        try
        {
            await client.ConnectAsync(new IPEndPoint(IPAddress.Parse(aiServerIp), port), ct);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or FormatException)
        {
            logger.LogError("AiSocketConnect: failed to connect to AI server (zone {Zone}) ({Ip}:{Port}): {Error}",
                index, aiServerIp, port, ex.Message);
            await client.DisposeAsync();
            return false;
        }

        var link = new AiLink(index, World, logger) { Transmit = payload => client.Send(payload) };
        client.OnPacket = (_, packet) =>
        {
            queue.TryWrite(() =>
            {
                link.Parsing(packet);
                return ValueTask.CompletedTask;
            });
            return ValueTask.CompletedTask;
        };

        link.Send([AiOpcode.AI_SERVER_CONNECT, (byte)index, reconnect ? (byte)1 : (byte)0]);
        World.AiSockets[index] = link;

        _ = RunAiLinkAsync(client, link, queue, ct);

        logger.LogDebug("AiSocketConnect: connected to zone {Zone}", index);
        return true;
    }

    private async Task RunAiLinkAsync(
        KoTcpClient client, AiLink link, ChannelWriter<Func<ValueTask>> queue, CancellationToken ct)
    {
        try
        {
            await client.RunAsync(ct);
        }
        finally
        {
            await client.DisposeAsync();

            // Deregister on the game loop; the 6s tick reconnects.
            queue.TryWrite(() =>
            {
                if (World.AiSockets.TryGetValue(link.SocketIndex, out AiLink? current) && current == link)
                    World.AiSockets.Remove(link.SocketIndex);

                return ValueTask.CompletedTask;
            });
        }
    }

    private async Task AcceptLoopAsync(ChannelWriter<Func<ValueTask>> queue, CancellationToken ct)
    {
        // UserAcceptThread: accepting starts only after SERVER_INFO_END for all zones.
        try
        {
            await _userAccept.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            queue.TryComplete();
            return;
        }

        _listener!.Start(backlog: 512);
        logger.LogInformation("Listening on 0.0.0.0:{Port}", ((IPEndPoint)_listener.Server.LocalEndPoint!).Port);

        while (!ct.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener!.AcceptSocketAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            short socketId = World.Register(id => new GameUser(id, World, dbAgent, logger));
            if (socketId < 0)
            {
                logger.LogWarning("server full, rejecting {Remote}", socket.RemoteEndPoint);
                socket.Dispose();
                continue;
            }

            var session = new GameSession(socket, World.Users[socketId]!, logger);
            logger.LogInformation("user {Id} connected from {Remote}", socketId, socket.RemoteEndPoint);

            _ = RunSessionAsync(session, queue, ct);
        }

        queue.TryComplete();
    }

    private async Task RunSessionAsync(GameSession session, ChannelWriter<Func<ValueTask>> queue, CancellationToken ct)
    {
        try
        {
            await session.ReceiveLoopAsync(
                data => queue.TryWrite(() => session.ProcessReceivedAsync(data)), ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "user {Id}: receive loop ended", session.User.SocketId);
        }
        finally
        {
            // Close notification runs on the game loop too (CloseProcess ordering).
            queue.TryWrite(() =>
            {
                session.User.UserInOut(GameUser.UserOut);
                World.Unregister(session.User.SocketId);
                logger.LogInformation("user {Id} disconnected", session.User.SocketId);
                session.Dispose();
                return ValueTask.CompletedTask;
            });
        }
    }

    private async Task GameLoopAsync(Channel<Func<ValueTask>> queue, CancellationToken ct)
    {
        const double regionFlushInterval = 0.2; // SendWorkerThread's 200ms cadence
        const double aiCheckInterval = 6.0;     // the C++ GameTimeTick TimerThread (6s)
        double lastRegionFlush = 0.0;
        double lastAiCheck = 0.0;

        while (!ct.IsCancellationRequested)
        {
            while (queue.Reader.TryRead(out Func<ValueTask>? work))
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "game loop: work item failed");
                }
            }

            double now = Environment.TickCount64 / 1000.0;
            if (now - lastRegionFlush >= regionFlushInterval)
            {
                lastRegionFlush = now;
                FlushRegionBuffers();
            }

            if (now - lastAiCheck >= aiCheckInterval)
            {
                lastAiCheck = now;
                AiSocketAliveCheck(queue.Writer, ct);
            }

            try
            {
                await Task.Delay(10, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>EbenezerApp::GameTimeTick's AI-socket alive/reconnect sweep.</summary>
    private void AiSocketAliveCheck(ChannelWriter<Func<ValueTask>> queue, CancellationToken ct)
    {
        if (!World.FirstServerFlag)
            return;

        int count = 0;
        for (int i = 0; i < EbenezerWorld.MaxAiSocket; i++)
        {
            if (World.AiSockets.ContainsKey(i))
            {
                count++;
                continue;
            }

            int index = i;
            _ = AiSocketConnectAsync(index, reconnect: true, queue, ct);
        }

        if (count <= 0)
            World.DeleteAllNpcList();
    }

    /// <summary>SendWorkerThread::tick — drains every user's region buffer.</summary>
    private void FlushRegionBuffers()
    {
        foreach (GameUser? user in World.Users)
        {
            if (user is null)
                continue;

            byte[]? packet = user.RegionPacketClear();
            if (packet is null)
                continue;

            if (packet.Length < 500)
                user.Send(packet);
            else
                user.SendCompressingPacket(packet);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _listener?.Stop();
    }
}

/// <summary>
/// One accepted game-client socket: owns the receive loop and a serialized send
/// queue; framing/cryption and dispatch live in <see cref="GameUser"/> and run
/// on the service's single-writer game loop.
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly Socket _socket;
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    public GameUser User { get; }

    public GameSession(Socket socket, GameUser user, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
        User = user;

        if (socket.RemoteEndPoint is System.Net.IPEndPoint remote)
            user.RemoteIp = remote.Address.ToString();

        user.Transmit = frame => _sendQueue.Writer.TryWrite(frame);
        user.Close = () => _cts.Cancel();
    }

    /// <summary>Reads raw chunks off the socket and hands them to the game loop.</summary>
    public async Task ReceiveLoopAsync(Action<byte[]> onData, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        Task sendLoop = SendLoopAsync(linked.Token);

        try
        {
            var buffer = new byte[8192];
            while (!linked.Token.IsCancellationRequested)
            {
                int received = await _socket.ReceiveAsync(buffer.AsMemory(), linked.Token);
                if (received == 0)
                    break;

                onData(buffer.AsSpan(0, received).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            await sendLoop;
        }
    }

    /// <summary>Runs on the game loop: feed, de-frame (PullOutCore) and dispatch.</summary>
    public async ValueTask ProcessReceivedAsync(byte[] data)
    {
        User.Core.Feed(data);

        while (true)
        {
            FrameResult result = User.Core.TryReadPacket(out byte[] packet);
            if (result == FrameResult.NeedMore)
                break;

            if (result == FrameResult.Close)
            {
                _logger.LogWarning("user {Id}: protocol violation, closing", User.SocketId);
                _cts.Cancel();
                break;
            }

            if (packet.Length == 0)
                continue;

            await User.ParsingAsync(packet);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (byte[] frame in _sendQueue.Reader.ReadAllAsync(ct))
                await _socket.SendAsync(frame.AsMemory(), SocketFlags.None, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
    }
}
