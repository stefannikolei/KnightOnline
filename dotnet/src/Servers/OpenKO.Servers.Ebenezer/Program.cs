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
        List<OpenKO.Data.Models.Magic>? magics = await db.LoadMagicTableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType1>? magicType1 = await db.LoadMagicType1TableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType2>? magicType2 = await db.LoadMagicType2TableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType3>? magicType3 = await db.LoadMagicType3TableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType4>? magicType4 = await db.LoadMagicType4TableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType5>? magicType5 = await db.LoadMagicType5TableAsync(stoppingToken);
        List<OpenKO.Data.Models.MagicType8>? magicType8 = await db.LoadMagicType8TableAsync(stoppingToken);
        List<OpenKO.Data.Models.ServerResource>? resources = await db.LoadServerResourceTableAsync(stoppingToken);
        List<OpenKO.Data.Models.StartPosition>? startPositions = await db.LoadStartPositionTableAsync(stoppingToken);
        List<OpenKO.Data.Models.KnightsRow>? knights = await db.LoadKnightsTableAsync(stoppingToken);
        List<OpenKO.Data.Models.KnightsUserRow>? knightsUsers = await db.LoadKnightsUserTableAsync(stoppingToken);
        List<OpenKO.Data.Models.EventTriggerRow>? eventTriggers = await db.LoadEventTriggerTableAsync(stoppingToken);
        if (coefficients is null || zoneInfos is null || items is null || levels is null || homes is null
            || magics is null || magicType1 is null || magicType2 is null || magicType3 is null
            || magicType4 is null || magicType5 is null || magicType8 is null || resources is null
            || startPositions is null || knights is null || knightsUsers is null || eventTriggers is null)
        {
            logger.LogError("Ebenezer startup table load failed, closing server");
            lifetime.StopApplication();
            return;
        }

        World.CoefficientTable = coefficients.ToDictionary(c => c.ClassId);
        World.ItemTable = items.ToDictionary(i => i.ID);
        World.LevelUpTable = levels.ToDictionary(l => (int)l.Level, l => l.RequiredExp);
        World.HomeTable = homes.ToDictionary(h => h.Nation);
        World.MagicTable = magics.ToDictionary(m => m.ID);
        World.MagicType1Table = magicType1.ToDictionary(m => m.ID);
        World.MagicType2Table = magicType2.ToDictionary(m => m.ID);
        World.MagicType3Table = magicType3.ToDictionary(m => m.ID);
        World.MagicType4Table = magicType4.ToDictionary(m => m.ID);
        World.MagicType5Table = magicType5.ToDictionary(m => m.ID);
        World.MagicType8Table = magicType8.ToDictionary(m => m.ID);
        World.ServerResources = resources.ToDictionary(r => r.ResourceId, r => r.Resource);
        World.StartPositionTable = startPositions.ToDictionary(sp => sp.ZoneId);

        // EbenezerApp::LoadAllKnights + LoadAllKnightsUserData.
        foreach (OpenKO.Data.Models.KnightsRow row in knights)
        {
            World.Knights[row.Id] = new KnightsClan
            {
                Index = row.Id,
                Flag = row.Flag,
                Nation = row.Nation,
                Name = row.Name,
                Chief = row.Chief,
                ViceChief1 = row.ViceChief1,
                ViceChief2 = row.ViceChief2,
                ViceChief3 = row.ViceChief3,
                Members = row.Members,
                Money = row.Gold,
                AllianceKnights = row.AllianceKnights,
                MarkVersion = row.MarkVersion,
                Cape = row.Cape,
                Domination = row.Domination,
                Points = row.Points,
                Grade = EbenezerWorld.GetKnightsGrade(row.Points),
                Ranking = row.Ranking,
            };
        }

        foreach (OpenKO.Data.Models.KnightsUserRow row in knightsUsers)
            World.AddKnightsUser(row.KnightsId, row.UserId);

        // EbenezerApp::LoadEventTriggerTable.
        foreach (OpenKO.Data.Models.EventTriggerRow row in eventTriggers)
        {
            uint key = ((uint)row.NpcType << 16) | (ushort)row.NpcId;
            if (!World.EventTriggers.TryAdd(key, row.TriggerNumber))
                logger.LogError("EVENT_TRIGGER: duplicate entry [NpcType={NpcType} NpcId={NpcId}]", row.NpcType, row.NpcId);
        }

        // EbenezerApp::MapFileLoad — read the .smd maps from MAP/<name> for the
        // real map extents and object events. Unlike the C++ (which aborts), a
        // missing map file degrades to a 1x1-region stub zone so the server
        // stays usable without the game assets checked out.
        string mapDir = KoHost.ResolveConfigPath("MAP");
        foreach (OpenKO.Data.Models.ZoneInfo zone in zoneInfos)
        {
            var gameZone = new GameZone(zone.ServerId, zone.ZoneId);

            string mapPath = Path.Combine(mapDir, zone.Name);
            if (File.Exists(mapPath))
            {
                try
                {
                    var map = OpenKO.GameData.Maps.GameMap.Load(mapPath);
                    gameZone = new GameZone(zone.ServerId, zone.ZoneId,
                        (map.MapSize - 1) * map.UnitDistance);

                    foreach (OpenKO.GameData.Maps.ObjectEvent objectEvent in map.ObjectEvents)
                    {
                        gameZone.ObjectEvents[objectEvent.Index] = new ObjectEvent
                        {
                            Index = objectEvent.Index,
                            Type = objectEvent.Type,
                            Belong = objectEvent.Belong,
                            ControlNpcId = objectEvent.ControlNpcId,
                            Life = 1, // C3DMap::LoadObjectEvent marks every event alive
                            PosX = objectEvent.PosX,
                            PosZ = objectEvent.PosZ,
                        };
                    }

                    foreach (OpenKO.GameData.Maps.WarpInfo warp in map.Warps)
                    {
                        gameZone.Warps[warp.WarpId] = new WarpInfo
                        {
                            WarpId = warp.WarpId,
                            WarpName = warp.WarpName,
                            Announce = warp.Announce,
                            Pay = warp.Pay,
                            Zone = warp.Zone,
                            X = warp.X,
                            Y = warp.Y,
                            Z = warp.Z,
                            R = warp.R,
                            Nation = warp.Nation,
                        };
                    }

                    foreach (OpenKO.GameData.Maps.RegeneEvent regene in map.RegeneEvents)
                    {
                        gameZone.RegeneEvents.Add(new RegeneEvent
                        {
                            PosX = regene.PosX,
                            PosZ = regene.PosZ,
                            AreaX = regene.AreaX,
                            AreaZ = regene.AreaZ,
                        });
                    }

                    logger.LogInformation("zone {Zone}: loaded {Map} ({Size} regions, {Events} object events)",
                        zone.ZoneId, zone.Name, gameZone.XRegionMax + 1, gameZone.ObjectEvents.Count);
                }
                catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
                {
                    logger.LogError(ex, "zone {Zone}: map load failed for {Map}, using a stub zone", zone.ZoneId, zone.Name);
                }
            }
            else
            {
                logger.LogWarning("zone {Zone}: map file {Map} not found, using a stub zone", zone.ZoneId, zone.Name);
            }

            // The C++ divides the ZONE_INFO spawn point by 100.
            gameZone.InitX = (float)(zone.InitX / 100.0);
            gameZone.InitZ = (float)(zone.InitZ / 100.0);
            gameZone.Type = zone.Type;
            World.Zones.Add(gameZone);

            // EbenezerApp::MapFileLoad also reads QUESTS/<zone>.evt per zone.
            string questPath = Path.Combine(KoHost.ResolveConfigPath("QUESTS"), $"{zone.ZoneId}.evt");
            if (File.Exists(questPath))
            {
                Dictionary<int, QuestEventData>? questEvents =
                    QuestEventFile.Load(questPath, zone.ZoneId, logger);
                if (questEvents is not null)
                {
                    World.QuestEvents[zone.ZoneId] = questEvents;
                    logger.LogInformation("zone {Zone}: loaded {Count} quest events", zone.ZoneId, questEvents.Count);
                }
            }
        }

        Channel<Func<ValueTask>> queue = Channel.CreateUnbounded<Func<ValueTask>>(
            new UnboundedChannelOptions { SingleReader = true });

        World.SendToAiServer = World.SendAiServer;
        World.UserAccept = () => _userAccept.TrySetResult();

        // The Aujard-queue messages become direct DB-agent calls on the game loop.
        World.SaveUserData = user => queue.Writer.TryWrite(async () =>
        {
            if (user.UserData is { } data && data.CharId.Length > 0)
                await dbAgent.UpdateUserAsync(data.CharId, user.SocketId, UserUpdateType.PacketSave, stoppingToken);
        });
        World.KickOutRequested = accountId => queue.Writer.TryWrite(async () =>
        {
            await dbAgent.AccountLogoutAsync(accountId, cancellationToken: stoppingToken);
        });
        World.SaveBattleResult = (charId, nation) => queue.Writer.TryWrite(async () =>
        {
            await dbAgent.UpdateBattleEventAsync(charId, nation, stoppingToken);
        });
        World.DailyKnightsRankRefresh = () => queue.Writer.TryWrite(async () =>
        {
            // AujardApp::AllKnightsList + CKnightsManager::RecvKnightsAllList.
            List<OpenKO.Data.Models.KnightsRankingEntry> ranking =
                await dbAgent.LoadKnightsRankingAsync(World.ServerNo, stoppingToken);
            World.ApplyKnightsRankUpdates(ranking.Select(r => (r.Id, r.Points, r.Ranking)));
        });

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
                // CUser::CloseProcess order: leave the world, the party, the trade.
                session.User.UserInOut(GameUser.UserOut);
                if (session.User.PartyIndex != -1)
                    session.User.PartyRemoveMember(session.User.SocketId);
                if (session.User.ExchangeUser != -1)
                    session.User.ExchangeCancel();
                session.User.MarketBbsUserDelete();
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
                // EbenezerApp::GameTimeTick: one game minute + the battle timer,
                // then the AI-socket alive check.
                World.UpdateGameTime();
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
